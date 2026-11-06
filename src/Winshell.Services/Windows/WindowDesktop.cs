using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>
/// Which virtual desktop a window is on, and moving it to another, through the supported
/// <c>IVirtualDesktopManager</c>. Unlike the desktop list in <see cref="VirtualDesktopBackend"/>, it works on every build.
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
        try
        {
            var manager = Manager();
            if (manager is null)
                return "";
            if (manager.DesktopOf(hwnd, out var id) < 0 || id == Guid.Empty)
                return "";
            return id.ToString("D");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _manager = null;
            return "";
        }
    }

    /// <summary>Moves the window to that desktop. False when Windows refuses or the manager is unavailable.</summary>
    public static bool Move(nint hwnd, Guid desktop)
    {
        if (hwnd == 0 || desktop == Guid.Empty)
            return false;
        try
        {
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
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _manager = null;
            Log.Default.Warn("desktops", $"MoveWindowToDesktop failed ({ex.GetType().Name}).");
            return false;
        }
    }

    private static WindowDesktopManager? Manager()
    {
        if (_manager is not null)
            return _manager;
        _manager = WindowDesktopManager.Create(out var hr);
        if (hr < 0 && !_loggedUnavailable)
        {
            _loggedUnavailable = true;
            Log.Default.Warn("desktops", $"IVirtualDesktopManager is unavailable (0x{hr:X8}).");
        }

        return _manager;
    }
}
