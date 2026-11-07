using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;

namespace Winshell;

/// <summary>Carries out <see cref="WindowFocus"/>'s answers on a native window and the shell's outside clicks.</summary>
internal sealed class HostFocusEffects(HostWindow host, ShellPlatform platform) : IFocusEffects
{
    public void AllowActivation(bool allow)
    {
        host.Placement.SetExStyle((uint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE, !allow);
    }

    public void Show(bool activate)
    {
        var show = activate ? (int)SHOW_WINDOW_CMD.SW_SHOW : (int)SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE;
        Win32.ShowWindow(host.Handle, show);
        // A process's first SW_SHOW takes the state its launcher asked for, so a hidden start would hide the window.
        if (!Win32.IsWindowVisible(host.Handle))
            Win32.ShowWindow(host.Handle, show);
    }

    public void TakeForeground()
    {
        Win32.SetForegroundWindow(host.Handle);
    }

    public void TakeKeyboard()
    {
        // A Windows-key chord arrives through a low-level hook, which does not grant the right to take the keyboard.
        Focus.Take(host.Handle);
        host.Presentation.FocusContent();
    }

    public void GrabOutsideClicks(bool on)
    {
        platform.OutsideClicks.Grab(host, on);
    }
}
