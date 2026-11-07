namespace Winshell;

internal enum WindowKind
{
    /// <summary>A <see cref="PanelWindow"/>: no frame, attached to screen edges, may reserve space.</summary>
    Panel,

    /// <summary>A <see cref="FloatingWindow"/>: a normal window with a title bar.</summary>
    Floating,

    /// <summary>A <see cref="PopupWindow"/>: no frame, placed next to another window.</summary>
    Popup
}

/// <summary>Whether windows are light or dark. <see cref="System"/> follows Windows.</summary>
public enum ColorScheme
{
    /// <summary>Follows the light or dark mode set in Windows.</summary>
    System,

    /// <summary>Light, whatever Windows is set to.</summary>
    Light,

    /// <summary>Dark, whatever Windows is set to.</summary>
    Dark
}

/// <summary>What a window shows behind its content: nothing, or a Windows 11 material.</summary>
public enum WindowBackdrop
{
    /// <summary>No material: what the content does not cover is transparent.</summary>
    None,

    /// <summary>Mica, tinted by the desktop background, as Windows Settings uses.</summary>
    Mica,

    /// <summary>Acrylic, a blur of what is behind the window, as Windows' flyouts use.</summary>
    Acrylic,

    /// <summary>Mica Alt, a stronger tint of Mica, as tabbed title bars use.</summary>
    MicaAlt
}

/// <summary>How a window's corners are drawn.</summary>
public enum WindowCorners
{
    /// <summary>
    /// The kind's own: a floating window's are Windows' choice, a popup with a backdrop is rounded, and the rest are
    /// square.
    /// </summary>
    Default,

    /// <summary>Square corners.</summary>
    Square,

    /// <summary>Rounded corners.</summary>
    Round,

    /// <summary>Corners with a smaller rounding.</summary>
    RoundSmall
}

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
