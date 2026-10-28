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

    [ThreadStatic]
    private static bool _scheduled;

    public static void Request(IPolishable item, IEventLoop? loop)
    {
        _pending ??= [];
        _set ??= [];
        if (!_set.Add(item))
            return;
        _pending.Add(item);
        if (_scheduled || loop is null)
            return;
        _scheduled = true;
        loop.Post(Flush);
    }

    /// <summary>Arranges every item that asked. Arranging one can make another ask, so this repeats.</summary>
    public static void Flush()
    {
        _scheduled = false;
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
    }
}
