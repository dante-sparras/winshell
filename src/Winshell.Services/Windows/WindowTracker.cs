using Winshell.Native;
using Winshell.Input;

namespace Winshell.Windows;

/// <summary>What Winshell knows about one window on the taskbar.</summary>
internal sealed record WindowInfo(
    nint Hwnd,
    string Title,
    string AppId,
    string Executable,
    string Icon,
    nint Owner,
    bool Maximized,
    bool Minimized,
    bool Fullscreen,
    string Monitor);

/// <summary>
/// The app id kept for a window between reads. <see cref="PackageKnown"/> is true once the process package has been
/// asked, including when it has none.
/// </summary>
internal readonly record struct WindowIdentity(
    string AppId,
    string Executable,
    string Icon,
    string PackageAppId,
    bool PackageKnown);

/// <summary>
/// The windows the taskbar and Alt+Tab show, for the whole process, kept current by WinEvent hooks set on the UI
/// thread. Events are gathered and read once per turn of the loop, then <see cref="Changed"/> is raised.
/// </summary>
internal static class WindowTracker
{
    private static readonly Dictionary<nint, WindowInfo> Tracked = [];
    private static readonly Dictionary<nint, WindowIdentity> Identities = [];
    private static readonly HashSet<nint> Dirty = [];
    private static readonly HashSet<nint> Flashed = [];
    private static readonly HashSet<nint> Wanting = [];
    private static IEventLoop? _loop;
    private static bool _flushPosted;
    private static bool _foregroundDirty;

    /// <summary>Raised on the UI thread after windows came, went or changed, or the foreground moved.</summary>
    public static event Action? Changed;

    public static IReadOnlyDictionary<nint, WindowInfo> Windows => Tracked;

    /// <summary>The tracked window that has the foreground, or owns the window that has it. Zero when none.</summary>
    public static nint Active { get; private set; }

    /// <summary>Whether the window flashed to ask for attention and has not been activated since.</summary>
    public static bool WantsAttention(nint hwnd) => Wanting.Contains(hwnd);

    /// <summary>Sets the hooks and reads the open windows, once. Must run on the UI thread, which pumps messages.</summary>
    public static void EnsureStarted(IEventLoop loop, Log log)
    {
        if (_loop is not null)
            return;
        _loop = loop;
        (uint Min, uint Max)[] ranges =
        [
            (Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND),
            (Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND),
            (Win32.EVENT_OBJECT_CREATE, Win32.EVENT_OBJECT_HIDE),
            (Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_NAMECHANGE),
            (Win32.EVENT_OBJECT_CLOAKED, Win32.EVENT_OBJECT_UNCLOAKED),
        ];
        foreach (var (min, max) in ranges)
        {
            if (!WinEventHook.Start(min, max, OnWinEvent))
                log.Warn("windows", $"SetWinEventHook failed for events {min:x}-{max:x}; those changes will not be seen");
        }
        // The shell hook messages Explorer's taskbar gets. Only HSHELL_FLASH is read: WinEvents say nothing when a
        // window flashes.
        if (!ShellHookWindow.Start(OnShellHook))
            log.Warn("windows", "RegisterShellHookWindow failed; windows that flash will not show that they want attention");
        foreach (var hwnd in TopLevelWindows.List())
            Dirty.Add(hwnd);
        _foregroundDirty = true;
        Flush();
    }

    private static void OnShellHook(nint code, nint hwnd) =>
        NativeCallback.Guard(Log.Default, "windows", () =>
        {
            if (WindowRules.IsFlash(code) && hwnd != 0)
            {
                Flashed.Add(hwnd);
                if (!_flushPosted && _loop is not null)
                {
                    _flushPosted = true;
                    _loop.Post(Flush);
                }
            }
        });

    private static void OnWinEvent(uint eventType, nint hwnd, int idObject, int idChild) =>
        NativeCallback.Guard(Log.Default, "windows", () => Mark(eventType, hwnd, idObject, idChild));

    private static void Mark(uint eventType, nint hwnd, int idObject, int idChild)
    {
        // Windows reports no move of a window on its own, so maximizing, restoring and moving to another monitor
        // arrive as location changes. The caret and cursor send them too, on every move, so drop those first.
        if (eventType == Win32.EVENT_OBJECT_LOCATIONCHANGE && !WindowRules.IsWindowObject(idObject, idChild))
            return;
        var isRoot = hwnd != 0 && eventType != Win32.EVENT_OBJECT_DESTROY && TopLevelWindows.RootOf(hwnd) == hwnd;
        var marksWindow = WindowRules.MarksWindow(eventType, hwnd, idObject, idChild, Tracked.ContainsKey(hwnd), isRoot);
        if (!marksWindow && !WindowRules.MarksForeground(eventType))
            return;
        if (marksWindow)
            Dirty.Add(hwnd);
        if (WindowRules.MarksForeground(eventType))
            _foregroundDirty = true;
        if (!_flushPosted && _loop is not null)
        {
            _flushPosted = true;
            _loop.Post(Flush);
        }
    }

