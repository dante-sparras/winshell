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
/// The windows the taskbar and Alt+Tab show, for the whole process, kept current by WinEvent hooks set on the UI
/// thread. Events are gathered and read once per turn of the loop, then <see cref="Changed"/> is raised.
/// </summary>
internal static unsafe class WindowTracker
{
    private static readonly Dictionary<nint, WindowInfo> Tracked = [];
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
        var isRoot = hwnd != 0 && eventType != EVENT_OBJECT_DESTROY && GetAncestor(hwnd, GA_ROOT) == hwnd;
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
            var info = IsTaskbarWindow(hwnd) ? Describe(hwnd, old) : null;
            if (info is null)
                changed |= Tracked.Remove(hwnd);
            else if (info != old)
            {
                Tracked[hwnd] = info;
                changed = true;
            }
        }
        if (_foregroundDirty)
        {
            _foregroundDirty = false;
            var active = ActiveOf(GetForegroundWindow());
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
        var hwnd = foreground == 0 ? 0 : GetAncestor(foreground, GA_ROOT);
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
        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        return WindowRules.ShowsOnTaskbar(new TaskbarWindowFacts(
            IsWindow(hwnd),
            IsWindowVisible(hwnd),
            hwnd == GetShellWindow(),
            (style & WS_CHILD) != 0,
            (ex & WS_EX_APPWINDOW) != 0,
            GetWindow(hwnd, GW_OWNER) != 0,
            (ex & WS_EX_TOOLWINDOW) != 0,
            (ex & WS_EX_NOACTIVATE) != 0,
            GetWindowTextLength(hwnd) > 0,
            IsCloaked(hwnd)));
    }

    private static WindowInfo Describe(nint hwnd, WindowInfo? old)
    {
        var minimized = IsIconic(hwnd);
        var maximized = !minimized && IsZoomed(hwnd);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        var device = monitor != 0 && Win32.GetMonitorInfo(monitor, ref mi) ? new string(mi.szDevice).TrimEnd('\0') : "";
        var fullscreen = false;
        if (!minimized && device.Length > 0 && GetWindowRect(hwnd, out var rect))
        {
            // No title bar and covering the whole monitor, as a game or a video player in fullscreen does.
            var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
            var screen = mi.rcMonitor;
            fullscreen = (style & WS_CAPTION) != WS_CAPTION && rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
        }
        // The app id and icon are read once: reading them asks the shell and the process.
        string executable, appId, icon;
        if (old is { } kept && (kept.AppId.Length > 0 || kept.Executable.Length > 0))
        {
            executable = kept.Executable;
            appId = kept.AppId.Length > 0 ? kept.AppId : WindowRules.AppId("", executable);
            icon = kept.Icon.Length > 0 ? kept.Icon : WindowRules.IconPath("", executable);
        }
        else
        {
            executable = ExePath(hwnd);
            var aumid = WindowAppId(hwnd);
            appId = WindowRules.AppId(aumid, executable);
            icon = WindowRules.IconPath(aumid, executable);
        }
        var owner = GetWindow(hwnd, GW_OWNER);
        return new WindowInfo(hwnd, WindowText(hwnd), appId, executable, icon, owner, maximized, minimized, fullscreen, device);
    }

    /// <summary>
    /// Moves a window onto the monitor with this device name. Its logical size and its offset in the work area stay,
    /// and a maximized window is maximized again on the monitor it lands on. The shell is per-monitor aware, so the
    /// rectangle and the placement use the same pixels.
    /// </summary>
    public static void MoveToMonitor(nint hwnd, string deviceName)
    {
        if (deviceName.Length == 0 || !IsWindow(hwnd))
            return;
        var target = Screens.Find(deviceName);
        if (target is null)
            return;
        var sourceHandle = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var source = sourceHandle == 0 ? null : Screens.Describe(sourceHandle);
        if (source is null || source.Info.Id == target.Info.Id)
            return;
        if (!GetWindowRect(hwnd, out var current))
            return;
        var placement = new WINDOWPLACEMENT { Length = (uint)sizeof(WINDOWPLACEMENT) };
        var hasPlacement = GetWindowPlacement(hwnd, ref placement);
        var minimized = IsIconic(hwnd);
        var maximized = !minimized && IsZoomed(hwnd);
        var sourceRect = WindowRules.RectangleToMove(minimized, maximized, current, hasPlacement ? placement.Normal : current);
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
            return;
        var next = WindowRules.MoveOnto(sourceRect, Frame(source), Frame(target));
        if (!hasPlacement)
        {
            Win32.SetWindowPos(hwnd, 0, next.Left, next.Top, next.Width, next.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            return;
        }
        // A restored window can disagree with its placement by a constant. Carry that into the placement.
        var biasX = !minimized && !maximized ? current.Left - placement.Normal.Left : 0;
        var biasY = !minimized && !maximized ? current.Top - placement.Normal.Top : 0;
        placement.Normal = new RECT(next.Left - biasX, next.Top - biasY, next.Right - biasX, next.Bottom - biasY);
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
        if (IsIconic(hwnd))
            ShowWindowAsync(hwnd, SW_RESTORE);
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd)
            return;
        var foreground = GetForegroundWindow();
        var current = GetCurrentThreadId();
        var other = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        if (other != 0 && other != current && AttachThreadInput(current, other, true))
        {
            try
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                AttachThreadInput(current, other, false);
            }
        }
        if (GetForegroundWindow() != hwnd)
        {
            // A key press makes this process the one that had the last input, which may take the foreground.
            KeyMask.Tap();
            SetForegroundWindow(hwnd);
        }
    }

    /// <summary>Asks the window to close, as its close button does.</summary>
    public static void Close(nint hwnd) => PostMessage(hwnd, WM_SYSCOMMAND, SC_CLOSE, 0);

    public static void SetMaximized(nint hwnd, bool maximized) => ShowWindowAsync(hwnd, maximized ? SW_MAXIMIZE : SW_RESTORE);

    public static void SetMinimized(nint hwnd, bool minimized) => ShowWindowAsync(hwnd, minimized ? SW_MINIMIZE : SW_RESTORE);
}
