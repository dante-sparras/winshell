using Winshell.Native;

namespace Winshell;

/// <summary>
/// How one kind of window sits on the desktop: a panel's <see cref="PanelEdge"/>, a <see cref="PopupPlacer"/>, or a
/// floating window's <see cref="FloatingFrame"/>. <see cref="HostWindow"/> makes the one its kind needs, and the
/// window's other parts tell it what happens to the window.
/// </summary>
internal interface IWindowPlacer
{
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
