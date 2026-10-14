using Microsoft.UI.Xaml;

namespace Winshell.Kit;

/// <summary>
/// A plugin that draws its panel with the Kit (ADR 0045). It calls <c>Context.OpenPanel()</c>, and the host builds the panel
/// with <see cref="BuildPanel"/>. While the panel is open, the host calls <see cref="UpdatePanel"/> on every tick, then reads
/// the getters the controls were given and changes only what moved.
/// </summary>
public interface IKitPanel
{
    /// <summary>Builds the panel. Called when it opens, and again after <see cref="Kit.Rebuild"/>.</summary>
    /// <param name="kit">The Kit for this panel. Its controls belong to this panel only.</param>
    /// <returns>What the panel shows.</returns>
    KitPanel BuildPanel(Kit kit);

    /// <summary>Fetches what the getters read, when that cannot happen inside a getter. Most plugins leave this empty.</summary>
    void UpdatePanel()
    {
    }

    /// <summary>
    /// Hears a key before the panel acts on it, Tab and Escape included. True means the plugin used the key:
    /// the panel then does nothing with it and no control receives it. The panel reads its getters after a key that was used.
    /// A held key arrives again with <see cref="KitKey.Repeat"/> set, so a plugin can let an arrow repeat and keep Enter from running twice.
    /// Most plugins leave this alone, so the arrow keys and Return move the Kit's selection, Tab goes to the next panel, and Escape closes this one.
    /// A plugin that moves a selection of its own here returns its panel <see cref="KitPanel.WithOwnKeys"/>.
    /// </summary>
    /// <param name="key">The key and the modifiers held with it.</param>
    /// <returns>True when the plugin used the key.</returns>
    bool OnKey(KitKey key) => false;
}

/// <summary>A key pressed while a panel has the keyboard.</summary>
/// <param name="Key">The key.</param>
/// <param name="Ctrl">True while Control is held.</param>
/// <param name="Shift">True while Shift is held.</param>
/// <param name="Alt">True while Alt is held.</param>
/// <param name="Repeat">True when the key was already down: Windows sends a held key again and again.</param>
public readonly record struct KitKey(Windows.System.VirtualKey Key, bool Ctrl, bool Shift, bool Alt, bool Repeat = false);

/// <summary>What a Kit panel shows, and its shape.</summary>
public sealed class KitPanel
{
    internal KitPanel(UIElement content, int width, int height, bool scrolls)
    {
        Content = content;
        Width = width;
        Height = height;
        Scrolls = scrolls;
    }

    /// <summary>The rows, top to bottom.</summary>
    public UIElement Content { get; }

    /// <summary>The width in device-independent pixels. Zero uses the usual width.</summary>
    public int Width { get; }

    /// <summary>The height in device-independent pixels, for rows of a fixed shape such as the tray grid. Zero fits the rows.</summary>
    public int Height { get; }

    /// <summary>False for a panel of a fixed shape, such as a month grid, that never scrolls.</summary>
    public bool Scrolls { get; }

    /// <summary>
    /// The height the panel grows to while it is open, in device-independent pixels. Zero for a panel that keeps its height.
    /// A panel at the middle of the screen is placed as one this tall, so its first row stays where it is as the panel grows.
    /// </summary>
    public int Room { get; private init; }

    /// <summary>
    /// True when the host keeps <see cref="Spacing.PanelPadding"/> between the panel's edge and its rows, on every side.
    /// False for a panel that draws to its own edge, as the Menu does.
    /// </summary>
    public bool Padded { get; private init; } = true;

    /// <summary>
    /// True for a panel that moves a selection of its own from <see cref="IKitPanel.OnKey"/>, as the Menu and Activity do.
    /// The arrow keys and Return then move no Kit selection in it, and a key its plugin leaves alone goes to the control that has the keyboard.
    /// </summary>
    public bool OwnKeys { get; private init; }

    /// <summary>This panel with room to grow to <paramref name="height"/>, as the Menu grows from its search line to its rows.</summary>
    /// <param name="height">The tallest the panel gets, in device-independent pixels.</param>
    /// <returns>The panel, placed for that height.</returns>
    public KitPanel WithRoom(int height) => new(Content, Width, Height, Scrolls) { Room = height, Padded = Padded, OwnKeys = OwnKeys };

    /// <summary>
    /// This panel with no padding: its rows reach the panel's edge on every side, and its size is the size they fill.
    /// The rows keep their own text clear of the edge.
    /// </summary>
    /// <returns>The panel, drawn to its edge.</returns>
    public KitPanel WithoutPadding() => new(Content, Width, Height, Scrolls) { Room = Room, Padded = false, OwnKeys = OwnKeys };

    /// <summary>
    /// This panel without the Kit's selection: its plugin moves a selection of its own from <see cref="IKitPanel.OnKey"/>,
    /// where the arrow keys do more than step from one control to the next.
    /// </summary>
    /// <returns>The panel, with its keys left to its plugin.</returns>
    public KitPanel WithOwnKeys() => new(Content, Width, Height, Scrolls) { Room = Room, Padded = Padded, OwnKeys = true };
}
