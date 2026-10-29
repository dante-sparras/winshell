using SkiaSharp;

namespace Winshell.Runtime.Shell;

/// <summary>
/// A monitor, as the platform reports it. Coordinates are logical pixels on the virtual desktop: a monitor keeps its
/// physical top-left corner and its size is divided by its scale, so monitors of mixed DPI never overlap.
/// </summary>
public sealed record ScreenInfo(
    string Id,
    string Name,
    string Model,
    string Manufacturer,
    string SerialNumber,
    QRect Geometry,
    QRect WorkArea,
    double DevicePixelRatio,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    int RefreshRate,
    bool IsPrimary);

/// <summary>How a window is put on the screen.</summary>
public enum WindowKind
{
    /// <summary>A PanelWindow: no frame, attached to screen edges, may reserve space.</summary>
    Panel,

    /// <summary>A FloatingWindow: a normal window with a title bar.</summary>
    Floating,

    /// <summary>A PopupWindow: no frame, placed next to its parent window.</summary>
    Popup,
}

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

/// <summary>A normal window's settings.</summary>
public sealed record FloatingPlacement(
    ScreenInfo? Screen,
    string Title,
    double Width,
    double Height,
    QSize MinimumSize,
    QSize MaximumSize,
    bool Maximized,
    bool Minimized,
    bool Fullscreen);

/// <summary>What the window shows behind its content: nothing, or a Windows 11 material.</summary>
public enum WindowBackdrop
{
    None,
    Mica,
    Acrylic,
    MicaAlt,
}

/// <summary>A window's corner style on Windows 11.</summary>
public enum WindowCorners
{
    Default,
    Square,
    Round,
    RoundSmall,
}

/// <summary>The native window behind a ShellWindow. The platform implements it.</summary>
public interface IWindowBackend : IDisposable
{
    void SetPanel(PanelPlacement placement);

    void SetPopup(PopupPlacement placement);

    void SetFloating(FloatingPlacement placement);

    void SetVisible(bool visible);

    /// <summary>The areas that take input, in logical window coordinates; null means everywhere.</summary>
    void SetInputRegion(IReadOnlyList<QRect>? rects);

    void SetCursor(int shape);

    void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners, bool darkFrame);

    /// <summary>Asks for a frame to be drawn soon.</summary>
    void RequestFrame();

    void Raise();

    void StartSystemMove();

    void StartSystemResize(Edges edges);

    /// <summary>Takes or gives up keyboard focus for the window.</summary>
    void SetKeyboardFocus(bool focus);

    bool IsVisible { get; }

    /// <summary>The window's origin on the virtual desktop, in logical pixels.</summary>
    QPoint Origin { get; }

    /// <summary>A point in logical window coordinates, in physical pixels on the virtual desktop.</summary>
    (int X, int Y) MapToScreen(double x, double y);
}

/// <summary>What a window backend tells the window it shows.</summary>
public interface IWindowClient
{
    /// <summary>The window's size changed, in logical pixels, or its pixel ratio did.</summary>
    void OnResized(double width, double height, double devicePixelRatio);

    void OnMoved(double x, double y);

    /// <summary>Draws the window. The canvas is in physical pixels and already cleared.</summary>
    void OnPaint(SKCanvas canvas);

    void OnClosed();

    void OnScreenChanged(string screenId);

    Quick.Scene Scene { get; }

    /// <summary>The window lost focus or was dismissed by a click elsewhere, as a grabbing popup is.</summary>
    void OnDismissed();

    void OnStateChanged(bool maximized, bool minimized, bool fullscreen);

    /// <summary>
    /// The native window threw while drawing or handling a message, and stopped doing either. Called on the loop,
    /// once per backend; the window disposes that backend and makes another.
    /// </summary>
    void OnFailed(Exception error);
}

/// <summary>The platform: screens and windows.</summary>
public interface IShellPlatform
{
    IReadOnlyList<ScreenInfo> Screens { get; }

    event Action? ScreensChanged;

    IWindowBackend CreateWindow(WindowKind kind, IWindowClient client);
}

[Flags]
public enum Edges
{
    None = 0,
    Top = 1,
    Left = 2,
    Right = 4,
    Bottom = 8,
}
