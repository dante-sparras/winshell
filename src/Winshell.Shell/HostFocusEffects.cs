using Winshell.Native;

namespace Winshell;

/// <summary>Carries out <see cref="WindowFocus"/>'s answers on a native window and the shell's outside clicks.</summary>
internal sealed class HostFocusEffects(HostWindow host, ShellPlatform platform) : IFocusEffects
{
    public void AllowActivation(bool allow) => host.Placement.SetExStyle(Win32.WS_EX_NOACTIVATE, !allow);

    public void Show(bool activate)
    {
        var show = activate ? Win32.SW_SHOW : Win32.SW_SHOWNOACTIVATE;
        Win32.ShowWindow(host.Handle, show);
        // A process's first SW_SHOW takes the state its launcher asked for, so a hidden start would hide the window.
        if (!Win32.IsWindowVisible(host.Handle))
            Win32.ShowWindow(host.Handle, show);
    }

    public void TakeForeground() => Win32.SetForegroundWindow(host.Handle);

    public void TakeKeyboard()
    {
        // A Windows-key chord arrives through a low-level hook, which does not grant foreground rights, so
        // SetForegroundWindow can fail. Join the foreground thread long enough to take the keyboard, then leave it.
        if (Win32.GetForegroundWindow() != host.Handle && !Win32.SetForegroundWindow(host.Handle))
            JoinForegroundAndTakeIt();
        host.Presentation.FocusContent();
    }

    public void GrabOutsideClicks(bool on) => platform.OutsideClicks.Grab(host, on);

    private void JoinForegroundAndTakeIt()
    {
        var foreground = Win32.GetForegroundWindow();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        var current = Win32.GetCurrentThreadId();
        if (other == 0 || other == current || !Win32.AttachThreadInput(current, other, true))
            return;
        try
        {
            Win32.SetForegroundWindow(host.Handle);
        }
        finally
        {
            Win32.AttachThreadInput(current, other, false);
        }
    }
}
