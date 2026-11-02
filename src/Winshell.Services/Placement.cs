namespace Winshell;

/// <summary>How a panel treats the edges other bars reserve.</summary>
public enum PanelExclusionMode
{
    /// <summary>Stays inside the work area, and reserves <c>ExclusiveZone</c> if that is set.</summary>
    Normal = 0,

    /// <summary>Covers the whole screen, over the taskbar and other bars.</summary>
    Ignore = 1,
}

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
    PanelExclusionMode ExclusionMode,
    double ExclusiveZone,
    bool AboveWindows,
    bool Focusable);

/// <summary>Where a popup sits, in screen coordinates.</summary>
internal sealed record PopupPlacement(ScreenInfo? Screen, QRect Rect, bool GrabFocus);

[Flags]
public enum Edges
{
    None = 0,
    Top = 1,
    Left = 2,
    Right = 4,
    Bottom = 8,
}
