using System.Collections;

namespace Winshell.Runtime;

/// <summary>A QML list property: <c>list&lt;Item&gt;</c>, <c>children</c>, <c>data</c>.</summary>
public interface IQmlList : IEnumerable<object?>
{
    int Count { get; }

    object? this[int index] { get; }

    Type ElementType { get; }

    void Append(object? item);

    void Clear();

    event Action? Changed;
}

/// <summary>A list property backed by a list, with hooks for what happens when an item joins or leaves.</summary>
public class QmlList<T> : IQmlList
    where T : class
{
    private readonly List<T> _items = [];
    private readonly Action<T>? _added;
    private readonly Action<T>? _removed;
    private readonly QObject? _owner;
    private readonly string? _name;

    public QmlList(QObject? owner = null, string? name = null, Action<T>? added = null, Action<T>? removed = null)
    {
        _owner = owner;
        _name = name;
        _added = added;
        _removed = removed;
    }

    public event Action? Changed;

    public int Count
    {
        get
        {
            Track();
            return _items.Count;
        }
    }

    public T this[int index]
    {
        get
        {
            Track();
            return _items[index];
        }
    }

    object? IQmlList.this[int index] => this[index];

    public Type ElementType => typeof(T);

    public void Append(object? item)
    {
        if (item is null)
            return;
        if (item is not T typed)
            throw new QmlException($"Cannot add {item} to a list of {typeof(T).Name}");
        Add(typed);
    }

    public void Add(T item)
    {
        _items.Add(item);
        _added?.Invoke(item);
        OnChanged();
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        _added?.Invoke(item);
        OnChanged();
    }

    public bool Remove(T item)
    {
        if (!_items.Remove(item))
            return false;
        _removed?.Invoke(item);
        OnChanged();
        return true;
    }

    public void Move(int from, int to)
    {
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        OnChanged();
    }

    /// <summary>Puts the same items in a new order, as one change.</summary>
    public void Reorder(IReadOnlyList<T> items)
    {
        if (items.Count != _items.Count)
            throw new ArgumentException("A reorder keeps the same items.", nameof(items));
        _items.Clear();
        _items.AddRange(items);
        OnChanged();
    }

    public int IndexOf(T item) => _items.IndexOf(item);

    public bool Contains(T item) => _items.Contains(item);

    public void Clear()
    {
        if (_items.Count == 0)
            return;
        var old = _items.ToArray();
        _items.Clear();
        foreach (var item in old)
            _removed?.Invoke(item);
        OnChanged();
    }

    /// <summary>The items without tracking the read.</summary>
    public IReadOnlyList<T> Snapshot => _items;

    private void Track()
    {
        if (_owner is not null && _name is not null)
            DependencyTracker.Read(_owner, _name);
    }

    private void OnChanged()
    {
        Changed?.Invoke();
        if (_owner is not null && _name is not null)
            _owner.NotifyChanged(_name);
    }

    /// <summary>The items, with the read tracked.</summary>
    public IReadOnlyList<T> Items
    {
        get
        {
            Track();
            return _items.ToList();
        }
    }

    public IEnumerator<object?> GetEnumerator()
    {
        Track();
        return _items.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
