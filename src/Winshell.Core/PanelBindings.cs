using Winshell.Core.Diagnostics;

namespace Winshell.Core;

/// <summary>Something a Kit control shows that is read from the plugin again on every refresh.</summary>
public interface IPanelBinding
{
    /// <summary>Reads the value and writes it to the screen when it changed.</summary>
    /// <returns>How many writes reached the screen.</returns>
    int Refresh();
}

/// <summary>
/// One value a Kit control shows. The getter is read on every refresh of the open panel, and the screen is written
/// only when the value differs from the one it holds, the way a QML binding repaints only what moved (ADR 0045).
/// </summary>
/// <typeparam name="T">The value's type.</typeparam>
public sealed class PanelBinding<T> : IPanelBinding
{
    private readonly Func<T> _read;
    private readonly Action<T> _write;
    private readonly IEqualityComparer<T> _same;
    private bool _written;
    private T _shown = default!;

    /// <summary>Binds <paramref name="read"/> to <paramref name="write"/>. Nothing is read until the first refresh.</summary>
    /// <param name="read">The plugin's getter. It runs once per refresh, so it must be cheap.</param>
    /// <param name="write">Puts a new value on the screen.</param>
    /// <param name="same">How two values are compared. Null uses the type's own equality.</param>
    public PanelBinding(Func<T> read, Action<T> write, IEqualityComparer<T>? same = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
        _same = same ?? EqualityComparer<T>.Default;
    }

    /// <summary>The value on screen. Default before the first write.</summary>
    public T Shown => _shown;

    /// <summary>
    /// Reads the getter and writes the value when it is the first or it changed.
    /// A getter that throws is logged and the screen keeps the value it has.
    /// </summary>
    /// <returns>1 when the screen was written, otherwise 0.</returns>
    public int Refresh()
    {
        T value;
        try
        {
            value = _read();
        }
        catch (Exception ex)
        {
            Log.Warning("Panel", "a panel value could not be read", ex);
            return 0;
        }

        if (_written && _same.Equals(_shown, value))
        {
            return 0;
        }

        _shown = value;
        _written = true;
        try
        {
            _write(value);
        }
        catch (Exception ex)
        {
            // Thrown past here it would skip every later binding and the rest of the bar's turn.
            Log.Warning("Panel", "a panel value could not be shown", ex);
        }

        return 1;
    }
}

/// <summary>
/// The bindings of an open panel, or of one item in a keyed list. A group refreshes its bindings in the order they were added.
/// An item's group is dropped with the item, so a removed row stops reading its getters.
/// </summary>
public sealed class PanelBindings : IPanelBinding
{
    private readonly List<IPanelBinding> _bindings = [];

    // What a refresh walks. Made again only after the list changed, so a refresh of an unchanged group allocates nothing.
    private IPanelBinding[]? _walk;

    /// <summary>How many bindings and groups this group holds.</summary>
    public int Count => _bindings.Count;

    /// <summary>Adds <paramref name="binding"/> and refreshes it once, so the control shows its first value as it is built.</summary>
    /// <param name="binding">A binding or a group.</param>
    /// <returns>The same binding.</returns>
    public TBinding Add<TBinding>(TBinding binding)
        where TBinding : IPanelBinding
    {
        ArgumentNullException.ThrowIfNull(binding);
        _bindings.Add(binding);
        _walk = null;
        binding.Refresh();
        return binding;
    }

    /// <summary>Stops refreshing <paramref name="binding"/>.</summary>
    /// <param name="binding">A binding or a group added earlier.</param>
    public void Remove(IPanelBinding binding)
    {
        if (_bindings.Remove(binding))
        {
            _walk = null;
        }
    }

    /// <summary>Drops every binding.</summary>
    public void Clear()
    {
        _bindings.Clear();
        _walk = null;
    }

    /// <inheritdoc />
    public int Refresh()
    {
        var writes = 0;

        // A write can add or remove bindings, such as a keyed list building a new item, so walk a copy.
        // The copy is the one the last refresh walked unless the list changed since.
        var walk = _walk ??= [.. _bindings];
        foreach (var binding in walk)
        {
            writes += binding.Refresh();
        }

        return writes;
    }
}
