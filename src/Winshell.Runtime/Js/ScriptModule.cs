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
    public static JsValue Load(QmlEngine engine, string code, string path)
    {
        var imports = new List<(string Name, string File)>();
        var lines = code.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith(".pragma", StringComparison.Ordinal))
            {
                lines[i] = "";
            }
            else if (line.StartsWith(".import", StringComparison.Ordinal))
            {
                // .import "other.js" as Other
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[^2] == "as")
                {
                    var file = parts[1].Trim('"', '\'');
                    if (file.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                        imports.Add((parts[^1], Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, file))));
                }
                lines[i] = "";
            }
        }
        var body = string.Join("\n", lines);

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
        sb.Append(string.Join(", ", imports.Select(i => i.Name)));
        sb.Append(") {\n").Append(body).Append("\nreturn {");
        sb.Append(string.Join(", ", names.Distinct().Select(n => $"get {n}() {{ return {n}; }}, set {n}(v) {{ {n} = v; }}")));
        sb.Append("};\n})");
        var factory = engine.Js.Engine.Evaluate(sb.ToString(), path);
        var args = imports.Select(i => engine.LoadScript(i.File)).ToArray();
        return engine.Js.Engine.Call(factory, JsValue.Undefined, args);
    }
}
