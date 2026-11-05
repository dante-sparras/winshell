using Winshell.Native;
using Winshell.Input;

namespace Winshell.Windows;

/// <summary>
/// One read of a window the taskbar shows. <see cref="AppUserModelId"/> is the id the window set, read every time.
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
    /// <summary>A WinEvent: its type, window, object id and child id. Raised on the thread that called <see cref="Start"/>.</summary>
    event Action<uint, nint, int, int>? WinEvent;

    /// <summary>A window flashed to ask for attention. Raised on the thread that called <see cref="Start"/>.</summary>
    event Action<nint>? Flashed;

    /// <summary>The window that has the foreground. Zero when none.</summary>
    nint Foreground { get; }

    /// <summary>Starts raising <see cref="WinEvent"/> and <see cref="Flashed"/>, once. The thread must pump messages.</summary>
    void Start(Log log);

    /// <summary>The top-level windows, in z-order.</summary>
    IReadOnlyList<nint> TopLevel();

    /// <summary>The window that owns this one. Zero when none.</summary>
    nint OwnerOf(nint hwnd);

    /// <summary>The top-level window this window is in, or the window itself.</summary>
    nint RootOf(nint hwnd);

    /// <summary>
    /// Reads a window when the taskbar shows it, or null. The process package is read only when
    /// <paramref name="package"/> is true and the window set no id. The program path is read only when
    /// <paramref name="executable"/> is true.
    /// </summary>
    WindowRead? Read(nint hwnd, bool package, bool executable);

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

    private WindowBackend()
    {
    }

    public event Action<uint, nint, int, int>? WinEvent;

    public event Action<nint>? Flashed;

    public nint Foreground => Win32.GetForegroundWindow();

    public IReadOnlyList<nint> TopLevel() => TopLevelWindows.List();

    public nint OwnerOf(nint hwnd) => Win32.GetWindow(hwnd, Win32.GW_OWNER);

    public nint RootOf(nint hwnd) => TopLevelWindows.RootOf(hwnd);

    public ScreenInfo? Screen(string deviceName) => Screens.Find(deviceName)?.Info;

    public string DesktopId(nint hwnd) => WindowDesktop.IdOf(hwnd);

    public bool MoveToDesktop(nint hwnd, Guid desktop) => WindowDesktop.Move(hwnd, desktop);

    public void Start(Log log)
    {
        if (_started)
            return;
        _started = true;
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
            if (WinEventHook.Start(min, max, OnWinEvent) is null)
                log.Warn("windows", $"SetWinEventHook failed for events {min:x}-{max:x}; those changes will not be seen");
        }
        // The shell hook messages Explorer's taskbar gets. Only HSHELL_FLASH is read: WinEvents say nothing when a
        // window flashes.
        if (!ShellHookWindow.Start(OnShellHook))
            log.Warn("windows", "RegisterShellHookWindow failed; windows that flash will not show that they want attention");
    }

    private void OnShellHook(nint code, nint hwnd) =>
        NativeCallback.Guard(Log.Default, "windows", () =>
        {
            if (WindowRules.IsFlash(code) && hwnd != 0)
                Flashed?.Invoke(hwnd);
        });

    private void OnWinEvent(uint eventType, nint hwnd, int idObject, int idChild) =>
        NativeCallback.Guard(Log.Default, "windows", () => WinEvent?.Invoke(eventType, hwnd, idObject, idChild));

    public WindowRead? Read(nint hwnd, bool package, bool executable) => IsTaskbarWindow(hwnd) ? Describe(hwnd, package, executable) : null;

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

    private static WindowRead Describe(nint hwnd, bool package, bool executable)
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
        // The property can arrive after the window is first shown, so it is read every time.
        var property = AppUserModelIds.OfWindow(hwnd);
        string? packageAppId = null;
        if (property.Length == 0 && package)
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            packageAppId = AppUserModelIds.OfProcess(pid);
        }
        return new WindowRead(TopLevelWindows.Title(hwnd), maximized, minimized, fullscreen, device, property, packageAppId, executable ? ExePath(hwnd) : null);
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
    public void Activate(nint hwnd)
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
    public void Close(nint hwnd) => TopLevelWindows.Close(hwnd);

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
