namespace Winshell.Core;

/// <summary>How the pointer hit a widget.</summary>
public enum WidgetGesture
{
    /// <summary>Left click.</summary>
    Left,

    /// <summary>Right click.</summary>
    Right,

    /// <summary>Middle click.</summary>
    Middle,

    /// <summary>Scroll up.</summary>
    ScrollUp,

    /// <summary>Scroll down.</summary>
    ScrollDown,

    /// <summary>The pointer entered the widget.</summary>
    Enter,

    /// <summary>The pointer left the widget.</summary>
    Leave,

    /// <summary>A tray icon was picked up, so the overflow can open under it.</summary>
    Drag,
}

/// <summary>One workspace chip.</summary>
public sealed record BarChip
{
    /// <summary>The workspace number.</summary>
    public string Text { get; init; } = "";

    /// <summary>True for a workspace in front.</summary>
    public bool Accent { get; init; }

    /// <summary>
    /// The one monitor whose Bar draws <see cref="Accent"/>, as <c>winoma desktop</c> numbers the monitors: the monitor a workspace is in front on.
    /// Null draws it on every Bar.
    /// </summary>
    public int? Monitor { get; init; }

    /// <summary>Value passed back with a click. Workspace chips use the number.</summary>
    public int Tag { get; init; }

    /// <summary>Who a tray icon is across restarts. Empty on a workspace chip. A drag carries this into the overflow.</summary>
    public string Key { get; init; } = "";

    /// <summary>PNG bytes for each window icon.</summary>
    public IReadOnlyList<byte[]> Icons { get; init; } = [];
}

/// <summary>
/// What the bar draws for one plugin. Publishing a face that is the <see cref="Same"/> as the last one costs nothing,
/// and a plugin that hands back the same list objects while their content is the same is compared without a walk.
/// </summary>
public sealed record BarFace
{
    /// <summary>The text drawn. Empty when the widget is icon-only.</summary>
    public string Text { get; init; } = "";

    /// <summary>Named icon, such as <c>mic</c> or <c>power</c>.</summary>
    public string Glyph { get; init; } = "";

    /// <summary>
    /// PNG bytes drawn at the left of the text, at the bar's icon size. Null or empty draws no picture.
    /// When this is set, the font glyph is not drawn. It is compared by the array it is, the same way a chip's icons are,
    /// so the same bytes in a new array count as a different picture.
    /// </summary>
    public byte[]? Picture { get; init; }

    /// <summary>What a screen reader announces. It is never drawn. Empty keeps the plugin's name.</summary>
    public string Label { get; init; } = "";

    /// <summary>Hover text. Empty shows none.</summary>
    public string Tooltip { get; init; } = "";

    /// <summary>
    /// The width the text is drawn in, in device-independent pixels. Zero, the default, sizes the item to the text.
    /// A positive width keeps the text that wide on a horizontal bar, and a line that does not fit scrolls inside it.
    /// A vertical bar sizes the item to the text.
    /// </summary>
    public double TextWidth { get; init; }

    /// <summary>False hides the widget.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>True uses the accent color.</summary>
    public bool Accent { get; init; }

    /// <summary>True draws the glyph and the text in the muted color, for an item that is there and idle. The accent wins over it.</summary>
    public bool Quiet { get; init; }

    /// <summary>A tone for the glyph and the text, such as amber for an item that cannot run. Normal leaves the color to <see cref="Accent"/> and <see cref="Quiet"/>.</summary>
    public PanelTone Tone { get; init; }

    /// <summary>Workspace chips. When this is non-empty the single glyph is not drawn.</summary>
    public IReadOnlyList<BarChip> Chips { get; init; } = [];

    /// <summary>True for the system tray: a chevron, an overflow panel, and pinned icons.</summary>
    public bool Tray { get; init; }

    /// <summary>True when the tray runs along a vertical bar.</summary>
    public bool TrayVertical { get; init; }