    private static void Flush()
    {
        _flushPosted = false;
        // Describe can pump once the UI thread is STA. Drop only the hwnds this pass already saw,
        // so a window marked during that call stays for the flush OnWinEvent already posted.
        var pending = Dirty.ToArray();
        foreach (var hwnd in pending)
            Dirty.Remove(hwnd);
        var changed = false;
        foreach (var hwnd in pending)
        {
            Tracked.TryGetValue(hwnd, out var old);
            var info = IsTaskbarWindow(hwnd) ? Describe(hwnd) : null;
            if (info is null)
            {
                changed |= Tracked.Remove(hwnd);
                Identities.Remove(hwnd);
            }
            else if (info != old)
            {
                Tracked[hwnd] = info;
                changed = true;
            }
        }
        if (_foregroundDirty)
        {
            _foregroundDirty = false;
            var active = ActiveOf(Win32.GetForegroundWindow());
            if (active != Active)
            {
                Active = active;
                changed = true;
            }
        }
        if (!Tracked.ContainsKey(Active) && Active != 0)
        {
            Active = 0;
            changed = true;
        }
        // A dialog that flashes asks for the window that owns it. The window in front already has the user.
        foreach (var hwnd in Flashed)
        {
            var wanting = ActiveOf(hwnd);
            if (wanting != 0 && wanting != Active)
                changed |= Wanting.Add(wanting);
        }
        Flashed.Clear();
        changed |= WindowRules.SettleAttention(Wanting, Active, Tracked.ContainsKey);
        if (changed)
            Changed?.Invoke();
    }

    private static nint ActiveOf(nint foreground)
    {
        var hwnd = foreground == 0 ? 0 : TopLevelWindows.RootOf(foreground);
        for (var depth = 0; hwnd != 0 && depth < 16; depth++)
        {
            if (Tracked.ContainsKey(hwnd))
                return hwnd;
            hwnd = Win32.GetWindow(hwnd, Win32.GW_OWNER);
        }
        return 0;
    }

    /// <summary>
    /// The taskbar's rule: a visible, uncloaked top-level window with a title that either asks for a taskbar button
    /// or has no owner and is not a tool window or a window that never activates.
    /// </summary>
    internal static bool IsTaskbarWindow(nint hwnd)
    {
        // Asking a window of this process for its title sends it a message with no timeout. A hidden one can belong to a
        // thread waiting for this one, as SystemEvents does while it raises an event, and neither would ever go on.
        if (!Win32.IsWindowVisible(hwnd))
            return false;
        var ex = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        return WindowRules.ShowsOnTaskbar(new TaskbarWindowFacts(
            Win32.IsWindow(hwnd),
            Win32.IsWindowVisible(hwnd),
            TopLevelWindows.IsShellWindow(hwnd),
            TopLevelWindows.HasChildStyle(hwnd),
            (ex & Win32.WS_EX_APPWINDOW) != 0,
            Win32.GetWindow(hwnd, Win32.GW_OWNER) != 0,
            (ex & Win32.WS_EX_TOOLWINDOW) != 0,
            (ex & Win32.WS_EX_NOACTIVATE) != 0,
            TopLevelWindows.HasTitle(hwnd),
            TopLevelWindows.IsCloaked(hwnd)));
    }

