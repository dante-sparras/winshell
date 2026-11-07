using Winshell.Native;

namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Puts back Explorer's taskbar for a process that did not hide it, such as the supervisor after its shell crashed.
/// It keeps nothing: each call reads the taskbar windows as they are.
/// </summary>
internal static class ExplorerTaskbarRecovery
{
    /// <summary>Whether a taskbar window of Explorer's is hidden. This does not say which process hid it.</summary>
    internal static bool AnyHidden()
    {
        return ExplorerTaskbarBackend.Find().Windows.Any(taskbar => !Win32.IsWindowVisible(taskbar.Hwnd));
    }

    /// <summary>Shows every hidden taskbar window and reserves its edge again. A window that still reserves its edge keeps it.</summary>
    internal static void RestoreHidden()
    {
        var (windows, autohide) = ExplorerTaskbarBackend.Find();
        foreach (var (hwnd, primary) in windows)
        {
            if (Win32.IsWindowVisible(hwnd))
                continue;
            var saved = ExplorerTaskbarBackend.Saved.Of(hwnd, primary, autohide, true);
            saved.Removed = saved.Reserves;
            ExplorerTaskbarBackend.PutBack(saved);
        }
    }
}
