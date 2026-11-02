using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;
using static Winshell.TomlSyntax;

namespace Winshell;

/// <summary>
/// A TOML file's text, read and edited as plain values: string, bool, long, double,
/// <see cref="IReadOnlyList{T}"/> of object and <see cref="IReadOnlyDictionary{TKey, TValue}"/> of string and object.
/// A date or a time reads as a string, and its token stays while that string does. <see cref="Set"/> changes one key
/// and leaves the rest of the text: comments, blank lines, number spellings and other keys stay. A table, an array of
/// tables, or a dotted key already in the file is the value: it is updated in place and never written a second time.
/// A key the file does not have yet goes at the end of its top-level keys, under its comment.
/// </summary>
internal sealed class TomlDocument
{
    private readonly DocumentSyntax _document;
    private readonly string _newline;
    private IReadOnlyDictionary<string, object?>? _values;

    private TomlDocument(DocumentSyntax document, TomlTable table)
    {
        _document = document;
        _newline = NewlineOf(document);
        _values = PlainTable(table);
    }

    /// <summary>The top-level keys and their values.</summary>
    public IReadOnlyDictionary<string, object?> Values =>
        _values ??= PlainTable(TomlSerializer.Deserialize<TomlTable>(_document.ToString()) ?? []);

    /// <summary>
    /// Reads <paramref name="text"/>. <paramref name="error"/> is <c>path[line:column]: message</c>, or
    /// <c>path: message</c> for a file past 1 MiB or nesting past 64 levels, when it does not parse.
    /// </summary>
    public static bool TryParse(string where, string text, [NotNullWhen(true)] out TomlDocument? document, out string error)
    {
        document = null;
        if (!TryLoad(where, text, out var syntax, out var table, out error))
            return false;
        document = new TomlDocument(syntax, table);
        return true;
    }

    /// <summary>
    /// Makes <paramref name="key"/> hold <paramref name="value"/>. Null takes the key out, because TOML has no null,
    /// and the comments around it stay. A key the file does not have yet is written under <paramref name="comment"/>.
    /// </summary>
    public void Set(string key, object? value, string? comment = null)
    {
        var desired = Plain(value);
        var owned = FindOwned(_document, key);
        if (desired is null)
            Take(owned);
        else if (!owned.Defined)
            AppendNew(key, desired, comment);
        else if (Same(Values.GetValueOrDefault(key), desired))
            return;
        else if (!UpdateOwned(owned, key, desired))
        {
            Take(owned);
            AppendNew(key, desired, comment);
        }
        _values = null;
    }

    /// <summary>The file's text.</summary>
    public override string ToString() => _document.ToString();

    /// <summary>A value as the plain types this class reads and writes.</summary>
    public static object? Plain(object? value) => value switch
    {
        null or string or bool or long or double => value,
        int i => (long)i,
        float f => (double)f,
        TomlTable table => PlainTable(table),
        IReadOnlyDictionary<string, object?> table => table.ToDictionary(pair => pair.Key, pair => Plain(pair.Value)),
        IEnumerable items => items.Cast<object?>().Select(Plain).ToList(),
        // A date or a time.
        _ => value.ToString() ?? "",
    };

