using Winshell.Native;

namespace Winshell.Windows;

/// <summary>
/// Which virtual desktop a window is on, and moving it to another, through the supported
/// <c>IVirtualDesktopManager</c>. Unlike the desktop list in <see cref="VirtualDesktopBroker"/>, it works on every build.
/// </summary>
internal static class WindowDesktop
{
    private static WindowDesktopManager? _manager;
    private static bool _loggedUnavailable;

    /// <summary>The id of the desktop the window is on, or empty when Windows does not say.</summary>
    public static string IdOf(nint hwnd)
    {
        if (hwnd == 0)
            return "";
        var manager = Manager();
        if (manager is null || manager.DesktopOf(hwnd, out var id) < 0 || id == Guid.Empty)
            return "";
        return id.ToString("D");
    }

    /// <summary>Moves the window to that desktop. False when Windows refuses or the manager is unavailable.</summary>
    public static bool Move(nint hwnd, Guid desktop)
    {
        if (hwnd == 0 || desktop == Guid.Empty)
            return false;
        var manager = Manager();
        if (manager is null)
            return false;
        var hr = manager.Move(hwnd, desktop);
        if (hr < 0)
        {
            Log.Default.Warn("desktops", $"MoveWindowToDesktop failed (0x{hr:X8}).");
            return false;
        }
        return true;
    }

    private static WindowDesktopManager? Manager()
    {
        if (_manager is not null)
            return _manager;
        _manager = WindowDesktopManager.Create(out var hr);
        if (_manager is null && !_loggedUnavailable)
        {
            _loggedUnavailable = true;
            Log.Default.Warn("desktops", $"IVirtualDesktopManager is unavailable (0x{hr:X8}).");
        }
        return _manager;
    }
}
