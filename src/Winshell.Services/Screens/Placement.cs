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

[Flags]
public enum Edges
{
    None = 0,
    Top = 1,
    Left = 2,
    Right = 4,
    Bottom = 8,
}
