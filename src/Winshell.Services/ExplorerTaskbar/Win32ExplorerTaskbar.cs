using System.Diagnostics;
using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar windows and removes the app-bar reservation that insets the work area, then puts both
/// back. An auto-hidden taskbar already reserves nothing, so only its window is hidden: removing that app bar and
/// registering it again would make it reserve an edge. This is the only type that calls Win32 on
/// <c>Shell_TrayWnd</c> or <c>Shell_SecondaryTrayWnd</c>.
/// </summary>
internal sealed class Win32ExplorerTaskbar : IExplorerTaskbar
{
    private static uint _callback;
    private readonly List<Saved> _saved = [];
    private bool _active;

    /// <summary>Whether Explorer shows a taskbar window now.</summary>
    public static bool Showing() => Windows("Shell_TrayWnd").Concat(Windows("Shell_SecondaryTrayWnd")).Any(Win32.IsWindowVisible);

    /// <summary>
    /// Shows every hidden taskbar window and reserves its edge again, for a process that did not hide them: the shell
    /// that did can end before it puts them back. The edge is read from where the window is now. A window that still
    /// reserves its edge keeps it, since Explorer refuses to register it twice.
    /// </summary>
    public static void RestoreHidden()
    {
        var primary = Windows("Shell_TrayWnd");
        var secondary = Windows("Shell_SecondaryTrayWnd");
        var sample = primary.Count > 0 ? primary[0] : secondary.Count > 0 ? secondary[0] : 0;
        var autohide = sample != 0 && Autohide(sample);
        foreach (var (hwnd, isPrimary) in primary.Select(h => (h, true)).Concat(secondary.Select(h => (h, false))))
        {
            if (Win32.IsWindowVisible(hwnd))
                continue;
            var saved = new Saved { Hwnd = hwnd, WasVisible = true };
            if (!autohide && TryPosition(hwnd, isPrimary, out var edge, out var rect))
            {
                saved.Edge = edge;
                saved.Rect = rect;
                saved.Removed = true;
            }
            PutBack(saved);
        }
    }

    /// <summary>Takes every taskbar window not yet taken, so calling it again picks up one Explorer made since.</summary>
    public void Hide()
    {
        _active = true;
        _saved.RemoveAll(s => !IsExplorerTaskbar(s.Hwnd));
        var primary = Windows("Shell_TrayWnd").Where(NotTaken).ToList();
        var secondary = Windows("Shell_SecondaryTrayWnd").Where(NotTaken).ToList();
        var sample = primary.Count > 0 ? primary[0] : secondary.Count > 0 ? secondary[0] : 0;
        var autohide = sample != 0 && Autohide(sample);
        foreach (var hwnd in primary)
            Take(hwnd, primary: true, autohide);
        foreach (var hwnd in secondary)
            Take(hwnd, primary: false, autohide);
    }

    public void Restore()
    {
        if (!_active)
            return;
        for (var i = _saved.Count - 1; i >= 0; i--)
            PutBack(_saved[i]);
        _saved.Clear();
        _active = false;
    }

    private bool NotTaken(nint hwnd) => _saved.All(s => s.Hwnd != hwnd);

    private void Take(nint hwnd, bool primary, bool autohide)
    {
        if (!Win32.IsWindow(hwnd))
            return;
        var saved = new Saved { Hwnd = hwnd, WasVisible = Win32.IsWindowVisible(hwnd) };
        // Record it before changing anything, so a failure halfway can still put this window back.
        _saved.Add(saved);
        if (!autohide && TryPosition(hwnd, primary, out var edge, out var rect))
        {
            saved.Edge = edge;
            saved.Rect = rect;
            var abd = AppBar(hwnd);
            saved.Removed = Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref abd) != 0;
        }
        Win32.ShowWindow(hwnd, Win32.SW_HIDE);
    }

    private static void PutBack(Saved saved)
    {
        if (!IsExplorerTaskbar(saved.Hwnd))
            return;
        if (saved.WasVisible)
            Win32.ShowWindow(saved.Hwnd, Win32.SW_SHOWNA);
        if (!saved.Removed)
            return;
        var abd = AppBar(saved.Hwnd);
        abd.uCallbackMessage = Callback();
        abd.uEdge = saved.Edge;
        abd.rc = saved.Rect;
        if (Win32.SHAppBarMessage(Win32.ABM_NEW, ref abd) == 0)
            return;
        Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref abd);
        abd.uEdge = saved.Edge;
        Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref abd);
    }

    private static uint Callback()
    {
        if (_callback == 0)
            _callback = Win32.RegisterWindowMessageW("WinshellExplorerTaskbar");
        return _callback;
    }

    private static bool Autohide(nint hwnd)
    {
        var abd = AppBar(hwnd);
        var state = (uint)Win32.SHAppBarMessage(Win32.ABM_GETSTATE, ref abd);
        return (state & Win32.ABS_AUTOHIDE) != 0;
    }

    private static bool TryPosition(nint hwnd, bool primary, out uint edge, out RECT rect)
    {
        if (primary)
        {
            var abd = AppBar(hwnd);
            if (Win32.SHAppBarMessage(Win32.ABM_GETTASKBARPOS, ref abd) != 0 && abd.rc.Width > 0 && abd.rc.Height > 0)
            {
                edge = abd.uEdge;
                rect = abd.rc;
                return true;
            }
        }
        if (Win32.GetWindowRect(hwnd, out rect) && rect.Width > 0 && rect.Height > 0)
        {
            edge = EdgeOf(hwnd, rect);
            return true;
        }
        edge = Win32.ABE_BOTTOM;
        rect = default;
        return false;
    }

    private static uint EdgeOf(nint hwnd, RECT window)
    {
        var monitor = Monitors.TryRead(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), out var info)
            ? info.Bounds
            : window;
        if (window.Height <= window.Width)
            return Math.Abs(window.Top - monitor.Top) < Math.Abs(monitor.Bottom - window.Bottom) ? Win32.ABE_TOP : Win32.ABE_BOTTOM;
        return Math.Abs(window.Left - monitor.Left) < Math.Abs(monitor.Right - window.Right) ? Win32.ABE_LEFT : Win32.ABE_RIGHT;
    }

    private static APPBARDATA AppBar(nint hwnd) => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    /// <summary>Explorer's windows of that class. A shell's tray host makes a <c>Shell_TrayWnd</c> of its own.</summary>
    private static List<nint> Windows(string className)
    {
        var found = new List<nint>();
        for (nint hwnd = 0, next; (next = Win32.FindWindowExW(0, hwnd, className, null)) != 0; hwnd = next)
        {
            Win32.GetWindowThreadProcessId(next, out var pid);
            if (pid != Environment.ProcessId && IsExplorer((int)pid))
                found.Add(next);
        }
        return found;
    }

    private static bool IsExplorer(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // It has exited.
            return false;
        }
    }

    /// <summary>False when the handle was destroyed or reused, so restore does not show some other window.</summary>
    private static bool IsExplorerTaskbar(nint hwnd) =>
        Win32.IsWindow(hwnd) && TopLevelWindows.ClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private sealed class Saved
    {
        public nint Hwnd;
        public bool WasVisible;
        public bool Removed;
        public uint Edge;
        public RECT Rect;
    }
}
