using System.Globalization;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;
using static Winshell.TomlSyntax;
using JsObject = Jint.Native.JsObject;

namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>TomlAdapter</c>: a TOML file as properties. Writing changes a key only when its value changed, and only the
/// keys it has properties for, so comments, blank lines, number spellings and other keys stay. A table, an array of
/// tables, or a dotted key already in the file is the value: it is updated in place and never written a second time.
/// A key the file does not have yet goes at the end of its top-level keys, under its line from <see cref="Comments"/>.
/// </summary>
public sealed class TomlAdapter : FileViewAdapter, IPropertyObserver
{
    /// <summary>2^53. A TOML integer outside this range is not a JavaScript number.</summary>
    private const double ExactIntegerLimit = 9007199254740992d;

    private readonly HashSet<string> _leave = new(StringComparer.Ordinal);

    /// <summary>Properties assigned since the last deserialize that parsed. A torn buffer must not replace the rest.</summary>
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);

    private bool _loading;

    /// <summary>A comment for each key, as <c>{ key: "text" }</c>, written above a key the file does not have yet.</summary>
    [Qml] public object? Comments { get; set; }

    /// <summary>
    /// A function <c>(key, value)</c> that returns why a value read from the file is refused, or <c>""</c>. A refused
    /// key is left as the file has it, like a value of the wrong type.
    /// </summary>
    [Qml] public object? Check { get; set; }

    /// <summary>Bytes <see cref="Deserialize"/> will read. Past this, the file is left as it is. 1 MiB.</summary>
    internal int MaxFileBytes { get; set; } = DataLimits.MaxTextBytes;

    public override void ComponentComplete()
    {
        foreach (var p in DynamicProperties)
            AddObserver(p.Name, this);
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        // The view passes it on, so it reaches the view once. Assigning clears a value we had refused to read.
        if (_loading)
            return;
        _leave.Remove(property);
        _dirty.Add(property);
        Emit("adapterUpdated");
    }

    public override void Deserialize(string text)
    {
        if (Engine is null)
            return;
        if (string.IsNullOrWhiteSpace(text))
        {
            Error = "";
            return;
        }
        if (!TryParse(text, out var table, out var error))
        {
            Error = error;
            Warn($"failed to parse {error}");
            return;
        }
        Error = "";
        Apply(table, skipDirty: false);
        _dirty.Clear();
    }

    /// <summary>Loads <paramref name="table"/> into the properties. A recovery skips properties assigned since the last good read.</summary>
    private void Apply(TomlTable table, bool skipDirty)
    {
        if (Engine is null)
            return;
        _loading = true;
        try
        {
            foreach (var p in DynamicProperties)
            {
                if (p.IsReadonly || (skipDirty && _dirty.Contains(p.Name)))
                    continue;
                try
                {
                    if (!table.TryGetValue(p.Name, out var value))
                    {
                        _leave.Remove(p.Name);
                        continue;
                    }
                    if (p.QmlType == "bool" && value is not bool and not long and not double)
                    {
                        Leave(p.Name, $"{p.Name} is not a boolean, so it was left unchanged");
                        continue;
                    }
                    // Converted to a string, the value would be written back over the user's line on the next save:
                    // accentColor = 0x0078D4 became accentColor = "30932". The property keeps its value, as it does
                    // for a key the file does not have, and the shell falls back the way it does for that.
                    if (p.QmlType is "string" or "url" or "color" && value is not string)
                    {
                        _leave.Add(p.Name);
                        continue;
                    }
                    if (HasUnsafeInteger(value))
                    {
                        Leave(p.Name, $"{p.Name} is an integer past 2^53, so it was left unchanged");
                        continue;
                    }
                    var js = ToJs(value, Engine.Js.Engine);
                    if (Refusal(p.Name, js) is { Length: > 0 } reason)
                    {
                        Leave(p.Name, $"{p.Name} {reason}, so it was left unchanged");
                        continue;
                    }
                    _leave.Remove(p.Name);
                    p.Write(js);
                }
                catch (QmlException e)
                {
                    Leave(p.Name, $"could not read {p.Name}: {e.Message}");
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public override string Serialize()
    {
        PreserveFile = false;
        if (Engine is null)
            return "";
        var buffer = View?.Text ?? "";
        if (OverBudget(buffer) is { } refused)
        {
            PreserveFile = true;
            Error = refused;
            Warn($"failed to parse {refused}; the file was left unchanged");
            return "";
        }
        DocumentSyntax document;
        try
        {
            document = SyntaxParser.Parse(buffer);
        }
        catch (TomlException e)
        {
            PreserveFile = true;
            Error = $"{Where}: {e.Message}";
            Warn($"failed to parse {Error}; the file was left unchanged");
            return "";
        }
        if (!Recover(ref document))
        {
            PreserveFile = true;
            return "";
        }
        var newline = NewlineOf(document);
        var comments = Engine.Js.ToJs(ValueConverter.Unwrap(Comments));
        TomlTable? model = null;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(document.ToString());
        }
        catch (TomlException)
        {
            // The syntax tree had no errors. Comparison falls through and the keys are still edited in place.
        }
        foreach (var p in DynamicProperties)
        {
            if (p.IsReadonly)
                continue;
            var desired = Engine.Js.ToJs(p.Read());
            var owned = FindOwned(document, p.Name);
            if (desired.IsNull() || desired.IsUndefined())
            {
                foreach (var kv in owned.KeyValues.ToList())
                    Remove(document.KeyValues, kv, newline);
                foreach (var table in owned.Tables.ToList())
                    RemoveTable(document, table);
                continue;
            }
            // A value we refused to read stays, until the user assigns the property.
            if (_leave.Contains(p.Name) && owned.Defined)
                continue;
            if (!owned.Defined)
            {
                AppendNew(document, p.Name, desired, comments, newline);
                continue;
            }
            if (model is not null && model.TryGetValue(p.Name, out var current) && !HasUnsafeInteger(current)
                && Equivalent(ToJs(current, Engine.Js.Engine), desired, p.QmlType))
                continue;
            if (!UpdateOwned(document, owned, p.Name, desired, newline))
            {
                foreach (var kv in owned.KeyValues.ToList())
                    Remove(document.KeyValues, kv, newline);
                foreach (var table in owned.Tables.ToList())
                    RemoveTable(document, table);
                AppendNew(document, p.Name, desired, comments, newline);
            }
        }
        return document.ToString();
    }

    /// <summary>
    /// A buffer that does not parse is read from disk again. A good read is loaded into the properties that were not
    /// assigned, then edited. A bad read, or a read that throws, writes nothing.
    /// </summary>
    private bool Recover(ref DocumentSyntax document)
    {
        if (!document.HasErrors)
            return true;
        var detail = document.Diagnostics.Count > 0 ? Locate(document.Diagnostics[0]) : $"{Where}: invalid TOML";
        var path = View?.Path;
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                var length = new FileInfo(path).Length;
                if (DataLimits.FileBytes(length, MaxFileBytes) is { } sized)
                    detail = $"{Where}: {sized}";
                else
                {
                    var againText = File.ReadAllText(path);
                    if (OverBudget(againText) is { } refused)
                        detail = refused;
                    else
                    {
                        var again = SyntaxParser.Parse(againText);
                        if (!again.HasErrors && LoadClean(again))
                        {
                            document = again;
                            Error = "";
                            return true;
                        }
                        if (again.HasErrors && again.Diagnostics.Count > 0)
                            detail = Locate(again.Diagnostics[0]);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TomlException)
            {
                detail = $"{Where}: {e.Message}";
            }
        }
        Error = detail;
        Warn($"failed to parse {detail}; the file was left unchanged");
        return false;
    }

    private string Locate(DiagnosticMessage diagnostic) => TomlSyntax.Locate(Where, diagnostic);

    /// <summary><c>path: why</c> when <paramref name="text"/> is past the byte limit, or null.</summary>
    private string? OverBudget(string text)
    {
        var why = DataLimits.FileBytes(Encoding.UTF8.GetByteCount(text), MaxFileBytes);
        return why is null ? null : $"{Where}: {why}";
    }

    private bool TryParse(string text, out TomlTable table, out string error)
    {
        if (OverBudget(text) is { } refused)
        {
            table = [];
            error = refused;
            return false;
        }
        return TryLoad(Where, text, out table, out error);
    }

    /// <summary>The parse <see cref="Deserialize"/> uses. See <see cref="TomlSyntax.TryLoad"/>.</summary>
    internal static bool TryLoad(string where, string text, out TomlTable table, out string error) =>
        TomlSyntax.TryLoad(where, text, out _, out table, out error);

    /// <summary>Loads a document that parses into the properties that have not been assigned since the last good read.</summary>
    private bool LoadClean(DocumentSyntax document)
    {
        if (Engine is null)
            return false;
        try
        {
            Apply(TomlSerializer.Deserialize<TomlTable>(document.ToString()) ?? [], skipDirty: true);
            return true;
        }
        catch (TomlException)
        {
            return false;
        }
    }

    private bool UpdateOwned(DocumentSyntax document, Owned owned, string name, JsValue desired, string newline)
    {
        var simple = owned.KeyValues.Where(kv => IsSimple(kv.Key)).ToList();
        var dotted = owned.KeyValues.Where(kv => !IsSimple(kv.Key)).ToList();
        var rows = owned.Tables.OfType<TableArraySyntax>().Where(t => IsSimple(t.Name)).ToList();
        var tables = owned.Tables.OfType<TableSyntax>().Where(t => IsSimple(t.Name)).ToList();
        if (simple.Count > 0 && rows.Count == 0 && tables.Count == 0 && dotted.Count == 0)
        {
            UpdateKeyValue(simple[0], desired, newline);
            return true;
        }
        if (simple.Count == 0 && dotted.Count == 0 && tables.Count == 0 && rows.Count > 0
            && desired.IsArray() && IsObjectArray(desired.AsArray()))
        {
            UpdateRows(document, rows, desired.AsArray(), name, newline);
            return true;
        }
        if (simple.Count == 0 && rows.Count == 0 && desired.IsObject() && !desired.IsArray()
            && (tables.Count > 0 || dotted.Count > 0))
        {
            var obj = desired.AsObject();
            if (tables.Count > 0)
                UpdateItems(tables[0].Items, obj, newline);
            if (dotted.Count > 0)
                UpdateDotted(document, dotted, obj, name, newline, addMissing: tables.Count == 0);
            return true;
        }
        return false;
    }

    private void UpdateRows(DocumentSyntax document, List<TableArraySyntax> rows, JsArray desired, string name, string newline)
    {
        var count = (int)desired.Length;
        var shared = Math.Min(count, rows.Count);
        for (var i = 0; i < shared; i++)
            UpdateItems(rows[i].Items, desired[(uint)i].AsObject(), newline);
        for (var i = rows.Count - 1; i >= count; i--)
            RemoveTable(document, rows[i]);
        var after = count == 0 ? null : rows[Math.Min(count, rows.Count) - 1];
        for (var i = rows.Count; i < count; i++)
        {
            var table = NewRow(name, desired[(uint)i].AsObject(), newline);
            if (after is null)
                document.Tables.Add(table);
            else
                InsertAfter(document, after, table);
            after = table;
        }
    }

    private void UpdateDotted(DocumentSyntax document, List<KeyValueSyntax> dotted, ObjectInstance desired, string name, string newline, bool addMissing)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in dotted)
        {
            var path = Segments(kv.Key!).Skip(1).ToList();
            if (!TryLeaf(desired, path, out var leaf))
                continue;
            seen.Add(string.Join(".", path));
            if (leaf.IsNull() || leaf.IsUndefined())
            {
                Remove(document.KeyValues, kv, newline);
                continue;
            }
            UpdateKeyValue(kv, leaf, newline);
        }
        if (addMissing)
            AddDotted(document, desired, [name], seen, newline);
    }

    private void AddDotted(DocumentSyntax document, ObjectInstance obj, List<string> prefix, HashSet<string> seen, string newline)
    {
        foreach (var key in ObjectKeys(obj))
        {
            var child = obj.Get(key);
            var path = prefix.Append(key).ToList();
            if (child.IsObject() && !child.IsArray() && !child.IsNull())
            {
                AddDotted(document, child.AsObject(), path, seen, newline);
                continue;
            }
            if (child.IsNull() || child.IsUndefined() || Format(child) is not { } written)
                continue;
            var relative = string.Join(".", path.Skip(1));
            if (seen.Contains(relative))
                continue;
            AppendRaw(document, string.Join(".", path.Select(Key)), written, newline);
        }
    }

    private void UpdateKeyValue(KeyValueSyntax kv, JsValue desired, string newline)
    {
        if (kv.Value is not null && TryUpdate(kv.Value, desired, newline))
            return;
        ReplaceValue(kv, desired, newline);
    }

    private bool TryUpdate(ValueSyntax node, JsValue desired, string newline)
    {
        switch (node)
        {
            case StringValueSyntax s when desired.IsString():
                if (s.Value != desired.AsString())
                    SetString(s, desired.AsString());
                return true;
            case BooleanValueSyntax b when desired.IsBoolean():
                if (b.Value != desired.AsBoolean())
                    SetBool(b, desired.AsBoolean());
                return true;
            case IntegerValueSyntax integer when desired.IsNumber():
                return UpdateInteger(integer, desired.AsNumber());
            case FloatValueSyntax number when desired.IsNumber():
                return UpdateFloat(number, desired.AsNumber());
            case DateTimeValueSyntax date when desired.IsString():
                // The token is the user's spelling. Quoting it would turn a date into a string.
                var written = desired.AsString();
                return date.Token?.Text == written || date.Value.ToString() == written;
            case ArraySyntax array when desired.IsArray():
                UpdateArray(array, desired.AsArray(), newline);
                return true;
            case InlineTableSyntax table when desired.IsObject() && !desired.IsArray():
                UpdateInline(table, desired.AsObject(), newline);
                return true;
            default:
                return false;
        }
    }

    private static bool UpdateInteger(IntegerValueSyntax node, double desired)
    {
        // The token is the user's spelling. A rounded JavaScript number must not replace it.
        if (!IsExactLong(node.Value))
            return NumbersEqual((double)node.Value, desired);
        if (!IsIntegerNumber(desired))
            return false;
        var asLong = (long)desired;
        if (node.Value == asLong)
            return true;
        node.Value = asLong;
        node.Token!.TokenKind = TokenKind.Integer;
        node.Token.Text = asLong.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool UpdateFloat(FloatValueSyntax node, double desired)
    {
        if (NumbersEqual(node.Value, desired))
            return true;
        if (double.IsNaN(desired) || double.IsInfinity(desired))
            return false;
        node.Value = desired;
        node.Token!.TokenKind = TokenKind.Float;
        node.Token.Text = desired.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }

    private void UpdateArray(ArraySyntax array, JsArray desired, string newline)
    {
        var count = (int)desired.Length;
        var shared = Math.Min(count, array.Items.ChildrenCount);
        for (var i = 0; i < shared; i++)
        {
            var item = array.Items.GetChild(i)!;
            var child = desired[(uint)i];
            if (item.Value is null || !TryUpdate(item.Value, child, newline))
                item.Value = ParsedValue(child, newline, item.Value);
        }
        while (array.Items.ChildrenCount > count)
            array.Items.RemoveChildAt(array.Items.ChildrenCount - 1);
        for (var i = array.Items.ChildrenCount; i < count; i++)
        {
            if (array.Items.ChildrenCount > 0)
            {
                var previous = array.Items.GetChild(array.Items.ChildrenCount - 1)!;
                previous.Comma ??= Comma();
            }
            array.Items.Add(new ArrayItemSyntax { Value = ParsedValue(desired[(uint)i], newline, null) });
        }
    }

    private void UpdateInline(InlineTableSyntax table, ObjectInstance desired, string newline)
    {
        foreach (var key in ObjectKeys(desired))
        {
            var child = desired.Get(key);
            InlineTableItemSyntax? found = null;
            foreach (var item in table.Items)
            {
                if (Names(item.KeyValue?.Key, key))
                {
                    found = item;
                    break;
                }
            }
            if (child.IsNull() || child.IsUndefined())
            {
                if (found is not null)
                    RemoveInline(table, found);
                continue;
            }
            if (found?.KeyValue is { } kv)
            {
                UpdateKeyValue(kv, child, newline);
                continue;
            }
            if (table.Items.ChildrenCount > 0)
            {
                var previous = table.Items.GetChild(table.Items.ChildrenCount - 1)!;
                previous.Comma ??= Comma();
            }
            var assignment = ParseAssignment(Key(key), Format(child) ?? "\"\"", newline);
            assignment.EndOfLineToken = null;
            table.Items.Add(new InlineTableItemSyntax(assignment));
        }
    }

    private void UpdateItems(SyntaxList<KeyValueSyntax> items, ObjectInstance desired, string newline)
    {
        foreach (var key in ObjectKeys(desired))
        {
            var child = desired.Get(key);
            var existing = FindItem(items, key);
            if (child.IsNull() || child.IsUndefined())
            {
                if (existing is not null)
                    Remove(items, existing, newline);
                continue;
            }
            if (existing is null)
            {
                if (Format(child) is { } text)
                    items.Add(ParseAssignment(Key(key), text, newline));
                continue;
            }
            UpdateKeyValue(existing, child, newline);
        }
    }

    private void ReplaceValue(KeyValueSyntax kv, JsValue desired, string newline)
    {
        if (Format(desired) is not { } text || kv.Value is null)
            return;
        kv.Value = ParsedValue(desired, newline, kv.Value, text);
    }

    private ValueSyntax ParsedValue(JsValue desired, string newline, ValueSyntax? previous, string? toml = null)
    {
        toml ??= Format(desired) ?? "\"\"";
        var parsed = ParseAssignment("k", toml, newline);
        var value = parsed.Value!;
        parsed.Value = null;
        if (previous is not null)
            LastToken(value).TrailingTrivia = LastToken(previous).TrailingTrivia;
        return value;
    }

    private static TableArraySyntax NewRow(string name, ObjectInstance obj, string newline)
    {
        var text = new StringBuilder();
        text.Append("[[").Append(Key(name)).Append("]]").Append(newline);
        foreach (var key in ObjectKeys(obj))
        {
            if (Format(obj.Get(key)) is { } value)
                text.Append(Key(key)).Append(" = ").Append(value).Append(newline);
        }
        var parsed = SyntaxParser.Parse(text.ToString());
        var table = (TableArraySyntax)parsed.Tables.GetChild(0)!;
        parsed.Tables.RemoveChild(table);
        return table;
    }

    private void AppendNew(DocumentSyntax document, string name, JsValue desired, JsValue comments, string newline)
    {
        if (Format(desired) is not { } text)
            return;
        AppendRaw(document, Key(name), text, newline, CommentLines(comments, name));
    }

    private static IEnumerable<string> CommentLines(JsValue comments, string name)
    {
        var comment = comments.IsObject() ? comments.AsObject().Get(name) : JsValue.Undefined;
        if (!comment.IsString())
            yield break;
        foreach (var line in comment.AsString().Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            yield return line.Length > 0 ? "# " + line : "#";
    }

    private static bool TryLeaf(ObjectInstance root, List<string> path, out JsValue leaf)
    {
        leaf = JsValue.Undefined;
        ObjectInstance current = root;
        foreach (var part in path)
        {
            if (!current.HasProperty(part))
                return false;
            leaf = current.Get(part);
            if (part != path[^1])
            {
                if (!leaf.IsObject() || leaf.IsArray())
                    return false;
                current = leaf.AsObject();
            }
        }
        return true;
    }

    private static bool IsObjectArray(JsArray array)
    {
        for (uint i = 0; i < array.Length; i++)
        {
            var item = array[i];
            if (!item.IsObject() || item.IsArray() || item.IsNull())
                return false;
        }
        return true;
    }

    private static IEnumerable<string> ObjectKeys(ObjectInstance obj)
    {
        foreach (var key in obj.GetOwnPropertyKeys(Jint.Runtime.Types.String))
            yield return key.AsString();
    }

    private static bool Equivalent(JsValue current, JsValue desired, string qmlType)
    {
        if (desired.IsNull() || desired.IsUndefined())
            return current.IsNull() || current.IsUndefined();
        if (qmlType == "bool" && desired.IsBoolean())
        {
            if (current.IsBoolean())
                return current.AsBoolean() == desired.AsBoolean();
            if (current.IsNumber())
            {
                var n = current.AsNumber();
                return desired.AsBoolean() == (n != 0 && !double.IsNaN(n));
            }
            return false;
        }
        if (desired.IsBoolean() && current.IsBoolean())
            return desired.AsBoolean() == current.AsBoolean();
        if (desired.IsNumber() && current.IsNumber())
            return NumbersEqual(current.AsNumber(), desired.AsNumber());
        if (desired.IsString() && current.IsString())
            return desired.AsString() == current.AsString();
        if (desired.IsArray() && current.IsArray())
        {
            var want = desired.AsArray();
            var have = current.AsArray();
            if (want.Length != have.Length)
                return false;
            for (uint i = 0; i < want.Length; i++)
            {
                if (!Equivalent(have[i], want[i], "var"))
                    return false;
            }
            return true;
        }
        if (desired.IsObject() && !desired.IsArray() && current.IsObject() && !current.IsArray())
        {
            foreach (var key in ObjectKeys(desired.AsObject()))
            {
                var child = desired.AsObject().Get(key);
                if (child.IsUndefined())
                    continue;
                var has = current.AsObject().HasProperty(key);
                if (child.IsNull())
                {
                    if (has)
                        return false;
                    continue;
                }
                if (!has || !Equivalent(current.AsObject().Get(key), child, "var"))
                    return false;
            }
            return true;
        }
        return false;
    }

    private static bool NumbersEqual(double left, double right) =>
        double.IsNaN(left) ? double.IsNaN(right) : left == right;

    private static bool HasUnsafeInteger(object? value) => value switch
    {
        long l => !IsExactLong(l),
        TomlArray array => array.Any(HasUnsafeInteger),
        TomlTable table => table.Any(pair => HasUnsafeInteger(pair.Value)),
        TomlTableArray tables => tables.Any(table => table.Any(pair => HasUnsafeInteger(pair.Value))),
        _ => false,
    };

    private static bool IsExactLong(long value)
    {
        var number = (double)value;
        return number >= -ExactIntegerLimit && number <= ExactIntegerLimit && (long)number == value;
    }

    private static bool IsIntegerNumber(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value == Math.Floor(value) && Math.Abs(value) <= ExactIntegerLimit;

    private string Refusal(string key, JsValue value)
    {
        if (Check is not JsValue check || !check.IsCallable() || Engine is null)
            return "";
        try
        {
            var result = Engine.Js.CallFunction(check, null, [key, value]);
            return result.IsString() ? result.AsString() : "";
        }
        catch (Jint.Runtime.JavaScriptException e)
        {
            return $"could not be checked: {e.Message}";
        }
    }

    private void Leave(string key, string message)
    {
        _leave.Add(key);
        Warn($"{Where}: {message}");
    }

    private void Warn(string message) => Engine?.Log.Warn("io", "TomlAdapter: " + message);

    private string Where => string.IsNullOrEmpty(View?.Path) ? "the file" : View!.Path!;

    private static JsValue ToJs(object? value, Jint.Engine engine) => value switch
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

    private static JsObject Object(TomlTable table, Jint.Engine engine)
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
            if (IsIntegerNumber(d))
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
}
