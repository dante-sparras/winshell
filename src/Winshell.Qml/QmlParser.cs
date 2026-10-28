using System.Globalization;
using System.Text;

namespace Winshell.Qml;

public sealed class QmlSyntaxException(string message, SourceLocation location)
    : Exception($"{location}: {message}")
{
    public SourceLocation Location { get; } = location;

    public string Reason { get; } = message;
}

/// <summary>
/// Parses QML documents. The QML structure is parsed here; JavaScript is kept as source text,
/// and only scanned far enough to find where each expression, block or function ends.
/// </summary>
public sealed class QmlParser
{
    private readonly string _text;
    private readonly string _file;
    private int _pos;
    private int _line = 1;
    private int _lineStart;

    private QmlParser(string text, string file)
    {
        _text = text;
        _file = file;
    }

    public static QmlDocument Parse(string text, string file = "<memory>") => new QmlParser(text, file).ParseDocument();

    private QmlDocument ParseDocument()
    {
        var pragmas = new List<QmlPragma>();
        var imports = new List<QmlImport>();
        SkipTrivia();
        while (!AtEnd)
        {
            var loc = Location;
            if (TryKeyword("pragma"))
            {
                var name = ReadIdentifier();
                string? value = null;
                SkipInlineSpace();
                if (Peek() == ':')
                {
                    _pos++;
                    SkipInlineSpace();
                    value = ReadToLineEnd().Trim().TrimEnd(';').Trim();
                }
                pragmas.Add(new QmlPragma(name, value, loc));
                SkipStatementEnd();
                continue;
            }
            if (TryKeyword("import"))
            {
                imports.Add(ParseImport(loc));
                SkipStatementEnd();
                continue;
            }
            break;
        }

        if (AtEnd)
            throw Error("Expected a root object");
        var root = ParseObject(ReadQualifiedName(), Location);
        SkipTrivia();
        if (!AtEnd)
            throw Error($"Unexpected '{Peek()}' after the root object");
        return new QmlDocument(_file, pragmas, imports, root);
    }

    private QmlImport ParseImport(SourceLocation loc)
    {
        SkipInlineSpace();
        ImportKind kind;
        string uri;
        if (Peek() is '"' or '\'')
        {
            uri = ReadStringLiteral();
            kind = uri.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || uri.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)
                ? ImportKind.Script
                : ImportKind.Directory;
        }
        else
        {
            uri = ReadQualifiedName();
            kind = ImportKind.Module;
        }

