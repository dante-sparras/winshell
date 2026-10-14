using Microsoft.UI.Xaml;

namespace Winshell.Kit;

/// <summary>Part of a panel built in its own scope by <see cref="Kit.Region"/>. Dropping it stops reading its getters.</summary>
public sealed class KitRegion
{
    private readonly Action _drop;
    private UIElement? _element;

    internal KitRegion(UIElement element, Action drop)
    {
        _element = element;
        _drop = drop;
    }

    /// <summary>What the part drew. It is gone once the panel has closed.</summary>
    /// <exception cref="InvalidOperationException">The panel this part was built for has closed.</exception>
    public UIElement Element => _element ?? throw new InvalidOperationException("The panel this part was built for has closed.");

    /// <summary>Stops reading the part's getters. Call when the part leaves the screen.</summary>
    public void Drop() => _drop();

    /// <summary>The panel closed. A plugin that still holds the region no longer holds what it drew.</summary>
    internal void Forget() => _element = null;
}