    private static WindowInfo Describe(nint hwnd)
    {
        var minimized = Win32.IsIconic(hwnd);
        var maximized = !minimized && Win32.IsZoomed(hwnd);
        var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = default(MonitorInfo);
        var device = monitor != 0 && Monitors.TryRead(monitor, out mi) ? mi.Device : "";
        var fullscreen = false;
        if (!minimized && device.Length > 0 && Win32.GetWindowRect(hwnd, out var rect))
        {
            // No title bar and covering the whole monitor, as a game or a video player in fullscreen does.
            var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
            fullscreen = WindowRules.CoversMonitor(style, rect, mi.Bounds);
        }
        Identities.TryGetValue(hwnd, out var previous);
        // The property can arrive after the window is first shown. Read it every time. The package id and the
        // program path do not change, so each is read once.
        var property = AppUserModelIds.OfWindow(hwnd);
        string? package = null;
        if (property.Length == 0 && !previous.PackageKnown)
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            package = AppUserModelIds.OfProcess(pid);
        }
        string? executable = previous.Executable is { Length: > 0 } ? null : ExePath(hwnd);
        var identity = Resolve(previous, property, package, executable);
        Identities[hwnd] = identity;
        var owner = Win32.GetWindow(hwnd, Win32.GW_OWNER);
        return new WindowInfo(hwnd, TopLevelWindows.Title(hwnd), identity.AppId, identity.Executable, identity.Icon, owner, maximized, minimized, fullscreen, device);
    }

    /// <summary>
    /// The app id to keep after one read of the window. <paramref name="windowProperty"/> is the property store now,
    /// and it replaces a package id or a program name. An empty property uses <paramref name="packageAppId"/> when
    /// that was read, or the package id already known. A null executable keeps the path already read.
    /// </summary>
    internal static WindowIdentity Resolve(WindowIdentity previous, string windowProperty, string? packageAppId, string? executable)
    {
        var package = packageAppId ?? previous.PackageAppId ?? "";
        var path = executable ?? previous.Executable ?? "";
        var source = windowProperty.Length > 0 ? windowProperty : package;
        return new WindowIdentity(
            WindowRules.AppId(windowProperty, package, path),
            path,
            WindowRules.IconPath(source, path),
            package,
            previous.PackageKnown || packageAppId is not null);
    }

    /// <summary>The full path of the program that owns a window, or empty when it cannot be read.</summary>
    private static string ExePath(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return ProcessImage.TryRead(pid) ?? "";
    }

    /// <summary>
    /// Moves a window onto the monitor with this device name. Its logical size and its offset in the work area stay,
    /// and a maximized window is maximized again on the monitor it lands on. The shell is per-monitor aware, so the
    /// rectangle and the placement use the same pixels.
    /// </summary>
    public static void MoveToMonitor(nint hwnd, string deviceName)
    {
        if (deviceName.Length == 0 || !Win32.IsWindow(hwnd))
            return;
        var monitors = Screens.Enumerate();
        var target = monitors.FirstOrDefault(m => m.Info.Id == deviceName);
        if (target is null)
            return;
        var sourceHandle = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var source = sourceHandle == 0 ? null : Screens.Describe(sourceHandle);
        if (source is null || source.Info.Id == target.Info.Id)
            return;
        if (!Win32.GetWindowRect(hwnd, out var current))
            return;
        var hasPlacement = TopLevelWindows.TryPlacement(hwnd, out var placement);
        var minimized = Win32.IsIconic(hwnd);
        var maximized = !minimized && Win32.IsZoomed(hwnd);
        // The placement is in workspace coordinates and the monitor frames are in screen coordinates. A restored
        // window shows the difference as a constant. A minimized or maximized one does not, so it takes the offset
        // of the primary monitor's work area.
        var bias = (X: 0, Y: 0);
        if (hasPlacement && !minimized && !maximized)
            bias = (current.Left - placement.Normal.Left, current.Top - placement.Normal.Top);
        else if (hasPlacement && monitors.FirstOrDefault(m => m.Info.IsPrimary) is { } primary)
            bias = WindowRules.WorkspaceOffset(primary.Bounds, primary.Work, ((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0);
        var sourceRect = WindowRules.RectangleToMove(minimized, maximized, current, WindowRules.Shift(placement.Normal, bias.X, bias.Y));
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
            return;
        var next = WindowRules.MoveOnto(sourceRect, Frame(source), Frame(target));
        if (hasPlacement)
            TopLevelWindows.Place(hwnd, placement, WindowRules.Shift(next, -bias.X, -bias.Y), minimized, maximized);
        else
            TopLevelWindows.Move(hwnd, next);
    }

    private static MonitorFrame Frame(ConnectedMonitor monitor)
    {
        var area = monitor.Work.Width > 0 && monitor.Work.Height > 0 ? monitor.Work : monitor.Bounds;
        return new MonitorFrame(area.Left, area.Top, area.Right, area.Bottom, monitor.Scale);
    }

    // Requests

    /// <summary>
    /// Brings a window to the front and focuses it. Windows lets a program take the foreground only in some cases, so
    /// when asking fails, Winshell attaches to the foreground thread's input, and as a last resort taps an unassigned key.
    /// </summary>
    public static void Activate(nint hwnd)
    {
        if (Win32.IsIconic(hwnd))
            TopLevelWindows.Restore(hwnd);
        if (Win32.SetForegroundWindow(hwnd) && Win32.GetForegroundWindow() == hwnd)
            return;
        var foreground = Win32.GetForegroundWindow();
        var current = Win32.GetCurrentThreadId();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        if (other != 0 && other != current && Win32.AttachThreadInput(current, other, true))
        {
            try
            {
                TopLevelWindows.Raise(hwnd);
                Win32.SetForegroundWindow(hwnd);
            }
            finally
            {
                Win32.AttachThreadInput(current, other, false);
            }
        }
        if (Win32.GetForegroundWindow() != hwnd)
        {
            // A key press makes this process the one that had the last input, which may take the foreground.
            KeyMask.Tap();
            Win32.SetForegroundWindow(hwnd);
        }
    }

    /// <summary>Asks the window to close, as its close button does.</summary>
    public static void Close(nint hwnd) => TopLevelWindows.Close(hwnd);

    public static void SetMaximized(nint hwnd, bool maximized)
    {
        if (maximized)
            TopLevelWindows.Maximize(hwnd);
        else
            TopLevelWindows.Restore(hwnd);
    }

    public static void SetMinimized(nint hwnd, bool minimized)
    {
        if (minimized)
            TopLevelWindows.Minimize(hwnd);
        else
            TopLevelWindows.Restore(hwnd);
    }
}
