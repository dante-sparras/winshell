namespace Winshell.Runtime.Quick;

/// <summary>An item that arranges its children before the next frame, such as Row or RowLayout.</summary>
public interface IPolishable
{
    void UpdatePolish();
}

/// <summary>
/// Collects items that need arranging and arranges them once, after the current work, the way QtQuick
/// polishes items before it draws.
/// </summary>
public static class PolishQueue
{
    [ThreadStatic]
    private static List<IPolishable>? _pending;

    [ThreadStatic]
    private static HashSet<IPolishable>? _set;

    // The loop a flush is posted to. A loop that never runs again, such as a test's or a failed load's, must not
    // keep later requests from being flushed, or the queue would hold their items, and their generations, for good.
    [ThreadStatic]
    private static IEventLoop? _scheduledOn;

    public static void Request(IPolishable item, IEventLoop? loop)
    {
        _pending ??= [];
        _set ??= [];
        if (!_set.Add(item))
            return;
        _pending.Add(item);
        if (loop is null || _scheduledOn == loop)
            return;
        _scheduledOn = loop;
        loop.Post(Flush);
    }

    /// <summary>Arranges every item that asked. Arranging one can make another ask, so this repeats.</summary>
    public static void Flush()
    {
        _scheduledOn = null;
        for (var pass = 0; pass < 50 && _pending is { Count: > 0 }; pass++)
        {
            var items = _pending.ToArray();
            _pending.Clear();
            _set!.Clear();
            foreach (var item in items)
            {
                if (item is QObject { IsDestroyed: true })
                    continue;
                item.UpdatePolish();
            }
        }
        if (_pending is { Count: > 0 })
            Log.Default.Warn("layout", "A layout pass did not settle after 50 iterations.");
    }
}