    /// <summary>Unpinned icons. They sit in the panel the chevron opens.</summary>
    public IReadOnlyList<BarChip> TrayDrawer { get; init; } = [];

    /// <summary>True while the overflow panel is open.</summary>
    public bool TrayExpanded { get; init; }

    /// <summary>Icons that stay on the bar.</summary>
    public IReadOnlyList<BarChip> TrayPinned { get; init; } = [];

    /// <summary>Figures drawn side by side, each beside its icon or label, such as the system usage item. When this is non-empty the text and glyph are not drawn.</summary>
    public IReadOnlyList<BarReadout> Readouts { get; init; } = [];

    /// <summary>
    /// Brand marks with a percent and a meter, such as AI subscription limits. When this is non-empty the text and glyph are not drawn.
    /// A right click on one sends its index as the gesture tag.
    /// </summary>
    public IReadOnlyList<BarMeter> Meters { get; init; } = [];

    /// <summary>
    /// True when <paramref name="other"/> draws what this face draws. Lists are compared item by item, and <see cref="Picture"/> or a chip icon by the array it is,
    /// so two pictures with equal bytes in different arrays do not count as the same.
    /// </summary>
    /// <param name="other">The face published before. Null is never the same.</param>
    /// <returns>True when publishing this face after <paramref name="other"/> would change nothing.</returns>
    public bool Same(BarFace? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && Visible == other.Visible
            && Accent == other.Accent
            && Quiet == other.Quiet
            && Tone == other.Tone
            && Tray == other.Tray
            && TrayVertical == other.TrayVertical
            && TrayExpanded == other.TrayExpanded
            && TextWidth == other.TextWidth
            && string.Equals(Text, other.Text, StringComparison.Ordinal)
            && string.Equals(Glyph, other.Glyph, StringComparison.Ordinal)
            && ReferenceEquals(Picture, other.Picture)
            && string.Equals(Label, other.Label, StringComparison.Ordinal)
            && string.Equals(Tooltip, other.Tooltip, StringComparison.Ordinal)
            && SameItems(Readouts, other.Readouts)
            && SameItems(Meters, other.Meters)
            && SameChips(Chips, other.Chips)
            && SameChips(TrayDrawer, other.TrayDrawer)
            && SameChips(TrayPinned, other.TrayPinned);
    }

    private static bool SameItems<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : class, IEquatable<T>
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]) && !left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameChips(IReadOnlyList<BarChip> left, IReadOnlyList<BarChip> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var (a, b) = (left[i], right[i]);
            if (ReferenceEquals(a, b))
            {
                continue;
            }

            if (a.Tag != b.Tag
                || a.Accent != b.Accent
                || !string.Equals(a.Text, b.Text, StringComparison.Ordinal)
                || !string.Equals(a.Key, b.Key, StringComparison.Ordinal)
                || a.Icons.Count != b.Icons.Count)
            {
                return false;
            }

            for (var icon = 0; icon < a.Icons.Count; icon++)
            {
                if (!ReferenceEquals(a.Icons[icon], b.Icons[icon]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>One provider on the bar: its mark, and as much of its usage as <see cref="Shows"/> asks for.</summary>
/// <param name="Mark">The provider whose mark is drawn, such as <c>claude</c>.</param>
/// <param name="Figure">The percent or amount beside the mark.</param>
/// <param name="Fill">Meter fill from 0 to 1.</param>
/// <param name="Tick">A second value from 0 to 1 drawn as a tick on the meter, such as the weekly use beside the session. Negative draws none.</param>
/// <param name="Tone">The meter's color, and the figure's when it is not <see cref="PanelTone.Normal"/>.</param>
/// <param name="Shows">Mark only, mark and figure, or mark, meter, and figure.</param>
public sealed record BarMeter(string Mark, string Figure, double Fill, double Tick, PanelTone Tone, BarShows Shows);
