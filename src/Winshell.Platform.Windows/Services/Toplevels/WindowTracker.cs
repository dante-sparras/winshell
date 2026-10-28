using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using static Winshell.Platform.Windows.Services.Toplevels.NativeMethods;

namespace Winshell.Platform.Windows.Services.Toplevels;

/// <summary>What Winshell knows about one window on the taskbar.</summary>
internal sealed record WindowInfo(
    nint Hwnd,
    string Title,
    string AppId,
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
                log.Warn("toplevels", $"SetWinEventHook failed for events {min:x}-{max:x}; those changes will not be seen");
        }
        _enumerated = [];
        EnumWindows(&Collect, 0);
        foreach (var hwnd in _enumerated)
            Dirty.Add(hwnd);
        _enumerated = null;
        _foregroundDirty = true;
        Flush();
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint data)
    {
        _enumerated!.Add(hwnd);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            _foregroundDirty = true;
            if (hwnd != 0)
                Dirty.Add(hwnd);
        }
        else
        {
            if (hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
                return;
            // Only top-level windows; a destroyed one can no longer say what it was.
            if (eventType == EVENT_OBJECT_DESTROY ? !Tracked.ContainsKey(hwnd) : GetAncestor(hwnd, GA_ROOT) != hwnd)
                return;
            Dirty.Add(hwnd);
            if (eventType is EVENT_SYSTEM_MINIMIZESTART or EVENT_SYSTEM_MINIMIZEEND or EVENT_OBJECT_DESTROY or EVENT_OBJECT_HIDE)
                _foregroundDirty = true;
        }
        if (!_flushPosted && _loop is not null)
        {
            _flushPosted = true;
            _loop.Post(Flush);
        }
    }

    private static void Flush()
    {
        _flushPosted = false;
        var changed = false;
        foreach (var hwnd in Dirty.ToArray())
        {
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
        Dirty.Clear();
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
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || hwnd == GetShellWindow())
            return false;
        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((style & WS_CHILD) != 0)
            return false;
        var appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow && (GetWindow(hwnd, GW_OWNER) != 0 || (ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0))
            return false;
        return GetWindowTextLength(hwnd) > 0 && !IsCloaked(hwnd);
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
        // The app id is read once: reading it asks the shell and the process.
        var appId = old is { AppId.Length: > 0 } ? old.AppId : AppIdOf(hwnd);
        var owner = GetWindow(hwnd, GW_OWNER);
        return new WindowInfo(hwnd, WindowText(hwnd), appId, owner, maximized, minimized, fullscreen, device);
    }

    /// <summary>The window's AppUserModelID, or else its program's file name in lower case without the extension.</summary>
    public static string AppIdOf(nint hwnd)
    {
        var aumid = WindowAppId(hwnd);
        if (aumid.Length > 0)
            return aumid;
        var exe = ExePath(hwnd);
        return exe.Length > 0 ? Path.GetFileNameWithoutExtension(exe).ToLowerInvariant() : "";
    }

    // Requests

    /// <summary>
    /// Brings a window to the front and focuses it. Windows lets a program take the foreground only in some cases, so
    /// when asking fails, Winshell attaches to the foreground thread's input, and as a last resort taps Alt.
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
            keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, 0);
            keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
            SetForegroundWindow(hwnd);
        }
    }

    /// <summary>Asks the window to close, as its close button does.</summary>
    public static void Close(nint hwnd) => PostMessage(hwnd, WM_SYSCOMMAND, SC_CLOSE, 0);

    public static void SetMaximized(nint hwnd, bool maximized) => ShowWindowAsync(hwnd, maximized ? SW_MAXIMIZE : SW_RESTORE);

    public static void SetMinimized(nint hwnd, bool minimized) => ShowWindowAsync(hwnd, minimized ? SW_MINIMIZE : SW_RESTORE);
}
