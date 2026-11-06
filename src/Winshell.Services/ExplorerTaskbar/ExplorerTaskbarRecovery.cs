using Winshell.Native;

namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Puts Explorer's taskbar back for a process that did not hide it: the shell that did can end before it puts it back,
/// and its supervisor can't know what that shell had taken.
/// </summary>
internal static class ExplorerTaskbarRecovery
{
    /// <summary>Whether a taskbar window of Explorer's is hidden. With no taskbar window at all, none is.</summary>
    public static bool AnyHidden()
    {
        return Win32ExplorerTaskbar.Windows("Shell_TrayWnd")
            .Concat(Win32ExplorerTaskbar.Windows("Shell_SecondaryTrayWnd")).Any(hwnd => !Win32.IsWindowVisible(hwnd));
    }

    /// <summary>
    /// Shows every hidden taskbar window and reserves its edge again, read from where the window is now. A window that
    /// still reserves its edge keeps it, since Explorer refuses to register it twice.
    /// </summary>
    public static void Restore()
    {
        var primary = Win32ExplorerTaskbar.Windows("Shell_TrayWnd");
        var secondary = Win32ExplorerTaskbar.Windows("Shell_SecondaryTrayWnd");
        var sample = primary.Count > 0 ? primary[0] : secondary.Count > 0 ? secondary[0] : 0;
        var autohide = sample != 0 && Win32ExplorerTaskbar.Autohide(sample);
        foreach (var (hwnd, isPrimary) in primary.Select(h => (h, true)).Concat(secondary.Select(h => (h, false))))
        {
            if (Win32.IsWindowVisible(hwnd))
                continue;
            var saved = new Win32ExplorerTaskbar.Saved { Hwnd = hwnd, WasVisible = true };
            if (!autohide && Win32ExplorerTaskbar.TryPosition(hwnd, isPrimary, out var edge, out var rect))
            {
                saved.Edge = edge;
                saved.Rect = rect;
                saved.Removed = true;
            }

            Win32ExplorerTaskbar.PutBack(saved);
        }
    }
}
