using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.Input;
using Winshell.Runtime;
using static Winshell.Platform.Windows.Services.WindowList.NativeMethods;

namespace Winshell.Platform.Windows.Services.WindowList;

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
internal static unsafe class WindowTracker
{
    private static readonly Dictionary<nint, WindowInfo> Tracked = [];
    private static readonly Dictionary<nint, WindowIdentity> Identities = [];
    private static readonly HashSet<nint> Dirty = [];
    private static readonly HashSet<nint> Flashed = [];
    private static readonly HashSet<nint> Wanting = [];
    private static uint _shellHookMessage;
    private static IEventLoop? _loop;
    private static bool _flushPosted;
    private static bool _foregroundDirty;

    [ThreadStatic]
    private static List<nint>? _enumerated;

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
            (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
            (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
            (EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE),
            (EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_NAMECHANGE),
            (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
        ];
        foreach (var (min, max) in ranges)
        {
            if (SetWinEventHook(min, max, 0, &OnWinEvent, 0, 0, WINEVENT_OUTOFCONTEXT) == 0)
                log.Warn("windows", $"SetWinEventHook failed for events {min:x}-{max:x}; those changes will not be seen");
        }
        StartShellHook(log);
        _enumerated = [];
        EnumWindows(&Collect, 0);
        foreach (var hwnd in _enumerated)
            Dirty.Add(hwnd);
        _enumerated = null;
        _foregroundDirty = true;
        Flush();
    }

    /// <summary>
    /// Asks for the shell hook messages Explorer's taskbar gets. Only <c>HSHELL_FLASH</c> is read: WinEvents say
    /// nothing when a window flashes.
    /// </summary>
    private static void StartShellHook(Log log)
    {
        const string className = "WinshellShellHook";
        _shellHookMessage = Win32.RegisterWindowMessageW("SHELLHOOK");
        fixed (char* name = className)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &ShellHookProc,
                hInstance = Win32.GetModuleHandleW(null),
                lpszClassName = name,
            };
            Win32.RegisterClassExW(wc);
        }
        var hwnd = Win32.CreateWindowExW(0, className, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, Win32.GetModuleHandleW(null), 0);
        if (hwnd == 0 || _shellHookMessage == 0 || !RegisterShellHookWindow(hwnd))
            log.Warn("windows", "RegisterShellHookWindow failed; windows that flash will not show that they want attention");
    }

    [UnmanagedCallersOnly]
    private static nint ShellHookProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message != _shellHookMessage || _shellHookMessage == 0)
            return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
        NativeCallback.Guard(Log.Default, "windows", () =>
        {
            if (WindowRules.IsFlash(wParam) && lParam != 0)
            {
                Flashed.Add(lParam);
                if (!_flushPosted && _loop is not null)
                {
                    _flushPosted = true;
                    _loop.Post(Flush);
                }
            }
        });
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint data)
    {
        _enumerated!.Add(hwnd);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time) =>
        NativeCallback.Guard(Log.Default, "windows", () => Mark(eventType, hwnd, idObject, idChild));

    private static void Mark(uint eventType, nint hwnd, int idObject, int idChild)
    {
        // Windows reports no move of a window on its own, so maximizing, restoring and moving to another monitor
        // arrive as location changes. The caret and cursor send them too, on every move, so drop those first.
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && !WindowRules.IsWindowObject(idObject, idChild))
            return;
        var isRoot = hwnd != 0 && eventType != EVENT_OBJECT_DESTROY && Win32.GetAncestor(hwnd, GA_ROOT) == hwnd;
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

    private static void Flush() => FlushPending(duringDescribe: null);

    /// <summary>Hwnds waiting for the next flush. Tests use this to see a mark that arrived mid-pass.</summary>
    internal static int PendingCount => Dirty.Count;

    internal static void MarkDirty(nint hwnd) => Dirty.Add(hwnd);

    internal static void FlushPending(Action? duringDescribe)
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
            duringDescribe?.Invoke();
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
        var hwnd = foreground == 0 ? 0 : Win32.GetAncestor(foreground, GA_ROOT);
        for (var depth = 0; hwnd != 0 && depth < 16; depth++)
        {
            if (Tracked.ContainsKey(hwnd))
                return hwnd;
            hwnd = GetWindow(hwnd, GW_OWNER);
        }
        return 0;
    }

    /// <summary>
    /// The taskbar's rule: a visible, uncloaked top-level window with a title that either asks for a taskbar button
    /// or has no owner and is not a tool window or a window that never activates.
    /// </summary>
    private static bool IsTaskbarWindow(nint hwnd)
    {
        var style = (long)Win32.GetWindowLongPtr(hwnd, GWL_STYLE);
        var ex = (long)Win32.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        return WindowRules.ShowsOnTaskbar(new TaskbarWindowFacts(
            Win32.IsWindow(hwnd),
            Win32.IsWindowVisible(hwnd),
            hwnd == GetShellWindow(),
            (style & WS_CHILD) != 0,
            (ex & WS_EX_APPWINDOW) != 0,
            GetWindow(hwnd, GW_OWNER) != 0,
            (ex & WS_EX_TOOLWINDOW) != 0,
            (ex & WS_EX_NOACTIVATE) != 0,
            GetWindowTextLength(hwnd) > 0,
            IsCloaked(hwnd)));
    }

    private static WindowInfo Describe(nint hwnd)
    {
        var minimized = Win32.IsIconic(hwnd);
        var maximized = !minimized && Win32.IsZoomed(hwnd);
        var monitor = Win32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        var device = monitor != 0 && Win32.GetMonitorInfo(monitor, ref mi) ? new string(mi.szDevice).TrimEnd('\0') : "";
        var fullscreen = false;
        if (!minimized && device.Length > 0 && Win32.GetWindowRect(hwnd, out var rect))
        {
            // No title bar and covering the whole monitor, as a game or a video player in fullscreen does.
            var style = (long)Win32.GetWindowLongPtr(hwnd, GWL_STYLE);
            var screen = mi.rcMonitor;
            fullscreen = (style & WS_CAPTION) != WS_CAPTION && rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
        }
        Identities.TryGetValue(hwnd, out var previous);
        // The property can arrive after the window is first shown. Read it every time. The package id and the
        // program path do not change, so each is read once.
        var property = WindowAppId(hwnd);
        string? package = null;
        if (property.Length == 0 && !previous.PackageKnown)
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            package = ProcessAppUserModelId(pid);
        }
        string? executable = previous.Executable is { Length: > 0 } ? null : ExePath(hwnd);
        var identity = Resolve(previous, property, package, executable);
        Identities[hwnd] = identity;
        var owner = GetWindow(hwnd, GW_OWNER);
        return new WindowInfo(hwnd, WindowText(hwnd), identity.AppId, identity.Executable, identity.Icon, owner, maximized, minimized, fullscreen, device);
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
        var sourceHandle = Win32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var source = sourceHandle == 0 ? null : Screens.Describe(sourceHandle);
        if (source is null || source.Info.Id == target.Info.Id)
            return;
        if (!Win32.GetWindowRect(hwnd, out var current))
            return;
        var placement = new WINDOWPLACEMENT { Length = (uint)sizeof(WINDOWPLACEMENT) };
        var hasPlacement = GetWindowPlacement(hwnd, ref placement);
        var minimized = Win32.IsIconic(hwnd);
        var maximized = !minimized && Win32.IsZoomed(hwnd);
        // The placement is in workspace coordinates and the monitor frames are in screen coordinates. A restored
        // window shows the difference as a constant. A minimized or maximized one does not, so it takes the offset
        // of the primary monitor's work area.
        var bias = (X: 0, Y: 0);
        if (hasPlacement && !minimized && !maximized)
            bias = (current.Left - placement.Normal.Left, current.Top - placement.Normal.Top);
        else if (hasPlacement && monitors.FirstOrDefault(m => m.Info.IsPrimary) is { } primary)
            bias = WindowRules.WorkspaceOffset(primary.Bounds, primary.Work, ((long)Win32.GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0);
        var sourceRect = WindowRules.RectangleToMove(minimized, maximized, current, WindowRules.Shift(placement.Normal, bias.X, bias.Y));
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
            return;
        var next = WindowRules.MoveOnto(sourceRect, Frame(source), Frame(target));
        if (!hasPlacement)
        {
            Win32.SetWindowPos(hwnd, 0, next.Left, next.Top, next.Width, next.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
            return;
        }
        placement.Normal = WindowRules.Shift(next, -bias.X, -bias.Y);
        placement.ShowCmd = (uint)(minimized ? SW_SHOWMINIMIZED : maximized ? SW_MAXIMIZE : SW_SHOWNORMAL);
        placement.Flags |= WPF_ASYNCWINDOWPLACEMENT;
        SetWindowPlacement(hwnd, placement);
    }

    private static MonitorFrame Frame(Monitor monitor)
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
            ShowWindowAsync(hwnd, SW_RESTORE);
        if (Win32.SetForegroundWindow(hwnd) && Win32.GetForegroundWindow() == hwnd)
            return;
        var foreground = Win32.GetForegroundWindow();
        var current = Win32.GetCurrentThreadId();
        var other = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        if (other != 0 && other != current && Win32.AttachThreadInput(current, other, true))
        {
            try
            {
                Win32.SetWindowPos(hwnd, Win32.HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
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
    public static void Close(nint hwnd) => Win32.PostMessageW(hwnd, WM_SYSCOMMAND, SC_CLOSE, 0);

    public static void SetMaximized(nint hwnd, bool maximized) => ShowWindowAsync(hwnd, maximized ? SW_MAXIMIZE : SW_RESTORE);

    public static void SetMinimized(nint hwnd, bool minimized) => ShowWindowAsync(hwnd, minimized ? SW_MINIMIZE : SW_RESTORE);
}
