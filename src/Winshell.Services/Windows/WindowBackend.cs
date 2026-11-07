using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>
/// What Windows says about a window that the taskbar's rule uses. A window that is not visible reports only
/// <see cref="Exists"/>: its title is not read, so every other fact is false.
/// </summary>
internal readonly record struct TaskbarWindowFacts(
    bool Exists,
    bool Visible,
    bool Shell,
    bool Child,
    bool AppWindow,
    bool Owned,
    bool ToolWindow,
    bool NoActivate,
    bool HasTitle,
    bool Cloaked);

/// <summary>
/// One read of a window. <see cref="AppUserModelId"/> is the id the window set, read every time.
/// <see cref="PackageAppId"/> and <see cref="Executable"/> are null when they were not read.
/// </summary>
internal sealed record WindowRead(
    string Title,
    bool Maximized,
    bool Minimized,
    bool Fullscreen,
    string Monitor,
    string AppUserModelId,
    string? PackageAppId,
    string? Executable);

/// <summary>
/// The other programs' windows in Win32, for a <see cref="WindowListService"/>. Tests use a stand-in, so no real
/// window is read or moved. The shell uses <see cref="WindowBackend"/>.
/// </summary>
internal interface IWindowBackend
{
    /// <summary>
    /// A WinEvent: its type, window, object id and child id. Each hook raises it on the thread that set it, so
    /// <see cref="WatchForeground"/> and <see cref="Start"/> are called on one thread.
    /// </summary>
    event Action<uint, nint, int, int>? WinEvent;

    /// <summary>A window flashed to ask for attention. Raised on the thread that called <see cref="Start"/>.</summary>
    event Action<nint>? Flashed;

    /// <summary>The window that has the foreground. Zero when none.</summary>
    nint Foreground { get; }

    /// <summary>Starts raising <see cref="WinEvent"/> and <see cref="Flashed"/>, once. The thread must pump messages.</summary>
    void Start(Log log);

    /// <summary>
    /// Starts raising <see cref="WinEvent"/> for a new foreground and for windows that move or resize, once, without
    /// the rest <see cref="Start"/> watches. The thread must pump messages.
    /// </summary>
    void WatchForeground(Log log);

    /// <summary>
    /// The monitors the foreground window covers, as a fullscreen game or video does. None when the foreground is the
    /// desktop, which covers every monitor, or a tool window, as panels and popups are.
    /// </summary>
    IReadOnlySet<string> FullscreenScreens();

    /// <summary>The top-level windows, in z-order.</summary>
    IReadOnlyList<nint> TopLevel();

    /// <summary>The window that owns this one. Zero when none.</summary>
    nint OwnerOf(nint hwnd);

    /// <summary>The top-level window this window is in, or the window itself.</summary>
    nint RootOf(nint hwnd);

    /// <summary>
    /// The facts the taskbar's rule uses to decide whether this window gets a button. A window that is not visible is
    /// reported as such without reading its title.
    /// </summary>
    TaskbarWindowFacts TaskbarFacts(nint hwnd);

    /// <summary>
    /// Reads a window. The process package is read only when <paramref name="package"/> is true and
    /// the window set no id. The program path is read only when <paramref name="executable"/> is true.
    /// </summary>
    WindowRead Read(nint hwnd, bool package, bool executable);

    /// <summary>The connected monitor with this device name, or null.</summary>
    ScreenInfo? Screen(string deviceName);

    string DesktopId(nint hwnd);

    bool MoveToDesktop(nint hwnd, Guid desktop);

    void Activate(nint hwnd);

    void Close(nint hwnd);

    void SetMaximized(nint hwnd, bool maximized);

    void SetMinimized(nint hwnd, bool minimized);

    void MoveToMonitor(nint hwnd, string deviceName);
}

/// <summary>
/// The windows through Win32. The WinEvent hooks and the shell hook window are set on the UI thread and stay for the
/// process, the same way the desktop switch hook does. Which desktop a window is on uses the supported desktop API.
/// </summary>
internal sealed class WindowBackend : IWindowBackend
{
    public static WindowBackend Shared { get; } = new();

    private bool _started;
    private bool _foregroundWatched;

    private WindowBackend()
    {
    }

    public event Action<uint, nint, int, int>? WinEvent;

    public event Action<nint>? Flashed;

    public nint Foreground => Win32.GetForegroundWindow();

    public IReadOnlyList<nint> TopLevel()
    {
        return TopLevelWindows.List();
    }

    public nint OwnerOf(nint hwnd)
    {
        return Win32.GetWindow(hwnd, Win32.GW_OWNER);
    }

    public nint RootOf(nint hwnd)
    {
        return TopLevelWindows.RootOf(hwnd);
    }

    public ScreenInfo? Screen(string deviceName)
    {
        return Screens.Find(deviceName)?.Info;
    }

    public string DesktopId(nint hwnd)
    {
        return WindowDesktop.IdOf(hwnd);
    }

    public bool MoveToDesktop(nint hwnd, Guid desktop)
    {
        return WindowDesktop.Move(hwnd, desktop);
    }

    public void WatchForeground(Log log)
    {
        if (_foregroundWatched)
            return;
        _foregroundWatched = true;
        Hook(log, Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND);
        Hook(log, Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE);
    }

    public void Start(Log log)
    {
        if (_started)
            return;
        _started = true;
        WatchForeground(log);
        Hook(log, Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND);
        Hook(log, Win32.EVENT_OBJECT_CREATE, Win32.EVENT_OBJECT_HIDE);
        Hook(log, Win32.EVENT_OBJECT_NAMECHANGE, Win32.EVENT_OBJECT_NAMECHANGE);
        Hook(log, Win32.EVENT_OBJECT_CLOAKED, Win32.EVENT_OBJECT_UNCLOAKED);
        // The shell hook messages Explorer's taskbar gets. Only HSHELL_FLASH is read: WinEvents say nothing when a
        // window flashes.
        if (!ShellHookWindow.Start(OnShellHook, Failed))
            log.Warn("windows",
                "RegisterShellHookWindow failed; windows that flash will not show that they want attention");
    }

