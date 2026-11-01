using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar windows and removes the app-bar reservation that insets the work area, then puts both
/// back. An auto-hidden taskbar already reserves nothing, so only its window is hidden: removing that app bar and
/// registering it again would make it reserve an edge. This is the only type that calls Win32 on
/// <c>Shell_TrayWnd</c> or <c>Shell_SecondaryTrayWnd</c>.
/// </summary>
internal sealed unsafe partial class Win32ExplorerTaskbar : IExplorerTaskbar
{
    private readonly List<Saved> _saved = [];
    private uint _callback;
    private bool _active;

    public void Hide()
    {
        if (_active)
            return;
        _active = true;
        var primary = Windows("Shell_TrayWnd");
        var secondary = Windows("Shell_SecondaryTrayWnd");
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

    private void PutBack(Saved saved)
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

    private uint Callback()
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

    private static unsafe bool TryPosition(nint hwnd, bool primary, out uint edge, out RECT rect)
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

    private static unsafe uint EdgeOf(nint hwnd, RECT window)
    {
        var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        var monitor = Win32.GetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), ref info)
            ? info.rcMonitor
            : window;
        if (window.Height <= window.Width)
            return Math.Abs(window.Top - monitor.Top) < Math.Abs(monitor.Bottom - window.Bottom) ? Win32.ABE_TOP : Win32.ABE_BOTTOM;
        return Math.Abs(window.Left - monitor.Left) < Math.Abs(monitor.Right - window.Right) ? Win32.ABE_LEFT : Win32.ABE_RIGHT;
    }

    private static unsafe APPBARDATA AppBar(nint hwnd) => new() { cbSize = (uint)sizeof(APPBARDATA), hWnd = hwnd };

    private static List<nint> Windows(string className)
    {
        var found = new List<nint>();
        for (nint hwnd = 0, next; (next = FindWindowExW(0, hwnd, className, null)) != 0; hwnd = next)
            found.Add(next);
        return found;
    }

    /// <summary>False when the handle was destroyed or reused, so restore does not show some other window.</summary>
    private static unsafe bool IsExplorerTaskbar(nint hwnd)
    {
        if (!Win32.IsWindow(hwnd))
            return false;
        var buffer = stackalloc char[64];
        var got = GetClassName(hwnd, buffer, 64);
        return got > 0 && new string(buffer, 0, got) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private sealed class Saved
    {
        public nint Hwnd;
        public bool WasVisible;
        public bool Removed;
        public uint Edge;
        public RECT Rect;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(nint hwnd, char* name, int capacity);
}
