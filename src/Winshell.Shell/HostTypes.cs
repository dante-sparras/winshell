namespace Winshell;

internal enum WindowKind
{
    /// <summary>A <see cref="PanelWindow"/>: no frame, attached to screen edges, may reserve space.</summary>
    Panel,

    /// <summary>A <see cref="FloatingWindow"/>: a normal window with a title bar.</summary>
    Floating,

    /// <summary>A <see cref="PopupWindow"/>: no frame, placed next to another window.</summary>
    Popup,
}

/// <summary>What a window shows behind its content: nothing, or a Windows 11 material.</summary>
public enum WindowBackdrop
{
    None,
    Mica,
    Acrylic,
    MicaAlt,
}

public enum WindowCorners
{
    Default,
    Square,
    Round,
    RoundSmall,
}

/// <summary>A normal window's settings.</summary>
internal sealed record FloatingPlacement(
    ScreenInfo? Screen,
    string Title,
    double Width,
    double Height,
    QSize MinimumSize,
    QSize MaximumSize,
    bool Maximized,
    bool Minimized,
    bool Fullscreen = false,
    HostWindow? Owner = null,
    bool StateRequested = false);

/// <summary>What a native window tells the shell window it shows.</summary>
internal interface IHostClient
{
    /// <summary>The window's size changed, in logical pixels, or its scale did.</summary>
    void OnResized(double width, double height, double scale);

    void OnMoved(double x, double y);

    void OnClosed();

    /// <summary>The native window became the foreground window, or stopped being it.</summary>
    void OnActiveChanged(bool active);

    void OnScreenChanged(string screenId);

    /// <summary>A click elsewhere, or Esc, dismissed a window that grabs focus.</summary>
    void OnDismissed();

    void OnStateChanged(bool maximized, bool minimized);

    void OnFullscreenChanged(bool fullscreen);

    /// <summary>Windows destroyed the native window, as when its owner closes.</summary>
    void OnBackingDestroyed();

    /// <summary>The native window threw while handling a message, and stopped handling them. Called on the loop, once.</summary>
    void OnFailed(Exception error);
}
