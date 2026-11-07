using System.Diagnostics;
using Winshell.Native;
using Winshell.Tray;

namespace Winshell.ExplorerTaskbar;

/// <summary>
/// Hides Explorer's taskbar windows and removes the app-bar reservation that insets the work area, then puts both
/// back. An auto-hidden taskbar already reserves nothing, so only its window is hidden: removing that app bar and
/// registering it again would make it reserve an edge.
/// </summary>
internal sealed class ExplorerTaskbarBackend : IExplorerTaskbarBackend
{
    private static uint _callback;
    private readonly object _gate = new();
    private readonly List<Saved> _saved = [];
    private bool _active;
    private bool _disposed;
    private IDisposable? _watch;

    /// <summary>Starts hearing Explorer make taskbar windows, on <paramref name="loop"/>'s thread.</summary>
    internal ExplorerTaskbarBackend(IEventLoop loop, Log log)
    {
        // The watch gets its messages on the thread that makes it.
        loop.Post(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _watch = TaskbarWindows.Watch(() => TaskbarMayHaveChanged?.Invoke(),
                    e => log.Warn("explorer-taskbar", e.Message));
                if (_watch is null)
                    log.Warn("explorer-taskbar",
                        "Could not watch for a new Explorer taskbar; one Explorer makes stays shown");
            }
        });
    }

    public event Action? TaskbarMayHaveChanged;

    /// <summary>Takes every taskbar window not yet taken, so calling it again picks up one Explorer made since.</summary>
    public void Hide()
    {
        _active = true;
        _saved.RemoveAll(s => !IsExplorerTaskbar(s.Hwnd));
        using var explorerFirst = SystemTray.ExplorerFirst();
        var (windows, autohide) = Find();
        foreach (var (hwnd, primary) in windows)
            if (_saved.All(s => s.Hwnd != hwnd))
                Take(hwnd, primary, autohide);
    }

    public void Restore()
    {
        if (!_active)
            return;
        using (SystemTray.ExplorerFirst())
        {
            for (var i = _saved.Count - 1; i >= 0; i--)
                PutBack(_saved[i]);
        }

        _saved.Clear();
        _active = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watch?.Dispose();
            _watch = null;
        }
    }

    /// <summary>Explorer's taskbar windows, and whether its taskbar auto-hides.</summary>
    internal static (List<(nint Hwnd, bool Primary)> Windows, bool Autohide) Find()
    {
        var windows = Windows(TaskbarWindows.PrimaryClass).Select(hwnd => (Hwnd: hwnd, Primary: true))
            .Concat(Windows(TaskbarWindows.SecondaryClass).Select(hwnd => (Hwnd: hwnd, Primary: false))).ToList();
        return (windows, windows.Count > 0 && AppBars.Autohide(windows[0].Hwnd));
    }

    /// <summary>Shows a window again and, when its app bar was removed, registers it and reserves its edge.</summary>
    internal static void PutBack(Saved saved)
    {
        if (!IsExplorerTaskbar(saved.Hwnd))
            return;
        if (saved.WasVisible)
            Win32.ShowWindow(saved.Hwnd, Win32.SW_SHOWNA);
        if (!saved.Removed)
            return;
        if (!AppBars.Register(saved.Hwnd, Callback()))
            return;
        // Reserve the free part of the saved edge. The edge is named again: the query is its own call.
        var offered = AppBars.Query(saved.Hwnd, saved.Edge, saved.Rect);
        AppBars.Reserve(saved.Hwnd, saved.Edge, offered);
    }

    private void Take(nint hwnd, bool primary, bool autohide)
    {
        if (!Win32.IsWindow(hwnd))
            return;
        var saved = Saved.Of(hwnd, primary, autohide, Win32.IsWindowVisible(hwnd));
        // Record it before changing anything, so a failure halfway can still put this window back.
        _saved.Add(saved);
        if (saved.Reserves)
            saved.Removed = AppBars.Remove(hwnd);
        Win32.ShowWindow(hwnd, Win32.SW_HIDE);
    }

    private static uint Callback()
    {
        if (_callback == 0)
            _callback = Win32.RegisterWindowMessageW("WinshellExplorerTaskbar");
        return _callback;
    }

    private static bool TryPosition(nint hwnd, bool primary, out AppBarEdge edge, out RECT rect)
    {
        if (primary && AppBars.TryPosition(hwnd, out edge, out rect))
            return true;

        if (Win32.GetWindowRect(hwnd, out rect) && rect.Width > 0 && rect.Height > 0)
        {
            edge = EdgeOf(hwnd, rect);
            return true;
        }

        edge = AppBarEdge.Bottom;
        rect = default;
        return false;
    }

    /// <summary>The edge a taskbar window sits on, from where it is on its monitor. Explorer did not say.</summary>
    private static AppBarEdge EdgeOf(nint hwnd, RECT window)
    {
        var monitor = Monitors.TryRead(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), out var info)
            ? info.Bounds
            : window;
        if (window.Height <= window.Width)
            return Math.Abs(window.Top - monitor.Top) < Math.Abs(monitor.Bottom - window.Bottom)
                ? AppBarEdge.Top
                : AppBarEdge.Bottom;
        return Math.Abs(window.Left - monitor.Left) < Math.Abs(monitor.Right - window.Right)
            ? AppBarEdge.Left
            : AppBarEdge.Right;
    }

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
    private static bool IsExplorerTaskbar(nint hwnd)
    {
        return Win32.IsWindow(hwnd) &&
               TopLevelWindows.ClassName(hwnd) is TaskbarWindows.PrimaryClass or TaskbarWindows.SecondaryClass;
    }

    /// <summary>A taskbar window as it was before it was hidden.</summary>
    internal sealed class Saved
    {
        public nint Hwnd;
        public bool WasVisible;

        /// <summary>Whether it reserved an edge: <see cref="Edge"/> and <see cref="Rect"/> say where.</summary>
        public bool Reserves;

        /// <summary>Whether its app bar was removed, so putting it back registers it again.</summary>
        public bool Removed;

        public AppBarEdge Edge;
        public RECT Rect;

        internal static Saved Of(nint hwnd, bool primary, bool autohide, bool wasVisible)
        {
            var saved = new Saved { Hwnd = hwnd, WasVisible = wasVisible };
            saved.Reserves = !autohide && TryPosition(hwnd, primary, out saved.Edge, out saved.Rect);
            return saved;
        }
    }
}
