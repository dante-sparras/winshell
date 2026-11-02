namespace Winshell.Windows;

/// <summary>
/// The windows a <see cref="WindowListService"/> follows, and what it asks of them. Tests use a stand-in, so no real
/// window moves. The shell uses <see cref="TrackedWindows"/>.
/// </summary>
internal interface IWindowSource
{
    IReadOnlyDictionary<nint, WindowInfo> Windows { get; }

    /// <summary>The listed window that has the foreground. Zero when none.</summary>
    nint Active { get; }

    event Action? Changed;

    void Start(IEventLoop loop, Log log);

    bool WantsAttention(nint hwnd);

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

/// <summary>The process's <see cref="WindowTracker"/>, and the supported desktop API.</summary>
internal sealed class TrackedWindows : IWindowSource
{
    public static readonly TrackedWindows Instance = new();

    public IReadOnlyDictionary<nint, WindowInfo> Windows => WindowTracker.Windows;

    public nint Active => WindowTracker.Active;

    public event Action? Changed
    {
        add => WindowTracker.Changed += value;
        remove => WindowTracker.Changed -= value;
    }

    public void Start(IEventLoop loop, Log log) => WindowTracker.EnsureStarted(loop, log);

    public bool WantsAttention(nint hwnd) => WindowTracker.WantsAttention(hwnd);

    public ScreenInfo? Screen(string deviceName) => Screens.Find(deviceName)?.Info;

    public string DesktopId(nint hwnd) => VirtualDesktopBroker.WindowDesktopId(hwnd);

    public bool MoveToDesktop(nint hwnd, Guid desktop) => VirtualDesktopBroker.MoveWindow(hwnd, desktop);

    public void Activate(nint hwnd) => WindowTracker.Activate(hwnd);

    public void Close(nint hwnd) => WindowTracker.Close(hwnd);

    public void SetMaximized(nint hwnd, bool maximized) => WindowTracker.SetMaximized(hwnd, maximized);

    public void SetMinimized(nint hwnd, bool minimized) => WindowTracker.SetMinimized(hwnd, minimized);

    public void MoveToMonitor(nint hwnd, string deviceName) => WindowTracker.MoveToMonitor(hwnd, deviceName);
}
