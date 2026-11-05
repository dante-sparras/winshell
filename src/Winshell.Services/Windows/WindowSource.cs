namespace Winshell.Windows;

/// <summary>
/// The windows a <see cref="WindowListService"/> follows, and what it asks of them. Tests use a stand-in, so no real
/// window moves. The shell uses <see cref="WindowTracker"/>.
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
