namespace Winshell;

/// <summary>Where and how a panel sits on its screen.</summary>
public sealed record PanelPlacement(
    ScreenInfo? Screen,
    Edges Anchors,
    double MarginLeft,
    double MarginTop,
    double MarginRight,
    double MarginBottom,
    double Width,
    double Height,
    int ExclusionMode,
    double ExclusiveZone,
    bool AboveWindows,
    bool Focusable,
    string Namespace);

/// <summary>Where a popup sits, in screen coordinates.</summary>
public sealed record PopupPlacement(ScreenInfo? Screen, QRect Rect, bool GrabFocus);

[Flags]
public enum Edges
{
    None = 0,
    Top = 1,
    Left = 2,
    Right = 4,
    Bottom = 8,
}