    /// <summary>Whether two plain values are the same value. An integer and a float can be.</summary>
    public static bool Same(object? left, object? right) => (left, right) switch
    {
        (IReadOnlyList<object?> a, IReadOnlyList<object?> b) => a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second)),
        (IReadOnlyDictionary<string, object?> a, IReadOnlyDictionary<string, object?> b) =>
            a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && Same(pair.Value, other)),
        (long a, double b) => a == b,
        (double a, long b) => a == b,
        _ => Equals(left, right),
    };

    private static Dictionary<string, object?> PlainTable(TomlTable table) =>
        table.ToDictionary(pair => pair.Key, pair => Plain(pair.Value));

    private void Take(Owned owned)
    {
        foreach (var kv in owned.KeyValues.ToList())
            RemoveKey(_document, kv, _newline);
        foreach (var table in owned.Tables.ToList())
            RemoveTable(_document, table);
    }

    private void AppendNew(string key, object desired, string? comment)
    {
        if (Format(desired) is { } text)
            AppendRaw(_document, Key(key), text, _newline, CommentLines(comment));
    }

    private static IEnumerable<string> CommentLines(string? comment)
    {
        if (comment is null)
            yield break;
        foreach (var line in comment.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            yield return line.Length > 0 ? "# " + line : "#";
    }

    private bool UpdateOwned(Owned owned, string name, object desired)
    {
        var simple = owned.KeyValues.Where(kv => IsSimple(kv.Key)).ToList();
        var dotted = owned.KeyValues.Where(kv => !IsSimple(kv.Key)).ToList();
        var rows = owned.Tables.OfType<TableArraySyntax>().Where(t => IsSimple(t.Name)).ToList();
        var tables = owned.Tables.OfType<TableSyntax>().Where(t => IsSimple(t.Name)).ToList();
        if (simple.Count > 0 && rows.Count == 0 && tables.Count == 0 && dotted.Count == 0)
        {
            UpdateKeyValue(simple[0], desired);
            return true;
        }
        // No rows is not an array of tables, so an empty list is written as [] and stays a choice the file remembers.
        if (simple.Count == 0 && dotted.Count == 0 && tables.Count == 0 && rows.Count > 0
            && desired is IReadOnlyList<object?> { Count: > 0 } list && list.All(item => item is IReadOnlyDictionary<string, object?>))
        {
            UpdateRows(rows, list, name);
            return true;
        }
        if (simple.Count == 0 && rows.Count == 0 && desired is IReadOnlyDictionary<string, object?> table
            && (tables.Count > 0 || dotted.Count > 0))
        {
            if (tables.Count > 0)
                UpdateItems(tables[0], table);
            if (dotted.Count > 0)
                UpdateDotted(dotted, table, name, addMissing: tables.Count == 0);
            return true;
        }
        return false;
    }

    private void UpdateRows(List<TableArraySyntax> rows, IReadOnlyList<object?> desired, string name)
    {
        var shared = Math.Min(desired.Count, rows.Count);
        for (var i = 0; i < shared; i++)
            UpdateItems(rows[i], (IReadOnlyDictionary<string, object?>)desired[i]!);
        for (var i = rows.Count - 1; i >= desired.Count; i--)
            RemoveTable(_document, rows[i]);
        TableSyntaxBase after = rows[shared - 1];
        for (var i = rows.Count; i < desired.Count; i++)
        {
            var table = NewRow(name, (IReadOnlyDictionary<string, object?>)desired[i]!);
            InsertAfter(_document, after, table);
            after = table;
        }
    }

    private void UpdateDotted(List<KeyValueSyntax> dotted, IReadOnlyDictionary<string, object?> desired, string name, bool addMissing)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in dotted)
        {
            var path = Segments(kv.Key!).Skip(1).ToList();
            if (!TryLeaf(desired, path, out var leaf))
                continue;
            seen.Add(string.Join(".", path));
            if (leaf is null)
                RemoveKey(_document, kv, _newline);
            else
                UpdateKeyValue(kv, leaf);
        }
        if (addMissing)
            AddDotted(desired, [name], seen);
    }

    private void AddDotted(IReadOnlyDictionary<string, object?> table, List<string> prefix, HashSet<string> seen)
    {
        foreach (var (key, child) in table)
        {
            var path = prefix.Append(key).ToList();
            if (child is IReadOnlyDictionary<string, object?> nested)
            {
                AddDotted(nested, path, seen);
                continue;
            }
            if (Format(child) is { } written && !seen.Contains(string.Join(".", path.Skip(1))))
                AppendRaw(_document, string.Join(".", path.Select(Key)), written, _newline);
        }
    }

    private static bool TryLeaf(IReadOnlyDictionary<string, object?> root, List<string> path, out object? leaf)
    {
        leaf = null;
        var current = root;
        for (var i = 0; i < path.Count; i++)
        {
            if (!current.TryGetValue(path[i], out leaf))
                return false;
            if (i == path.Count - 1)
                break;
            if (leaf is not IReadOnlyDictionary<string, object?> next)
                return false;
            current = next;
        }
        return true;
    }

    private void UpdateKeyValue(KeyValueSyntax kv, object desired)
    {
        if (kv.Value is null)
            return;
        if (!TryUpdate(kv.Value, desired))
            kv.Value = ParsedValue(desired, kv.Value);
    }

    private bool TryUpdate(ValueSyntax node, object? desired)
    {
        switch (node)
        {
            case StringValueSyntax s when desired is string text:
                if (s.Value != text)
                    SetString(s, text);
                return true;
            case BooleanValueSyntax b when desired is bool flag:
                if (b.Value != flag)
                    SetBool(b, flag);
                return true;
            case IntegerValueSyntax integer when desired is long number:
                if (integer.Value != number)
                {
                    integer.Value = number;
                    integer.Token!.TokenKind = TokenKind.Integer;
                    integer.Token.Text = number.ToString(CultureInfo.InvariantCulture);
                }
                return true;
            case FloatValueSyntax number when desired is double value:
                return UpdateFloat(number, value);
            case DateTimeValueSyntax date when desired is string written:
                // The token is the user's spelling. Quoting it would turn a date into a string.
                return date.Token?.Text == written || date.Value.ToString() == written;
            case ArraySyntax array when desired is IReadOnlyList<object?> list:
                UpdateArray(array, list);
                return true;
            case InlineTableSyntax table when desired is IReadOnlyDictionary<string, object?> items:
                UpdateInline(table, items);
                return true;
            default:
                return false;
        }
    }

    private static bool UpdateFloat(FloatValueSyntax node, double desired)
    {
        if (node.Value.Equals(desired))
            return true;
        if (double.IsNaN(desired) || double.IsInfinity(desired))
            return false;
        node.Value = desired;
        node.Token!.TokenKind = TokenKind.Float;
        node.Token.Text = Number(desired);
        return true;
    }

    private void UpdateArray(ArraySyntax array, IReadOnlyList<object?> desired)
    {
        var shared = Math.Min(desired.Count, array.Items.ChildrenCount);
        for (var i = 0; i < shared; i++)
        {
            var item = array.Items.GetChild(i)!;
            if (item.Value is null || !TryUpdate(item.Value, desired[i]))
                item.Value = ParsedValue(desired[i], item.Value);
        }
        while (array.Items.ChildrenCount > desired.Count)
            array.Items.RemoveChildAt(array.Items.ChildrenCount - 1);
        for (var i = array.Items.ChildrenCount; i < desired.Count; i++)
        {
            if (array.Items.ChildrenCount > 0)
            {
                var previous = array.Items.GetChild(array.Items.ChildrenCount - 1)!;
                previous.Comma ??= Comma();
            }
            array.Items.Add(new ArrayItemSyntax { Value = ParsedValue(desired[i], null) });
        }
    }

    private void UpdateInline(InlineTableSyntax table, IReadOnlyDictionary<string, object?> desired)
    {
        foreach (var (key, child) in desired)
        {
            InlineTableItemSyntax? found = null;
            foreach (var item in table.Items)
            {
                if (Names(item.KeyValue?.Key, key))
                {
                    found = item;
                    break;
                }
            }
            if (child is null)
            {
                if (found is not null)
                    RemoveInline(table, found);
                continue;
            }
            if (found?.KeyValue is { } kv)
            {
                UpdateKeyValue(kv, child);
                continue;
            }
            if (table.Items.ChildrenCount > 0)
            {
                var previous = table.Items.GetChild(table.Items.ChildrenCount - 1)!;
                previous.Comma ??= Comma();
            }
            var assignment = ParseAssignment(Key(key), Format(child) ?? "\"\"", _newline);
            assignment.EndOfLineToken = null;
            table.Items.Add(new InlineTableItemSyntax(assignment));
        }
    }

    private void UpdateItems(TableSyntaxBase table, IReadOnlyDictionary<string, object?> desired)
    {
        var items = table.Items;
        foreach (var (key, child) in desired)
        {
            var existing = FindItem(items, key);
            if (child is null)
            {
                if (existing is not null && Remove(items, existing, _newline) is { } kept && table.EndOfLineToken is { } line)
                    line.TrailingTrivia = [.. line.TrailingTrivia ?? [], .. kept];
                continue;
            }
            if (existing is not null)
                UpdateKeyValue(existing, child);
            else if (Format(child) is { } text)
                items.Add(ParseAssignment(Key(key), text, _newline));
        }
    }

    private ValueSyntax ParsedValue(object? desired, ValueSyntax? previous)
    {
        var parsed = ParseAssignment("k", Format(desired) ?? "\"\"", _newline);
        var value = parsed.Value!;
        parsed.Value = null;
        if (previous is not null)
            LastToken(value).TrailingTrivia = LastToken(previous).TrailingTrivia;
        return value;
    }

    private TableArraySyntax NewRow(string name, IReadOnlyDictionary<string, object?> row)
    {
        var text = new StringBuilder();
        text.Append("[[").Append(Key(name)).Append("]]").Append(_newline);
        foreach (var (key, child) in row)
        {
            if (Format(child) is { } value)
                text.Append(Key(key)).Append(" = ").Append(value).Append(_newline);
        }
        var parsed = SyntaxParser.Parse(text.ToString());
        var table = (TableArraySyntax)parsed.Tables.GetChild(0)!;
        parsed.Tables.RemoveChild(table);
        return table;
    }

    /// <summary>A value as TOML, or null for one TOML cannot hold.</summary>
    private static string? Format(object? value)
    {
        switch (value)
        {
            case string text:
                return Quote(text);
            case bool flag:
                return flag ? "true" : "false";
            case long number:
                return number.ToString(CultureInfo.InvariantCulture);
            case double number:
                return Number(number);
            case IReadOnlyList<object?> list:
                return "[" + string.Join(", ", list.Select(Format).OfType<string>()) + "]";
            case IReadOnlyDictionary<string, object?> table:
                var items = table
                    .Where(pair => Format(pair.Value) is not null)
                    .Select(pair => Key(pair.Key) + " = " + Format(pair.Value))
                    .ToList();
                return items.Count == 0 ? "{}" : "{ " + string.Join(", ", items) + " }";
            default:
                return null;
        }
    }

    /// <summary>A double as a TOML float. Without a fraction or an exponent it would read back as an integer.</summary>
    private static string Number(double value)
    {
        if (double.IsNaN(value))
            return "nan";
        if (double.IsInfinity(value))
            return value > 0 ? "inf" : "-inf";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.AsSpan().ContainsAny('.', 'E') ? text : text + ".0";
    }
}
