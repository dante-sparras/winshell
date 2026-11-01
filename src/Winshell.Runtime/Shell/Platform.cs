using SkiaSharp;

namespace Winshell.Runtime.Shell;

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
    bool Fullscreen = false,
    ShellWindow? Owner = null,
    bool StateRequested = false);

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

    /// <summary>Multiplies the opacity of the window content, from 0 to 1. 1 leaves a new window unchanged.</summary>
    void SetOpacity(double opacity);

    /// <summary>
    /// Clips drawing to this rectangle in logical window coordinates, or null to draw the whole window.
    /// It does not change <see cref="SetInputRegion"/>.
    /// </summary>
    void SetVisibleMask(QRect? bounds);

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

    /// <summary>The native window became the foreground window, or stopped being it. The default does nothing.</summary>
    void OnActiveChanged(bool active) { }

    void OnScreenChanged(string screenId);

    Quick.Scene Scene { get; }

    /// <summary>The window lost focus or was dismissed by a click elsewhere, as a grabbing popup is.</summary>
    void OnDismissed();

    void OnStateChanged(bool maximized, bool minimized);

    /// <summary>The window started or stopped covering its monitor without a frame. The default does nothing.</summary>
    void OnFullscreenChanged(bool fullscreen) { }

    /// <summary>Windows destroyed the native window, as when its owner closes. The default does nothing.</summary>
    void OnBackingDestroyed() { }

    /// <summary>
    /// The native window threw while drawing or handling a message, and stopped doing either. Called on the loop,
    /// once per backend; the window disposes that backend and makes another.
    /// </summary>
    void OnFailed(Exception error);

    /// <summary>
    /// The graphics device could not draw a frame, and the frame was skipped. Making the window again would not help,
    /// since every window draws with the same device, so it stays; this is for the crash report. Called on the loop.
    /// </summary>
    void OnDrawFailed(Exception error);

    /// <summary>
    /// The swap chain or composition surface could not be created or resized, or the device was removed, reset, or
    /// out of memory. The window emits <c>resourcesLost</c> and closes. It does not open again. Called on the loop.
    /// </summary>
    void OnResourcesLost(Exception error);

    /// <summary>
    /// Whether the configuration asked for an opaque surface. Null means it did not: the swap chain stays
    /// premultiplied, which is the chain every window has used so far.
    /// </summary>
    bool? SurfaceOpaque => null;
}

/// <summary>
/// The window cannot create or resize the surface it draws with, or the graphics device was removed, reset, or out
/// of memory. The shell closes that window and does not try again.
/// </summary>
public sealed class SurfaceResourcesException : Exception
{
    public SurfaceResourcesException(string message)
        : base(message)
    {
    }

    public SurfaceResourcesException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The platform: screens and windows.</summary>
public interface IShellPlatform
{
    /// <summary>Whether the system is holding notifications back, as in a presentation. The default is no.</summary>
    bool NotificationsAreQuiet => false;

    IReadOnlyList<ScreenInfo> Screens { get; }

    event Action? ScreensChanged;

    IWindowBackend CreateWindow(WindowKind kind, IWindowClient client);
}

