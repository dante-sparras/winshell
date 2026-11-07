using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// How one kind of window sits on the desktop: a panel's <see cref="PanelEdge"/>, a <see cref="PopupPlacer"/>, or a
/// floating window's <see cref="FloatingFrame"/>. Each kind of <see cref="ShellWindow"/> makes its own for each native
/// window it gets, and the window's other parts tell it what happens to the window.
/// </summary>
internal interface IWindowPlacer
{
    /// <summary>
    /// The styles the native window is made with, apart from the ones <see cref="WindowFocus"/> adds: frameless, above
    /// other windows, and out of the taskbar.
    /// </summary>
    (uint Style, uint ExStyle) Styles => ((uint)WINDOW_STYLE.WS_POPUP,
        (uint)(WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
               WINDOW_EX_STYLE.WS_EX_TOPMOST));

    /// <summary>The window is about to show.</summary>
    void Show()
    {
    }

    /// <summary>The window hid.</summary>
    void Hide()
    {
    }

    /// <summary>The window is going away. Nothing posted earlier places it again.</summary>
    void Release()
    {
    }

    /// <summary>
    /// The window's monitor changed scale, or the window went to another monitor. <paramref name="placing"/> is a
    /// change that the window's own move caused.
    /// </summary>
    void OnDpiChanged(RECT suggested, bool placing);

    /// <summary>The work area may have moved.</summary>
    void OnWorkAreaChanged()
    {
    }

    /// <summary>A message the window's own handling left to Windows. Returns null to leave it to Windows still.</summary>
    nint? OnMessage(WindowMessage message)
    {
        return null;
    }
}