    private void Hook(Log log, uint min, uint max)
    {
        if (WinEventHook.Start(min, max, OnWinEvent, Failed) is null)
            log.Warn("windows", $"SetWinEventHook failed for events {min:x}-{max:x}; those changes will not be seen");
    }

    private static void Failed(Exception e)
    {
        Log.Default.Warn("windows", e.Message);
    }

    private void OnShellHook(nint code, nint hwnd)
    {
        if (WindowRules.IsFlash(code) && hwnd != 0)
            Flashed?.Invoke(hwnd);
    }

    private void OnWinEvent(uint eventType, nint hwnd, int idObject, int idChild)
    {
        WinEvent?.Invoke(eventType, hwnd, idObject, idChild);
    }

    public TaskbarWindowFacts TaskbarFacts(nint hwnd)
    {
        // Asking a window of this process for its title sends it a message with no timeout. A hidden one can belong to a
        // thread waiting for this one, as SystemEvents does while it raises an event, and neither would ever go on.
        if (!Win32.IsWindowVisible(hwnd))
            return new TaskbarWindowFacts(Win32.IsWindow(hwnd), false, false, false, false, false, false, false, false,
                false);
        var ex = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        return new TaskbarWindowFacts(
            Win32.IsWindow(hwnd),
            true,
            TopLevelWindows.IsShellWindow(hwnd),
            TopLevelWindows.HasChildStyle(hwnd),
            (ex & (uint)WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0,
            Win32.GetWindow(hwnd, Win32.GW_OWNER) != 0,
            (ex & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0,
            (ex & (uint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE) != 0,
            TopLevelWindows.HasTitle(hwnd),
            TopLevelWindows.IsCloaked(hwnd));
    }

    public WindowRead Read(nint hwnd, bool package, bool executable)
    {
        var minimized = Win32.IsIconic(hwnd);
        var maximized = !minimized && Win32.IsZoomed(hwnd);
        var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = default(MonitorInfo);
        var device = monitor != 0 && Monitors.TryRead(monitor, out mi) ? mi.Device : "";
        var fullscreen = false;
        if (!minimized && device.Length > 0 && Win32.GetWindowRect(hwnd, out var rect))
            fullscreen =
                WindowRules.CoversMonitor((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE), rect, mi.Bounds);

        // The property can arrive after the window is first shown, so it is read every time.
        var property = AppUserModelIds.OfWindow(hwnd);
        string? packageAppId = null;
        if (property.Length == 0 && package)
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            packageAppId = AppUserModelIds.OfProcess(pid);
        }

        return new WindowRead(TopLevelWindows.Title(hwnd), maximized, minimized, fullscreen, device, property,
            packageAppId, executable ? ExePath(hwnd) : null);
    }

    /// <summary>The full path of the program that owns a window, or empty when it cannot be read.</summary>
    private static string ExePath(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return ProcessImage.TryRead(pid) ?? "";
    }

    public IReadOnlySet<string> FullscreenScreens()
    {
        HashSet<string> screens = [];
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == 0 || !Win32.IsWindowVisible(hwnd) || TopLevelWindows.IsShellWindow(hwnd)
            || ((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0
            || !Win32.GetWindowRect(hwnd, out var rect))
            return screens;
        var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        foreach (var monitor in Screens.Enumerate())
            if (WindowRules.CoversMonitor(style, rect, monitor.Bounds))
                screens.Add(monitor.Info.Id);
        return screens;
    }

    /// <summary>
    /// Moves a window onto the monitor with this device name. <see cref="WindowOnScreen"/> keeps its logical size
    /// and its offset in the work area, and a maximized window is maximized again on the monitor it lands on. The
    /// shell is per-monitor aware, so the rectangle and the placement use the same pixels.
    /// </summary>
    public void MoveToMonitor(nint hwnd, string deviceName)
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
        var primary = monitors.FirstOrDefault(m => m.Info.IsPrimary);
        var window = new WindowToMove(current, hasPlacement ? placement.Normal : default, hasPlacement, minimized,
            maximized,
            ((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0);
        if (!WindowOnScreen.TryMove(window, MonitorFrame.Of(source.Bounds, source.Work, source.Scale),
                MonitorFrame.Of(target.Bounds, target.Work, target.Scale), primary?.Bounds ?? default,
                primary?.Work ?? default, out var place))
            return;
        if (hasPlacement)
            TopLevelWindows.Place(hwnd, placement, place.Workspace, minimized, maximized);
        else
            TopLevelWindows.Move(hwnd, place.Screen);
    }

    // Requests

    /// <summary>Brings a window to the front and focuses it.</summary>
    public void Activate(nint hwnd)
    {
        if (Win32.IsIconic(hwnd))
            TopLevelWindows.Restore(hwnd);
        Focus.Take(hwnd);
    }

    /// <summary>Asks the window to close, as its close button does.</summary>
    public void Close(nint hwnd)
    {
        TopLevelWindows.Close(hwnd);
    }

    public void SetMaximized(nint hwnd, bool maximized)
    {
        if (maximized)
            TopLevelWindows.Maximize(hwnd);
        else
            TopLevelWindows.Restore(hwnd);
    }

    public void SetMinimized(nint hwnd, bool minimized)
    {
        if (minimized)
            TopLevelWindows.Minimize(hwnd);
        else
            TopLevelWindows.Restore(hwnd);
    }
}
