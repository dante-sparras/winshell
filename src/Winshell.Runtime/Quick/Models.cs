using Jint;
using Jint.Native;

namespace Winshell.Runtime.Quick;

/// <summary>A model whose rows can change without the model property being assigned again.</summary>
public interface IObservableModel
{
    event Action? ModelChanged;

    IReadOnlyList<object?> Rows { get; }

    /// <summary>The roles of a row, as names a delegate can use directly, or null for plain values.</summary>
    IReadOnlyDictionary<string, object?>? RolesOf(int index) => null;
}

/// <summary>One row of a model, as a delegate sees it.</summary>
public sealed record ModelEntry(object? ModelData, IReadOnlyDictionary<string, object?>? Roles);

/// <summary>Reads the kinds of value QML accepts as a model: a count, an array, a list or a model object.</summary>
public static class ModelReader
{
    public static List<ModelEntry> Read(object? model)
    {
        model = model is JsValue js ? ValueConverter.KeepJs(js) : model;
        switch (model)
        {
            case null:
                return [];
            case double d:
                return Enumerable.Range(0, Math.Max(0, (int)d)).Select(i => new ModelEntry(i, null)).ToList();
            case int n:
                return Enumerable.Range(0, Math.Max(0, n)).Select(i => new ModelEntry(i, null)).ToList();
            case IObservableModel observable:
                {
                    var rows = observable.Rows;
                    return rows.Select((r, i) => new ModelEntry(r, observable.RolesOf(i))).ToList();
                }
            case IQmlList list:
                return list.ToList().Select(o => new ModelEntry(o, null)).ToList();
            case JsValue array when array.IsArray():
                {
                    var a = array.AsArray();
                    var result = new List<ModelEntry>((int)a.Length);
                    for (uint i = 0; i < a.Length; i++)
                        result.Add(new ModelEntry(ValueConverter.KeepJs(a[i]), null));
                    return result;
                }
            case System.Collections.IEnumerable items when model is not string:
                return items.Cast<object?>().Select(o => new ModelEntry(o, null)).ToList();
            default:
                return [new ModelEntry(model, null)];
        }
    }

    /// <summary>The names a delegate instance sees: <c>index</c>, <c>modelData</c>, <c>model</c>, and each role.</summary>
    public static Dictionary<string, object?> ContextFor(ModelEntry entry, int index)
    {
        var props = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["index"] = index,
            ["modelData"] = entry.ModelData,
        };
        if (entry.Roles is not null)
        {
            foreach (var (k, v) in entry.Roles)
                props[k] = v;
            props["model"] = entry.Roles;
        }
        else
        {
            props["model"] = entry.ModelData;
        }
        return props;
    }

    /// <summary>
    /// Whether two rows are the same row, so a delegate made for one can stay for the other. Objects compare by
    /// identity; plain values by value.
    /// </summary>
    public static bool SameRow(ModelEntry a, ModelEntry b)
    {
        if (a.Roles is not null || b.Roles is not null)
            return ReferenceEquals(a.Roles, b.Roles);
        return a.ModelData switch
        {
            QObject or JsValue => ReferenceEquals(a.ModelData, b.ModelData),
            _ => Equals(a.ModelData, b.ModelData),
        };
    }
}

/// <summary>QtQml.Models' <c>ListElement</c>.</summary>
public sealed class ListElement : QObject, IAcceptsAnyProperty;

/// <summary>QtQml.Models' <c>ListModel</c>.</summary>
[QmlDefaultProperty("elements")]
public sealed class ListModel : QObject, IObservableModel
{
    private readonly List<Dictionary<string, object?>> _rows = [];
    private readonly QmlList<ListElement> _elements;

    public ListModel()
    {
        _elements = new QmlList<ListElement>(this, "elements", added: e =>
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var p in e.DynamicProperties)
                row[p.Name] = p.Read();
            _pendingElements.Add((e, row));
        });
    }

    private readonly List<(ListElement Element, Dictionary<string, object?> Row)> _pendingElements = [];

    public event Action? ModelChanged;

    [Qml]
    public QmlList<ListElement> Elements => _elements;

    [Qml]
    public int Count
    {
        get { Track(); return _rows.Count; }
    }

    [Qml]
    public bool DynamicRoles { get; set; }

    public IReadOnlyList<object?> Rows => _rows;

    public IReadOnlyDictionary<string, object?>? RolesOf(int index) => _rows[index];

    public override void ComponentComplete()
    {
        // ListElement properties are plain declarations; take their values once they exist.
        foreach (var (element, _) in _pendingElements)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var p in element.DynamicProperties)
                row[p.Name] = p.Read();
            _rows.Add(row);
        }
        _pendingElements.Clear();
        Changed();
    }

    private void Changed()
    {
        NotifyChanged("count");
        ModelChanged?.Invoke();
    }

    private static Dictionary<string, object?> RowFrom(object? value)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (value is JsValue js && js.IsObject())
        {
            var obj = js.AsObject();
            foreach (var key in obj.GetOwnPropertyKeys())
            {
                if (key.IsString())
                    row[key.AsString()] = ValueConverter.KeepJs(obj.Get(key));
            }
        }
        else if (value is IDictionary<string, object?> dict)
        {
            foreach (var (k, v) in dict)
                row[k] = v;
        }
        return row;
    }

    [Qml]
    public void Append(object? value)
    {
        if (value is JsValue js && js.IsArray())
        {
            var a = js.AsArray();
            for (uint i = 0; i < a.Length; i++)
                _rows.Add(RowFrom(a[i]));
        }
        else
        {
            _rows.Add(RowFrom(value));
        }
        Changed();
    }

    [Qml]
    public void Insert(int index, object? value)
    {
        _rows.Insert(Math.Clamp(index, 0, _rows.Count), RowFrom(value));
        Changed();
    }

    [Qml]
    public void Remove(int index, int count = 1)
    {
        if (index < 0 || index >= _rows.Count)
            return;
        _rows.RemoveRange(index, Math.Min(count, _rows.Count - index));
        Changed();
    }

    [Qml]
    public void Clear()
    {
        _rows.Clear();
        Changed();
    }

    [Qml]
    public object? Get(int index) => index >= 0 && index < _rows.Count ? _rows[index] : null;

    [Qml]
    public void Set(int index, object? value)
    {
        if (index == _rows.Count)
        {
            Append(value);
            return;
        }
        if (index < 0 || index > _rows.Count)
            return;
        foreach (var (k, v) in RowFrom(value))
            _rows[index][k] = v;
        Changed();
    }

    [Qml]
    public void SetProperty(int index, string property, object? value)
    {
        if (index < 0 || index >= _rows.Count)
            return;
        _rows[index][property] = ValueConverter.KeepJs(value);
        Changed();
    }

    [Qml]
    public void Move(int from, int to, int count = 1)
    {
        if (from < 0 || to < 0 || from + count > _rows.Count || to + count > _rows.Count)
            return;
        var moved = _rows.GetRange(from, count);
        _rows.RemoveRange(from, count);
        _rows.InsertRange(to, moved);
        Changed();
    }

    [Qml]
    public void Sync()
    {
    }
}
