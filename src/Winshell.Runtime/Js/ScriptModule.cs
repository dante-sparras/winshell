using System.Text;
using Acornima;
using Acornima.Ast;
using Jint.Native;

namespace Winshell.Runtime;

/// <summary>
/// A JavaScript file imported with <c>import "file.js" as Name</c>. Its top-level functions and variables
/// become members of the object the qualifier names.
/// </summary>
public static class ScriptModule
{
    /// <summary>Whether a script says <c>.pragma library</c>, so one copy is shared by every importer.</summary>
    public static bool IsLibrary(string code) =>
        code.Replace("\r\n", "\n").Split('\n').Any(static line => IsLibraryDirective(line.Trim()));

    /// <summary>A <c>.pragma</c> line is a library only when its directive is <c>library</c>, not when that word appears somewhere in it.</summary>
    private static bool IsLibraryDirective(string line)
    {
        if (!line.StartsWith(".pragma", StringComparison.Ordinal))
            return false;
        var rest = line.AsSpan(7).TrimStart();
        if (!rest.StartsWith("library"))
            return false;
        rest = rest[7..].TrimStart();
        return rest.IsEmpty || rest.StartsWith("//");
    }

    public static JsValue Load(QmlEngine engine, string code, string path, ComponentContext? context = null)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var imports = ReadImports(path, lines);
        var body = string.Join("\n", lines);
        // A script without .pragma library runs in the component that imported it, so it can see that component's ids.
        var scope = context is null ? null : new ScopeObject(engine.Js, context.ContextObject, context);

        var names = new List<string>();
        try
        {
            var script = new Parser().ParseScript(body, path, false);
            foreach (var statement in script.Body)
            {
                switch (statement)
                {
                    case FunctionDeclaration { Id: { } id }:
                        names.Add(id.Name);
                        break;
                    case VariableDeclaration v:
                        foreach (var d in v.Declarations)
                        {
                            if (d.Id is Identifier ident)
                                names.Add(ident.Name);
                        }
                        break;
                    case ClassDeclaration { Id: { } cid }:
                        names.Add(cid.Name);
                        break;
                }
            }
        }
        catch (ParseErrorException e)
        {
            throw new QmlException($"{path}: {e.Message}");
        }

        var sb = new StringBuilder("(function(");
        if (scope is not null)
            sb.Append("__qmlScope");
        if (scope is not null && imports.Count > 0)
            sb.Append(", ");
        sb.Append(string.Join(", ", imports.Select(i => i.Alias)));
        // The body starts on the factory's first line, so a script error names the file's own line.
        // The function is made inside the with block, so names it does not declare resolve on the component.
        sb.Append(") { ");
        if (scope is not null)
            sb.Append("with (__qmlScope) { ");
        sb.Append(body).Append("\nreturn {");
        sb.Append(string.Join(", ", names.Distinct().Select(n => $"get {n}() {{ return {n}; }}, set {n}(v) {{ {n} = v; }}")));
        sb.Append("};\n");
        if (scope is not null)
            sb.Append('}');
        sb.Append("})");
        var factory = engine.Js.Engine.Evaluate(sb.ToString(), path);
        var args = new JsValue[imports.Count + (scope is null ? 0 : 1)];
        var index = 0;
        if (scope is not null)
            args[index++] = scope;
        foreach (var import in imports)
            args[index++] = engine.LoadScript(import.File, context);
        return engine.Js.Engine.Call(factory, JsValue.Undefined, args);
    }

    private readonly record struct ScriptImport(string Alias, string File);

    private static List<ScriptImport> ReadImports(string path, string[] lines)
    {
        var imports = new List<ScriptImport>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith(".pragma", StringComparison.Ordinal))
            {
                lines[i] = "";
                continue;
            }
            if (!line.StartsWith(".import", StringComparison.Ordinal))
                continue;
            if (!TryParseImport(line, out var specifier, out var alias))
                throw new QmlException($"{path}: {line} is not a javascript import");
            if (!specifier.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                throw new QmlException($"{path}: .import \"{specifier}\" is not a javascript file");
            var directory = Path.GetDirectoryName(path)!;
            imports.Add(new ScriptImport(alias, Path.GetFullPath(Path.Combine(directory, specifier))));
            lines[i] = "";
        }
        return imports;
    }

    /// <summary><c>.import "file.js" as Name</c>, or <c>.import Module Version as Name</c>.</summary>
    private static bool TryParseImport(string line, out string specifier, out string alias)
    {
        specifier = "";
        alias = "";
        var rest = line.AsSpan(7).Trim();
        if (rest.IsEmpty)
            return false;
        if (rest[0] is '"' or '\'')
        {
            var quote = rest[0];
            rest = rest[1..];
            var end = rest.IndexOf(quote);
            if (end < 0)
                return false;
            specifier = rest[..end].ToString();
            rest = rest[(end + 1)..].Trim();
        }
        else
        {
            var space = rest.IndexOf(' ');
            if (space < 0)
                return false;
            specifier = rest[..space].ToString();
            rest = rest[(space + 1)..].Trim();
        }
        if (!StartsWithAs(rest))
        {
            var space = rest.IndexOf(' ');
            if (space < 0)
                return false;
            rest = rest[(space + 1)..].Trim();
        }
        if (!StartsWithAs(rest))
            return false;
        rest = rest[2..].Trim();
        if (rest.IsEmpty || !IsIdentifier(rest))
            return false;
        alias = rest.ToString();
        return specifier.Length > 0;
    }

    private static bool StartsWithAs(ReadOnlySpan<char> text) =>
        text.Length >= 2 && text.StartsWith("as") && (text.Length == 2 || char.IsWhiteSpace(text[2]));

    private static bool IsIdentifier(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || !(char.IsLetter(text[0]) || text[0] is '_' or '$'))
            return false;
        for (var i = 1; i < text.Length; i++)
        {
            if (!(char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$'))
                return false;
        }
        return true;
    }
}