        string? version = null;
        string? qualifier = null;
        SkipInlineSpace();
        if (char.IsAsciiDigit(Peek()))
        {
            var start = _pos;
            while (char.IsAsciiDigit(Peek()) || Peek() == '.')
                _pos++;
            version = _text[start.._pos];
            SkipInlineSpace();
        }
        if (TryKeyword("as"))
        {
            qualifier = ReadIdentifier();
            if (!char.IsUpper(qualifier[0]))
                throw new QmlSyntaxException($"Import qualifier '{qualifier}' must start with an uppercase letter", loc);
        }
        if (kind == ImportKind.Script && qualifier is null)
            throw new QmlSyntaxException($"Script import \"{uri}\" requires a qualifier", loc);
        return new QmlImport(kind, uri, version, qualifier, loc);
    }

    // Object := QualifiedName '{' Member* '}'
    private QmlObjectNode ParseObject(string typeName, SourceLocation loc)
    {
        SkipTrivia();
        Expect('{');
        var members = new List<QmlMember>();
        while (true)
        {
            SkipTriviaAndSemicolons();
            if (AtEnd)
                throw Error($"Unterminated object '{typeName}'");
            if (Peek() == '}')
            {
                _pos++;
                break;
            }
            members.Add(ParseMember());
        }
        return new QmlObjectNode(typeName, members, loc);
    }

    private QmlMember ParseMember()
    {
        var loc = Location;
        var save = Save();

        // Modifiers and keywords. Each keyword can also be an ordinary property name ("property: 5"),
        // so a keyword only counts when what follows fits it.
        bool required = false, isReadonly = false, isDefault = false;
        while (true)
        {
            var before = Save();
            if (TryKeyword("required"))
            {
                required = true;
                if (PeekKeyword("property") || PeekKeyword("readonly") || PeekKeyword("default"))
                    continue;
                // "required name": marks an existing property required.
                if (char.IsLetter(Peek()) || Peek() == '_')
                {
                    var name = ReadIdentifier();
                    if (!IsAssignmentAhead())
                    {
                        SkipStatementEnd();
                        return new QmlRequiredMember(name, loc);
                    }
                }
                Restore(before);
                required = false;
                break;
            }
            if (TryKeyword("readonly"))
            {
                if (PeekKeyword("property") || PeekKeyword("required") || PeekKeyword("default"))
                {
                    isReadonly = true;
                    continue;
                }
                Restore(before);
                break;
            }
            if (TryKeyword("default"))
            {
                if (PeekKeyword("property") || PeekKeyword("required") || PeekKeyword("readonly"))
                {
                    isDefault = true;
                    continue;
                }
                Restore(before);
                break;
            }
            break;
        }

        if (TryKeywordFollowedByIdentifier("property"))
            return ParsePropertyDeclaration(loc, required, isReadonly, isDefault);
        if (required || isReadonly || isDefault)
            throw Error("Expected 'property'");

        if (TryKeywordFollowedByIdentifier("signal"))
            return ParseSignal(loc);
        if (TryKeywordFollowedByIdentifier("function"))
            return ParseFunction(loc);
        if (TryKeywordFollowedByIdentifier("enum"))
            return ParseEnum(loc);
        if (TryKeywordFollowedByIdentifier("component"))
        {
            var name = ReadIdentifier();
            SkipTrivia();
            Expect(':');
            SkipTrivia();
            var objLoc = Location;
            var obj = ParseObject(ReadQualifiedName(), objLoc);
            return new QmlInlineComponent(name, obj, loc);
        }

        Restore(save);
        var qualified = ReadQualifiedName();
        SkipTrivia();
        var c = Peek();

        if (c == ':')
        {
            _pos++;
            if (qualified == "id")
            {
                SkipTrivia();
                var id = ReadIdentifier();
                SkipStatementEnd();
                return new QmlIdMember(id, loc);
            }
            var value = ParseValue();
            return new QmlAssignment(qualified, value, loc);
        }

        if (c == '{')
        {
            if (IsTypeName(qualified))
                return new QmlChildObject(ParseObject(qualified, loc), loc);
            return ParseGroupedBlock(qualified, loc);
        }

        if (IsTypeName(qualified) && PeekKeyword("on"))
        {
            TryKeyword("on");
            var property = ReadQualifiedName();
            var obj = ParseObject(qualified, loc);
            return new QmlOnAssignment(property, obj, loc);
        }

        throw Error($"Unexpected '{(AtEnd ? "end of file" : c.ToString())}' after '{qualified}'");
    }

    private QmlGroupedBlock ParseGroupedBlock(string name, SourceLocation loc)
    {
        Expect('{');
        var members = new List<QmlMember>();
        while (true)
        {
            SkipTriviaAndSemicolons();
            if (AtEnd)
                throw Error($"Unterminated group '{name}'");
            if (Peek() == '}')
            {
                _pos++;
                break;
            }
            var memberLoc = Location;
            var inner = ReadQualifiedName();
            SkipTrivia();
            if (Peek() == ':')
            {
                _pos++;
                members.Add(new QmlAssignment(inner, ParseValue(), memberLoc));
            }
            else if (Peek() == '{')
            {
                members.Add(ParseGroupedBlock(inner, memberLoc));
            }
            else
            {
                throw Error($"Expected ':' after '{inner}'");
            }
        }
        return new QmlGroupedBlock(name, members, loc);
    }

    private QmlPropertyDeclaration ParsePropertyDeclaration(SourceLocation loc, bool required, bool isReadonly, bool isDefault)
    {
        var type = ReadTypeName();
        SkipInlineSpace();
        var name = ReadIdentifier();
        SkipInlineSpace();
        QmlValue? value = null;
        if (Peek() == ':')
        {
            _pos++;
            value = ParseValue();
        }
        else
        {
            SkipStatementEnd();
        }
        return new QmlPropertyDeclaration(name, type, required, isReadonly, isDefault, value, loc);
    }

    private string ReadTypeName()
    {
        SkipTrivia();
        var name = ReadQualifiedName();
        SkipInlineSpace();
        if (Peek() == '<')
        {
            _pos++;
            var inner = ReadTypeName();
            SkipInlineSpace();
            Expect('>');
            return $"{name}<{inner}>";
        }
        return name;
    }

    private QmlSignalDeclaration ParseSignal(SourceLocation loc)
    {
        var name = ReadIdentifier();
        SkipInlineSpace();
        var parameters = Peek() == '(' ? ParseParameters(allowOldStyle: true) : [];
        SkipStatementEnd();
        return new QmlSignalDeclaration(name, parameters, loc);
    }

    private QmlFunctionDeclaration ParseFunction(SourceLocation loc)
    {
        var name = ReadIdentifier();
        SkipTrivia();
        var parameters = ParseParameters(allowOldStyle: false);
        SkipTrivia();
        if (Peek() == ':')
        {
            // A return type annotation, which plain JavaScript does not accept.
            _pos++;
            ReadTypeName();
            SkipTrivia();
        }
        Expect('{');
        var bodyLoc = Location;
        var start = _pos;
        ScanBalanced('}');
        var body = _text[start.._pos];
        _pos++;
        return new QmlFunctionDeclaration(name, parameters, body, bodyLoc, loc);
    }

    private List<QmlParameter> ParseParameters(bool allowOldStyle)
    {
        Expect('(');
        var list = new List<QmlParameter>();
        while (true)
        {
            SkipTrivia();
            if (Peek() == ')')
            {
                _pos++;
                break;
            }
            var first = ReadTypeName();
            SkipTrivia();
            string name;
            string? type = null;
            if (Peek() == ':')
            {
                _pos++;
                name = first;
                type = ReadTypeName();
            }
            else if (allowOldStyle && (char.IsLetter(Peek()) || Peek() == '_'))
            {
                // Old signal syntax: signal foo(int bar)
                type = first;
                name = ReadIdentifier();
            }
            else if (Peek() == '=')
            {
                // A default value: function f(a = 1). Keep the raw text in the name for the JS side.
                var start = _pos;
                ScanExpression(stopAtComma: true);
                name = first + _text[start.._pos];
            }
            else
            {
                name = first;
            }
            list.Add(new QmlParameter(name, type));
            SkipTrivia();
            if (Peek() == ',')
                _pos++;
        }
        return list;
    }

    private QmlEnumDeclaration ParseEnum(SourceLocation loc)
    {
        var name = ReadIdentifier();
        SkipTrivia();
        Expect('{');
        var values = new List<KeyValuePair<string, int>>();
        var next = 0;
        while (true)
        {
            SkipTrivia();
            if (Peek() == '}')
            {
                _pos++;
                break;
            }
            var key = ReadIdentifier();
            SkipTrivia();
            if (Peek() == '=')
            {
                _pos++;
                SkipTrivia();
                var negative = false;
                if (Peek() == '-')
                {
                    negative = true;
                    _pos++;
                }
                var start = _pos;
                while (char.IsAsciiHexDigit(Peek()) || Peek() is 'x' or 'X')
                    _pos++;
                var literal = _text[start.._pos];
                var number = literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? int.Parse(literal.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : int.Parse(literal, CultureInfo.InvariantCulture);
                next = negative ? -number : number;
                SkipTrivia();
            }
            values.Add(new(key, next++));
            if (Peek() == ',')
                _pos++;
        }
        return new QmlEnumDeclaration(name, values, loc);
    }

    // Value := Object | '[' Object (',' Object)* ']' | Block | Expression
    private QmlValue ParseValue()
    {
        SkipTrivia();
        var loc = Location;
        var save = Save();

        if (char.IsLetter(Peek()) || Peek() == '_')
        {
            var name = ReadQualifiedName();
            SkipTrivia();
            if (IsTypeName(name) && Peek() == '{')
                return new QmlObjectValue(ParseObject(name, loc), loc);
            Restore(save);
        }

        if (Peek() == '[' && LooksLikeObjectList())
        {
            _pos++;
            var objects = new List<QmlObjectNode>();
            while (true)
            {
                SkipTrivia();
                if (Peek() == ']')
                {
                    _pos++;
                    break;
                }
                var objLoc = Location;
                objects.Add(ParseObject(ReadQualifiedName(), objLoc));
                SkipTrivia();
                if (Peek() == ',')
                    _pos++;
            }
            SkipStatementEnd();
            return new QmlListValue(objects, loc);
        }

        if (Peek() == '{')
        {
            _pos++;
            var blockLoc = Location;
            var start = _pos;
            ScanBalanced('}');
            var code = _text[start.._pos];
            _pos++;
            // A block followed by more expression ("{...}.foo") is rare; treat what follows the brace as the end.
            SkipStatementEnd();
            return new QmlScriptValue(code, IsBlock: true, blockLoc);
        }

        var exprStart = _pos;
        ScanExpression(stopAtComma: false);
        var expr = _text[exprStart.._pos].TrimEnd();
        if (expr.Length == 0)
            throw Error("Expected a value");
        SkipStatementEnd();
        return new QmlScriptValue(expr, IsBlock: false, loc);
    }

    private bool LooksLikeObjectList()
    {
        var save = Save();
        try
        {
            _pos++;
            SkipTrivia();
            if (Peek() == ']')
                return false;
            if (!(char.IsLetter(Peek()) || Peek() == '_'))
                return false;
            var name = ReadQualifiedName();
            SkipTrivia();
            return IsTypeName(name) && Peek() == '{';
        }
        finally
        {
            Restore(save);
        }
    }

    /// <summary>
    /// Advances over one JavaScript expression that ends at a newline, a semicolon or the closing brace of the
    /// enclosing object. A newline ends it only when the expression is complete: brackets are balanced, the line
    /// does not end with an operator, and the next line does not start with one.
    /// </summary>
    private void ScanExpression(bool stopAtComma)
    {
        var depth = 0;
        var pendingTernary = 0;
        var lastSignificant = '\0';
        var lastWord = "";
        while (!AtEnd)
        {
            var c = Peek();
            if (c == '\n')
            {
                if (depth == 0 && pendingTernary == 0 && !EndsWithContinuation(lastSignificant, lastWord) && !NextLineContinues())
                    return;
                Advance();
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                Advance();
                continue;
            }
            if (c == '/' && PeekAt(1) == '/')
            {
                while (!AtEnd && Peek() != '\n')
                    _pos++;
                continue;
            }
            if (c == '/' && PeekAt(1) == '*')
            {
                SkipBlockComment();
                continue;
            }
            if (depth == 0 && (c == ';' || c == '}' || (stopAtComma && (c == ',' || c == ')'))))
                return;
            switch (c)
            {
                case '"' or '\'':
                    ReadStringLiteral();
                    lastSignificant = 'a';
                    lastWord = "";
                    continue;
                case '`':
                    SkipTemplate();
                    lastSignificant = 'a';
                    lastWord = "";
                    continue;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case '?' when PeekAt(1) is '.' or '?':
                    _pos += 2;
                    lastSignificant = '?';
                    lastWord = "";
                    continue;
                case '?' when depth == 0:
                    pendingTernary++;
                    break;
                case ':' when depth == 0 && pendingTernary > 0:
                    pendingTernary--;
                    break;
                case '/' when IsRegexStart(lastSignificant, lastWord):
                    SkipRegex();
                    lastSignificant = 'a';
                    lastWord = "";
                    continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
            {
                var start = _pos;
                while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '$'))
                    _pos++;
                lastWord = _text[start.._pos];
                lastSignificant = 'a';
                continue;
            }
            lastSignificant = c;
            lastWord = "";
            Advance();
        }
    }

    private static bool EndsWithContinuation(char last, string lastWord) =>
        lastWord is "typeof" or "new" or "in" or "instanceof" or "delete" or "void" or "await"
        || (lastWord.Length == 0 && last is '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^' or '!' or '~'
            or '<' or '>' or '=' or '?' or ':' or ',' or '.' or '\0');

    private bool NextLineContinues()
    {
        var i = _pos + 1;
        while (true)
        {
            while (i < _text.Length && char.IsWhiteSpace(_text[i]))
                i++;
            if (i + 1 < _text.Length && _text[i] == '/' && _text[i + 1] == '/')
            {
                while (i < _text.Length && _text[i] != '\n')
                    i++;
                continue;
            }
            break;
        }
        if (i >= _text.Length)
            return false;
        var c = _text[i];
        if (c == '/' && i + 1 < _text.Length && _text[i + 1] == '*')
            return false;
        return c is '.' or '?' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^' or '<' or '>' or '=' or ',' or ')' or ']';
    }

    private static bool IsRegexStart(char last, string lastWord) =>
        lastWord is "return" or "typeof" or "case" or "in" or "of" or "new" or "delete" or "void" or "throw"
        || (lastWord.Length == 0 && last is '\0' or '(' or ',' or '=' or ':' or '[' or '!' or '&' or '|' or '?' or '{' or '}' or ';' or '+' or '-' or '*' or '%' or '<' or '>' or '~' or '^');

    /// <summary>Advances to the bracket that closes the current one, leaving the position on it.</summary>
    private void ScanBalanced(char close)
    {
        var stack = new Stack<char>();
        stack.Push(close);
        var lastSignificant = '{';
        var lastWord = "";
        while (!AtEnd)
        {
            var c = Peek();
            if (c == '/' && PeekAt(1) == '/')
            {
                while (!AtEnd && Peek() != '\n')
                    _pos++;
                continue;
            }
            if (c == '/' && PeekAt(1) == '*')
            {
                SkipBlockComment();
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                Advance();
                continue;
            }
            switch (c)
            {
                case '"' or '\'':
                    ReadStringLiteral();
                    lastSignificant = 'a';
                    lastWord = "";
                    continue;
                case '`':
                    SkipTemplate();
                    lastSignificant = 'a';
                    lastWord = "";
                    continue;
                case '(':
                    stack.Push(')');
                    break;
                case '[':
                    stack.Push(']');
                    break;
                case '{':
                    stack.Push('}');
                    break;
                case ')' or ']' or '}':
                    if (stack.Peek() != c)
                        throw Error($"Mismatched '{c}'");
                    stack.Pop();
                    if (stack.Count == 0)
                        return;
                    break;
                case '/' when IsRegexStart(lastSignificant, lastWord):
                    SkipRegex();
                    lastSignificant = 'a';
                    lastWord = "";
                    continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
            {
                var start = _pos;
                while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '$'))
                    _pos++;
                lastWord = _text[start.._pos];
                lastSignificant = 'a';
                continue;
            }
            lastSignificant = c;
            lastWord = "";
            Advance();
        }
        throw Error($"Expected '{close}'");
    }

    private void SkipTemplate()
    {
        Advance(); // `
        while (!AtEnd)
        {
            var c = Peek();
            if (c == '\\')
            {
                Advance();
                Advance();
                continue;
            }
            if (c == '`')
            {
                Advance();
                return;
            }
            if (c == '$' && PeekAt(1) == '{')
            {
                Advance();
                Advance();
                ScanBalanced('}');
                Advance();
                continue;
            }
            Advance();
        }
        throw Error("Unterminated template literal");
    }

    private void SkipRegex()
    {
        Advance(); // /
        var inClass = false;
        while (!AtEnd)
        {
            var c = Peek();
            if (c == '\n')
                throw Error("Unterminated regular expression");
            Advance();
            if (c == '\\')
            {
                Advance();
                continue;
            }
            if (c == '[')
                inClass = true;
            else if (c == ']')
                inClass = false;
            else if (c == '/' && !inClass)
                break;
        }
        while (!AtEnd && char.IsLetter(Peek()))
            _pos++;
    }

    private string ReadStringLiteral()
    {
        var quote = Peek();
        Advance();
        var sb = new StringBuilder();
        while (!AtEnd)
        {
            var c = Peek();
            if (c == '\\')
            {
                Advance();
                var e = Peek();
                Advance();
                sb.Append(e switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => e,
                });
                continue;
            }
            if (c == quote)
            {
                Advance();
                return sb.ToString();
            }
            if (c == '\n')
                throw Error("Unterminated string");
            sb.Append(c);
            Advance();
        }
        throw Error("Unterminated string");
    }

    private static bool IsTypeName(string qualified)
    {
        var last = qualified.LastIndexOf('.');
        var name = last < 0 ? qualified : qualified[(last + 1)..];
        return name.Length > 0 && char.IsUpper(name[0]);
    }

    private bool IsAssignmentAhead()
    {
        var save = Save();
        SkipInlineSpace();
        var result = Peek() == ':';
        Restore(save);
        return result;
    }

    private string ReadQualifiedName()
    {
        var sb = new StringBuilder(ReadIdentifier());
        while (Peek() == '.' && (char.IsLetter(PeekAt(1)) || PeekAt(1) == '_'))
        {
            _pos++;
            sb.Append('.').Append(ReadIdentifier());
        }
        return sb.ToString();
    }

    private string ReadIdentifier()
    {
        SkipTrivia();
        var start = _pos;
        if (!AtEnd && (char.IsLetter(Peek()) || Peek() == '_' || Peek() == '$'))
        {
            _pos++;
            while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '$'))
                _pos++;
        }
        if (start == _pos)
            throw Error(AtEnd ? "Unexpected end of file" : $"Expected an identifier, found '{Peek()}'");
        return _text[start.._pos];
    }

    private bool TryKeyword(string keyword)
    {
        SkipTrivia();
        if (!PeekKeyword(keyword))
            return false;
        _pos += keyword.Length;
        return true;
    }

    /// <summary>Consumes the keyword only when an identifier follows it, so "property: 5" stays an assignment.</summary>
    private bool TryKeywordFollowedByIdentifier(string keyword)
    {
        var save = Save();
        if (!TryKeyword(keyword))
            return false;
        SkipTrivia();
        if (char.IsLetter(Peek()) || Peek() == '_' || Peek() == '$')
            return true;
        Restore(save);
        return false;
    }

    private bool PeekKeyword(string keyword)
    {
        var save = Save();
        SkipTrivia();
        var ok = string.CompareOrdinal(_text, _pos, keyword, 0, keyword.Length) == 0
            && (_pos + keyword.Length >= _text.Length || !(char.IsLetterOrDigit(_text[_pos + keyword.Length]) || _text[_pos + keyword.Length] == '_'));
        if (!ok)
            Restore(save);
        else
            Restore(save);
        return ok;
    }

    private string ReadToLineEnd()
    {
        var start = _pos;
        while (!AtEnd && Peek() != '\n')
            _pos++;
        return _text[start.._pos];
    }

    private void SkipStatementEnd()
    {
        SkipInlineSpace();
        if (Peek() == ';')
            _pos++;
    }

    private void SkipInlineSpace()
    {
        while (!AtEnd)
        {
            var c = Peek();
            if (c is ' ' or '\t' or '\r')
            {
                _pos++;
                continue;
            }
            if (c == '/' && PeekAt(1) == '*')
            {
                SkipBlockComment();
                continue;
            }
            if (c == '/' && PeekAt(1) == '/')
            {
                while (!AtEnd && Peek() != '\n')
                    _pos++;
                continue;
            }
            break;
        }
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            var c = Peek();
            if (char.IsWhiteSpace(c))
            {
                Advance();
                continue;
            }
            if (c == '/' && PeekAt(1) == '/')
            {
                while (!AtEnd && Peek() != '\n')
                    _pos++;
                continue;
            }
            if (c == '/' && PeekAt(1) == '*')
            {
                SkipBlockComment();
                continue;
            }
            break;
        }
    }

    private void SkipTriviaAndSemicolons()
    {
        while (true)
        {
            SkipTrivia();
            if (Peek() == ';')
            {
                _pos++;
                continue;
            }
            break;
        }
    }

    private void SkipBlockComment()
    {
        _pos += 2;
        while (!AtEnd && !(Peek() == '*' && PeekAt(1) == '/'))
            Advance();
        if (AtEnd)
            throw Error("Unterminated comment");
        _pos += 2;
    }

    private void Expect(char c)
    {
        SkipTrivia();
        if (Peek() != c)
            throw Error(AtEnd ? $"Expected '{c}' before the end of file" : $"Expected '{c}', found '{Peek()}'");
        _pos++;
    }

    private void Advance()
    {
        if (_pos < _text.Length && _text[_pos] == '\n')
        {
            _line++;
            _lineStart = _pos + 1;
        }
        _pos++;
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    private char PeekAt(int offset) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private SourceLocation Location
    {
        get
        {
            SyncLine();
            return new SourceLocation(_file, _line, _pos - _lineStart + 1);
        }
    }

    // Positions move forward with plain _pos++ in places that never cross a newline, and Restore can move
    // backward, so the line is recounted from the nearest known point when a location is needed.
    private void SyncLine()
    {
        if (_lineStart > _pos)
        {
            _line = 1;
            _lineStart = 0;
        }
        for (var i = _lineStart; i < _pos && i < _text.Length; i++)
        {
            if (_text[i] == '\n')
            {
                _line++;
                _lineStart = i + 1;
            }
        }
    }

    private (int Pos, int Line, int LineStart) Save()
    {
        SyncLine();
        return (_pos, _line, _lineStart);
    }

    private void Restore((int Pos, int Line, int LineStart) state) => (_pos, _line, _lineStart) = state;

    private QmlSyntaxException Error(string message) => new(message, Location);
}
