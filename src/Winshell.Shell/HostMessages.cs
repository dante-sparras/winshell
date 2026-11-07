using Winshell.Native;

namespace Winshell;

/// <summary>What a native window does with each message Windows sends it.</summary>
internal sealed class HostMessages(HostWindow host, ShellPlatform platform, Action dismissMenu)
{
    /// <summary>A panel or popup press ends another app's native menu before its control handles the click.</summary>
    internal void Press()
    {
        if (host.Kind is WindowKind.Panel or WindowKind.Popup)
            dismissMenu();
    }

    /// <summary>The window's own handling first. What it leaves to Windows goes to the kind's placer.</summary>
    public nint? OnMessage(WindowMessage message)
    {
        return OnWindowMessage(message) ?? host.Placer.OnMessage(message);
    }

    private nint? OnWindowMessage(WindowMessage message)
    {
        var placement = host.Placement;
        var (msg, wParam) = (message.Id, message.WParam);
        switch (msg)
        {
            case Win32.WM_SIZE:
            case Win32.WM_MOVE:
                if (host.Handle != 0)
                    placement.UpdateSize();
                return null;
            case Win32.WM_DPICHANGED:
            {
                // A size we apply from this message can send it again with the monitor's old scale. Keep the one that started it.
                if (!placement.Placing)
                    placement.Scale = DesktopSpace.Scale((uint)Win32.HighWord(wParam));
                host.Placer.OnDpiChanged(message.SuggestedRect, placement.Placing);
                placement.UpdateSize();
                platform.ScreensMayHaveChanged();
                return 0;
            }
            case Win32.WM_MOUSEACTIVATE:
                // A click here does not close a menu another app opened without the foreground, as tray menus
                // often are. Close it before the mouse message reaches the XAML island.
                Press();
                return host.Focus.ActivatesOnClick ? null : Win32.MA_NOACTIVATE;
            case Win32.WM_ACTIVATE:
                // A grabbing popup closes on a click outside it, which the mouse hook sees; losing activation
                // alone does not close it, since Windows can take activation away at any time.
                host.Client.OnActiveChanged(Win32.LowWord(wParam) != 0);
                return null;
            case Win32.WM_SETFOCUS:
                host.Presentation.FocusContent();
                return null;
            case Win32.WM_CLOSE:
                host.Client.OnClosed();
                placement.SetVisible(false);
                return 0;
            case Win32.WM_DESTROY:
                // The owner is closing, so Windows destroys this window. Drop the handle before it dangles.
                host.NoteExternalDestroy();
                return null;
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_DISPLAYCHANGE or Win32.WM_SETTINGCHANGE or Win32.WM_DWMCOLORIZATIONCOLORCHANGED
                or Win32.WM_SYSCOLORCHANGE or Win32.WM_THEMECHANGED:
                platform.SystemChanged(msg, wParam);
                return null;
            default:
                return null;
        }
    }
}
