using Windows.Foundation;

namespace Winshell;

/// <summary>Where and how a panel sits on its screen.</summary>
internal sealed record PanelPlacement(
    ScreenInfo? Screen,
    Edges Anchors,
    double MarginLeft,
    double MarginTop,
    double MarginRight,
    double MarginBottom,
    double Width,
    double Height,
    bool CoversScreen,
    double ReservedSize,
    bool AboveWindows,
    bool Focusable);

/// <summary>Where a popup sits, in screen coordinates.</summary>
internal sealed record PopupPlacement(ScreenInfo? Screen, Rect Rect, bool GrabFocus);

/// <summary>The edges of a screen or a window, such as the ones a panel is anchored to. These values combine.</summary>
[Flags]
public enum Edges
{
    /// <summary>No edge.</summary>
    None = 0,
    /// <summary>The top edge.</summary>
    Top = 1,
    /// <summary>The left edge.</summary>
    Left = 2,
    /// <summary>The right edge.</summary>
    Right = 4,
    /// <summary>The bottom edge.</summary>
    Bottom = 8,
}
