using System.Globalization;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;
using JsObject = Jint.Native.JsObject;

namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>TomlAdapter</c>: a TOML file as properties. Writing changes only the keys it has properties for, so the
/// comments, blank lines and other keys in the file stay. A key the file does not have yet goes at the end of its
/// top-level keys, under its line from <see cref="Comments"/>.
/// </summary>
public sealed class TomlAdapter : FileViewAdapter, IPropertyObserver
{
    private bool _loading;

    /// <summary>A comment for each key, as <c>{ key: "text" }</c>, written above a key the file does not have yet.</summary>
    [Qml] public object? Comments { get; set; }

    public override void ComponentComplete()
    {
        foreach (var p in DynamicProperties)
            AddObserver(p.Name, this);
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        // The view passes it on, so it reaches the view once.
        if (!_loading)
            Emit("adapterUpdated");
    }

    public override void Deserialize(string text)
    {
        if (Engine is null || string.IsNullOrWhiteSpace(text))
            return;
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text) ?? [];
        }
        catch (TomlException e)
        {
            Engine.Log.Warn("io", $"TomlAdapter: failed to parse {View?.Path}: {e.Message}");
            return;
        }
        _loading = true;
        try
        {
            foreach (var p in DynamicProperties)
            {
                if (!p.IsReadonly && table.TryGetValue(p.Name, out var value))
                    p.Write(ToJs(value, Engine.Js.Engine));
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public override string Serialize()
    {
        if (Engine is null)
            return "";
        var document = SyntaxParser.Parse(View?.Text ?? "");
        // A file that does not parse is written anew.
        if (document.HasErrors)
            document = new DocumentSyntax();
        var comments = Engine.Js.ToJs(ValueConverter.Unwrap(Comments));
        foreach (var p in DynamicProperties)
        {
            var existing = Find(document, p.Name);
            var text = Format(Engine.Js.ToJs(p.Read()));
            if (text is null)
            {
                // TOML has no null: a key without a value is left out.
                if (existing is not null)
                    Remove(document, existing);
                continue;
            }
            var parsed = SyntaxParser.Parse(Key(p.Name) + " = " + text + "\n");
            var fresh = (KeyValueSyntax)parsed.KeyValues.GetChild(0)!;
            if (existing is not null)
            {
                // A comment after the value stays after the new one.
                var trailing = LastToken(existing.Value!).TrailingTrivia;
                var value = fresh.Value!;
                fresh.Value = null;
                existing.Value = value;
                LastToken(value).TrailingTrivia = trailing;
                continue;
            }
            parsed.KeyValues.RemoveChild(fresh);
            var leading = new List<SyntaxTrivia>();
            if (document.KeyValues.ChildrenCount > 0)
                leading.Add(new SyntaxTrivia(TokenKind.NewLine, "\n"));
            var comment = comments.IsObject() ? comments.AsObject().Get(p.Name) : JsValue.Undefined;
            if (comment.IsString())
            {
                foreach (var line in comment.AsString().Split('\n'))
                {
                    leading.Add(new SyntaxTrivia(TokenKind.Comment, line.Length > 0 ? "# " + line : "#"));
                    leading.Add(new SyntaxTrivia(TokenKind.NewLine, "\n"));
                }
            }
            fresh.Key!.Tokens(false).OfType<SyntaxToken>().First().LeadingTrivia = leading;
            Append(document, fresh);
        }
        return document.ToString();
    }

    // What follows the last key, such as the blank line and comment before a table, follows the key added after it.
    private static void Append(DocumentSyntax document, KeyValueSyntax fresh)
    {
        List<SyntaxTrivia>? after = document.Tables.ChildrenCount > 0 ? [new SyntaxTrivia(TokenKind.NewLine, "\n")] : null;
        var count = document.KeyValues.ChildrenCount;
        if (count > 0)
        {
            var last = (KeyValueSyntax)document.KeyValues.GetChild(count - 1)!;
            // A file that does not end in a new line gets one before the key.
            last.EndOfLineToken ??= new SyntaxToken(TokenKind.NewLine, "\n");
            after = last.EndOfLineToken.TrailingTrivia;
            last.EndOfLineToken.TrailingTrivia = null;
        }
        document.KeyValues.Add(fresh);
        fresh.EndOfLineToken!.TrailingTrivia = after;
    }

    // The comments and blank lines after a key belong to the lines below it, so they stay when the key goes.
    private static void Remove(DocumentSyntax document, KeyValueSyntax kv)
    {
        var after = kv.EndOfLineToken?.TrailingTrivia;
        var index = 0;
        while (document.KeyValues.GetChild(index) != kv)
            index++;
        document.KeyValues.RemoveChild(kv);
        if (after is null || after.Count == 0)
            return;
        if (index > 0)
        {
            var previous = (KeyValueSyntax)document.KeyValues.GetChild(index - 1)!;
            previous.EndOfLineToken ??= new SyntaxToken(TokenKind.NewLine, "\n");
            previous.EndOfLineToken.TrailingTrivia = [.. previous.EndOfLineToken.TrailingTrivia ?? [], .. after];
        }
        else if (index < document.KeyValues.ChildrenCount)
        {
            var next = ((KeyValueSyntax)document.KeyValues.GetChild(index)!).Key!.Tokens(false).OfType<SyntaxToken>().First();
            next.LeadingTrivia = [.. after, .. next.LeadingTrivia ?? []];
        }
    }

    private static KeyValueSyntax? Find(DocumentSyntax document, string name)
    {
        foreach (var kv in document.KeyValues)
        {
            var key = kv.Key;
            if (key is null || key.DotKeys.ChildrenCount > 0)
                continue;
            var text = key.Key switch
            {
                BareKeySyntax bare => bare.Key?.Text,
                StringValueSyntax quoted => quoted.Value,
                _ => null,
            };
            if (text == name)
                return kv;
        }
        return null;
    }

    private static SyntaxToken LastToken(SyntaxNode node) => node.Tokens(false).OfType<SyntaxToken>().Last();

    private static JsValue ToJs(object? value, Engine engine) => value switch
    {
        string s => s,
        bool b => b,
        long l => l,
        double d => d,
        TomlTable table => Object(table, engine),
        TomlArray array => new JsArray(engine, array.Select(v => ToJs(v, engine)).ToArray()),
        TomlTableArray tables => new JsArray(engine, tables.Select(t => Object(t, engine)).ToArray()),
        null => JsValue.Null,
        _ => value.ToString() ?? "",
    };

    private static JsObject Object(TomlTable table, Engine engine)
    {
        var obj = new JsObject(engine);
        foreach (var (key, value) in table)
            obj.Set(key, ToJs(value, engine));
        return obj;
    }

    /// <summary>A value as TOML, or null for one TOML cannot hold.</summary>
    private static string? Format(JsValue value)
    {
        if (value.IsString())
            return Quote(value.AsString());
        if (value.IsBoolean())
            return value.AsBoolean() ? "true" : "false";
        if (value.IsNumber())
        {
            var d = value.AsNumber();
            if (double.IsNaN(d))
                return "nan";
            if (double.IsInfinity(d))
                return d > 0 ? "inf" : "-inf";
            if (d == Math.Floor(d) && Math.Abs(d) < 9007199254740992)
                return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }
        if (value.IsArray())
        {
            var array = value.AsArray();
            var items = new List<string>();
            for (uint i = 0; i < array.Length; i++)
            {
                if (Format(array[i]) is { } item)
                    items.Add(item);
            }
            return "[" + string.Join(", ", items) + "]";
        }
        if (value is ObjectInstance obj && !value.IsDate())
        {
            var items = new List<string>();
            foreach (var key in obj.GetOwnPropertyKeys(Jint.Runtime.Types.String))
            {
                if (Format(obj.Get(key)) is { } item)
                    items.Add(Key(key.AsString()) + " = " + item);
            }
            return items.Count == 0 ? "{}" : "{ " + string.Join(", ", items) + " }";
        }
        return null;
    }

    private static string Key(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? key : Quote(key);

    private static string Quote(string text)
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
}
