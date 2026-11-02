using System.Globalization;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Winshell;

/// <summary>
/// What editing a TOML file's syntax tree takes, whatever the values are. A key is found by its first segment, and
/// what follows a removed key or table stays in the file.
/// </summary>
internal static class TomlSyntax
{
    /// <summary><c>path[line:column]: message</c>. Tomlyn's <c>Message</c> drops the span; the span's line is 0-based.</summary>
    public static string Locate(string where, DiagnosticMessage diagnostic)
    {
        var line = diagnostic.Span.Start.Line + 1;
        var column = diagnostic.Span.Start.Column + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{where}[{line}:{column}]: {diagnostic.Message}");
    }

    /// <summary>
    /// Whether <paramref name="text"/> loads. <paramref name="error"/> is <c>path[line:column]: message</c>, or
    /// <c>path: message</c> for a byte limit or for Tomlyn's depth limit, when it does not. A file past 1 MiB does not
    /// load. Nesting past 64 levels makes Tomlyn throw, which is reported here and is not applied.
    /// </summary>
    public static bool TryLoad(string where, string text, out DocumentSyntax document, out TomlTable table, out string error)
    {
        document = new DocumentSyntax();
        table = [];
        if (DataLimits.FileBytes(Encoding.UTF8.GetByteCount(text), DataLimits.MaxTextBytes) is { } why)
        {
            error = $"{where}: {why}";
            return false;
        }
        try
        {
            document = SyntaxParser.Parse(text);
        }
        catch (TomlException e)
        {
            error = $"{where}: {e.Message}";
            return false;
        }
        if (document.HasErrors)
        {
            error = document.Diagnostics.Count > 0 ? Locate(where, document.Diagnostics[0]) : $"{where}: invalid TOML";
            return false;
        }
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? [];
            error = "";
            return true;
        }
        catch (TomlException e)
        {
            error = e.Line > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{where}[{e.Line}:{e.Column}]: {e.Message}")
                : $"{where}: {e.Message}";
            return false;
        }
    }

    public static void RemoveInline(InlineTableSyntax table, InlineTableItemSyntax item)
    {
        var index = 0;
        while (table.Items.GetChild(index) != item)
            index++;
        table.Items.RemoveChild(item);
        if (index > 0 && index == table.Items.ChildrenCount)
            table.Items.GetChild(index - 1)!.Comma = null;
    }

    public static void AppendRaw(DocumentSyntax document, string keyText, string tomlValue, string newline, IEnumerable<string>? commentLines = null)
    {
        var fresh = ParseAssignment(keyText, tomlValue, newline);
        var leading = new List<SyntaxTrivia>();
        if (document.KeyValues.ChildrenCount == 0)
            leading.AddRange(StealPreamble(document));
        else
            leading.Add(new SyntaxTrivia(TokenKind.NewLine, newline));
        if (commentLines is not null)
        {
            foreach (var line in commentLines)
            {
                leading.Add(new SyntaxTrivia(TokenKind.Comment, line));
                leading.Add(new SyntaxTrivia(TokenKind.NewLine, newline));
            }
        }
        FirstToken(fresh.Key!).LeadingTrivia = leading;
        Append(document, fresh, newline);
    }

    // A header comment, or a comment above the first table, belongs above the first key we add.
    private static List<SyntaxTrivia> StealPreamble(DocumentSyntax document)
    {
        if (document.TrailingTrivia is { Count: > 0 } trailing)
        {
            document.TrailingTrivia = null;
            return trailing;
        }
        if (document.Tables.ChildrenCount > 0 && document.Tables.GetChild(0) is TableSyntaxBase table
            && table.LeadingTrivia is { Count: > 0 } leading)
        {
            table.LeadingTrivia = null;
            return leading;
        }
        return [];
    }

    private static void Append(DocumentSyntax document, KeyValueSyntax fresh, string newline)
    {
        List<SyntaxTrivia>? after = document.Tables.ChildrenCount > 0 ? [new SyntaxTrivia(TokenKind.NewLine, newline)] : null;
        var count = document.KeyValues.ChildrenCount;
        if (count > 0)
        {
            var last = (KeyValueSyntax)document.KeyValues.GetChild(count - 1)!;
            last.EndOfLineToken ??= new SyntaxToken(TokenKind.NewLine, newline);
            after = last.EndOfLineToken.TrailingTrivia;
            last.EndOfLineToken.TrailingTrivia = null;
        }
        document.KeyValues.Add(fresh);
        fresh.EndOfLineToken ??= new SyntaxToken(TokenKind.NewLine, newline);
        fresh.EndOfLineToken.TrailingTrivia = after;
    }

    public static KeyValueSyntax ParseAssignment(string keyText, string tomlValue, string newline)
    {
        var parsed = SyntaxParser.Parse(keyText + " = " + tomlValue + newline);
        var fresh = (KeyValueSyntax)parsed.KeyValues.GetChild(0)!;
        parsed.KeyValues.RemoveChild(fresh);
        return fresh;
    }

    // What follows the last key, such as the blank line and comment before a table, follows the key added after it.
    // A comment above the key stays too. Returns what is left to keep when no key is left in the list, or null.
    public static List<SyntaxTrivia>? Remove(SyntaxList<KeyValueSyntax> list, KeyValueSyntax kv, string newline)
    {
        var above = kv.LeadingTrivia;
        List<SyntaxTrivia> kept = [.. above is not null && above.Exists(t => t.Kind == TokenKind.Comment) ? above : [], .. kv.EndOfLineToken?.TrailingTrivia ?? []];
        var index = 0;
        while (list.GetChild(index) != kv)
            index++;
        list.RemoveChild(kv);
        if (kept.Count == 0)
            return null;
        if (index > 0)
        {
            var previous = list.GetChild(index - 1)!;
            previous.EndOfLineToken ??= new SyntaxToken(TokenKind.NewLine, newline);
            previous.EndOfLineToken.TrailingTrivia = [.. previous.EndOfLineToken.TrailingTrivia ?? [], .. kept];
            return null;
        }
        if (index < list.ChildrenCount)
        {
            var next = list.GetChild(index)!;
            next.LeadingTrivia = [.. kept, .. next.LeadingTrivia ?? []];
            return null;
        }
        return kept;
    }

    /// <summary>Takes out a top-level key. What is left to keep after the last one goes above the first table, or ends the file.</summary>
    public static void RemoveKey(DocumentSyntax document, KeyValueSyntax kv, string newline)
    {
        if (Remove(document.KeyValues, kv, newline) is not { } kept)
            return;
        if (document.Tables.ChildrenCount > 0)
        {
            var first = document.Tables.GetChild(0)!;
            first.LeadingTrivia = [.. kept, .. first.LeadingTrivia ?? []];
        }
        else
            document.TrailingTrivia = [.. kept, .. document.TrailingTrivia ?? []];
    }

    public static void RemoveTable(DocumentSyntax document, TableSyntaxBase table)
    {
        var leading = table.LeadingTrivia;
        var index = 0;
        while (document.Tables.GetChild(index) != table)
            index++;
        document.Tables.RemoveChild(table);
        if (leading is not { Count: > 0 })
            return;
        if (index < document.Tables.ChildrenCount)
        {
            var next = document.Tables.GetChild(index)!;
            next.LeadingTrivia = [.. leading, .. next.LeadingTrivia ?? []];
        }
        else if (document.KeyValues.ChildrenCount > 0)
        {
            var last = (KeyValueSyntax)document.KeyValues.GetChild(document.KeyValues.ChildrenCount - 1)!;
            if (last.EndOfLineToken is not null)
                last.EndOfLineToken.TrailingTrivia = [.. last.EndOfLineToken.TrailingTrivia ?? [], .. leading];
        }
        else
            document.TrailingTrivia = [.. leading, .. document.TrailingTrivia ?? []];
    }

    public static void InsertAfter(DocumentSyntax document, TableSyntaxBase after, TableSyntaxBase table)
    {
        var following = new List<TableSyntaxBase>();
        var seen = false;
        foreach (var current in document.Tables)
        {
            if (seen)
                following.Add(current);
            else if (ReferenceEquals(current, after))
                seen = true;
        }
        foreach (var current in following)
            document.Tables.RemoveChild(current);
        document.Tables.Add(table);
        foreach (var current in following)
            document.Tables.Add(current);
    }

    public static Owned FindOwned(DocumentSyntax document, string name)
    {
        var owned = new Owned();
        foreach (var kv in document.KeyValues)
        {
            if (RootName(kv.Key) == name)
                owned.KeyValues.Add(kv);
        }
        foreach (var table in document.Tables)
        {
            if (RootName(table.Name) == name)
                owned.Tables.Add(table);
        }
        return owned;
    }

    public static KeyValueSyntax? FindItem(SyntaxList<KeyValueSyntax> items, string name)
    {
        foreach (var kv in items)
        {
            if (Names(kv.Key, name))
                return kv;
        }
        return null;
    }

    public static bool Names(KeySyntax? key, string name) => IsSimple(key) && RootName(key) == name;

    public static bool IsSimple(KeySyntax? key) => key is not null && key.DotKeys.ChildrenCount == 0;

    private static string? RootName(KeySyntax? key) => key is null ? null : SegmentText(key.Key);

    public static IEnumerable<string> Segments(KeySyntax key)
    {
        if (SegmentText(key.Key) is { } first)
            yield return first;
        foreach (var dot in key.DotKeys)
        {
            if (SegmentText(dot.Key) is { } part)
                yield return part;
        }
    }

    private static string? SegmentText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text,
        StringValueSyntax quoted => quoted.Value,
        _ => null,
    };

    public static string NewlineOf(DocumentSyntax document)
    {
        foreach (var kv in document.KeyValues)
        {
            if (Line(kv.EndOfLineToken) is { } line)
                return line;
        }
        foreach (var table in document.Tables)
        {
            if (Line(table.EndOfLineToken) is { } line)
                return line;
            foreach (var kv in table.Items)
            {
                if (Line(kv.EndOfLineToken) is { } nested)
                    return nested;
            }
        }
        // A comment-only file has its newline in trivia, not on a key.
        if (TriviaNewline(document.TrailingTrivia) is { } trailing)
            return trailing;
        return "\n";
    }

    private static string? TriviaNewline(List<SyntaxTrivia>? trivia)
    {
        if (trivia is null)
            return null;
        foreach (var item in trivia)
        {
            if (item.Kind == TokenKind.NewLine && item.Text is { Length: > 0 } text)
                return text == "\r\n" ? "\r\n" : "\n";
        }
        return null;
    }

    private static string? Line(SyntaxToken? token) =>
        token is { TokenKind: TokenKind.NewLine, Text: { } text } && text.Length > 0
            ? text == "\r\n" ? "\r\n" : "\n"
            : null;

    public static SyntaxToken FirstToken(SyntaxNode node) => node.Tokens(false).OfType<SyntaxToken>().First();

    public static SyntaxToken LastToken(SyntaxNode node) => node.Tokens(false).OfType<SyntaxToken>().Last();

    public static SyntaxToken Comma()
    {
        var comma = new SyntaxToken(TokenKind.Comma, ",");
        comma.TrailingTrivia = [new SyntaxTrivia(TokenKind.Whitespaces, " ")];
        return comma;
    }

    public static void SetString(StringValueSyntax node, string text)
    {
        node.Value = text;
        node.Token!.TokenKind = TokenKind.String;
        node.Token.Text = Quote(text);
    }

    public static void SetBool(BooleanValueSyntax node, bool value)
    {
        node.Value = value;
        node.Token!.TokenKind = value ? TokenKind.True : TokenKind.False;
        node.Token.Text = value ? "true" : "false";
    }

    public static string Key(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? key : Quote(key);

    public static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in text)
        {
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }
        return quoted.Append('"').ToString();
    }

    /// <summary>Everything in a document that defines one key: its assignments, and its tables or array of tables.</summary>
    public sealed class Owned
    {
        public List<KeyValueSyntax> KeyValues { get; } = [];

        public List<TableSyntaxBase> Tables { get; } = [];

        public bool Defined => KeyValues.Count > 0 || Tables.Count > 0;
    }
}
