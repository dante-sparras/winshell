using System.Collections.ObjectModel;

namespace Winshell;

/// <summary>Brings a collection of live objects to a new snapshot.</summary>
internal static class ObservableSync
{
    /// <summary>
    /// Makes <paramref name="items"/> match <paramref name="next"/> in order. An object whose key is still there is
    /// kept and updated, because a UI holds on to it. <paramref name="removed"/> hears about each object that goes.
    /// </summary>
    public static void Sync<T, TInfo, TKey>(this ObservableCollection<T> items, IEnumerable<TInfo> next, Func<T, TKey> key, Func<TInfo, TKey> infoKey,
        Func<TInfo, T> create, Action<T, TInfo> update, Action<T>? removed = null)
        where TKey : notnull
    {
        var known = new Dictionary<TKey, Queue<T>>();
        foreach (var item in items)
        {
            if (!known.TryGetValue(key(item), out var same))
                known[key(item)] = same = new Queue<T>();
            same.Enqueue(item);
        }
        var index = 0;
        foreach (var info in next)
        {
            if (known.TryGetValue(infoKey(info), out var same) && same.TryDequeue(out var kept))
            {
                update(kept, info);
                var at = items.IndexOf(kept);
                if (at != index)
                    items.Move(at, index);
            }
            else
            {
                var item = create(info);
                update(item, info);
                items.Insert(index, item);
            }
            index++;
        }
        for (var i = items.Count - 1; i >= index; i--)
        {
            var gone = items[i];
            items.RemoveAt(i);
            removed?.Invoke(gone);
        }
    }
}
