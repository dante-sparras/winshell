using System.Collections.ObjectModel;

namespace Winshell.Windows;

/// <summary>The other programs' windows, as the taskbar and Alt+Tab list them.</summary>
public sealed class WindowListService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly IWindowSource _source;
    private readonly VirtualDesktopsService? _desktops;
    private readonly Dictionary<nint, AppWindow> _byHwnd = [];
    private AppWindow? _activeWindow;
    private bool _disposed;

    /// <param name="loop">The loop of the thread the windows are read on.</param>
    /// <param name="log">Where a hook that cannot be set is reported.</param>
    /// <param name="desktops">Gives <see cref="AppWindow.Desktop"/> its value. Without it, that stays null.</param>
    /// <param name="source">The windows, for a test.</param>
    internal WindowListService(IEventLoop loop, Log log, VirtualDesktopsService? desktops = null, IWindowSource? source = null)
    {
        _loop = loop;
        _desktops = desktops;
        _source = source ?? TrackedWindows.Instance;
        _source.Start(loop, log);
        _source.Changed += OnChanged;
        if (_desktops is not null)
            _desktops.Refreshed += OnDesktopsRefreshed;
        Apply();
    }

    public ObservableCollection<AppWindow> Windows { get; } = [];

    public AppWindow? ActiveWindow => _activeWindow;

    private void OnChanged() => _loop.Post(Apply);

    private void Apply()
    {
        if (_disposed)
            return;
        var windows = _source.Windows;
        foreach (var (hwnd, window) in _byHwnd.ToArray())
        {
            if (windows.ContainsKey(hwnd))
                continue;
            _byHwnd.Remove(hwnd);
            Windows.Remove(window);
            if (_activeWindow == window)
                Set(ref _activeWindow, null, nameof(ActiveWindow));
            window.NotifyClosed();
        }
        var added = new List<AppWindow>();
        foreach (var (hwnd, info) in windows)
        {
            if (_byHwnd.TryGetValue(hwnd, out var window))
            {
                window.Update(info);
                continue;
            }
            window = new AppWindow(info, _source, _desktops);
            _byHwnd[hwnd] = window;
            added.Add(window);
        }
        // Owners are known once every window is.
        foreach (var (hwnd, info) in windows)
            _byHwnd[hwnd].SetParent(info.Owner == 0 ? null : _byHwnd.GetValueOrDefault(info.Owner));
        // A new window goes at the end, so the list stays in the order windows opened.
        foreach (var window in added)
            Windows.Add(window);
        var active = _byHwnd.GetValueOrDefault(_source.Active);
        Set(ref _activeWindow, active, nameof(ActiveWindow));
        foreach (var window in _byHwnd.Values)
        {
            window.SetActivated(window == active);
            window.SetAttention(_source.WantsAttention(window.Hwnd));
        }
    }

    private void OnDesktopsRefreshed()
    {
        foreach (var window in _byHwnd.Values)
            window.NotifyDesktop();
    }

    public void Dispose()
    {
        _disposed = true;
        _source.Changed -= OnChanged;
        if (_desktops is not null)
            _desktops.Refreshed -= OnDesktopsRefreshed;
    }
}

/// <summary>A top-level window of another program.</summary>
public sealed class AppWindow : ObservableObject
{
    private readonly IWindowSource _source;
    private readonly VirtualDesktopsService? _desktops;
    private WindowInfo _info;
    private AppWindow? _parent;
    private bool _activated;
    private bool _attention;

    internal AppWindow(WindowInfo info, IWindowSource source, VirtualDesktopsService? desktops)
    {
        _info = info;
        _source = source;
        _desktops = desktops;
    }

    internal nint Hwnd => _info.Hwnd;

    public string Title => _info.Title;

    /// <summary>The AppUserModelID the window carries, or its process package's, or its program's name in lower case.</summary>
    public string AppId => _info.AppId;

    /// <summary>The full path of the program that owns the window, or empty when it cannot be read.</summary>
    public string Executable => _info.Executable;

    /// <summary>A path an icon is read from: a packaged app's Start icon, or the program's own icon.</summary>
    public string Icon => _info.Icon;

    /// <summary>The virtual desktop this window is on, or empty when Windows does not report one. This uses the supported desktop API.</summary>
    public string DesktopId => _source.DesktopId(Hwnd);

    /// <summary>The desktop in <see cref="VirtualDesktopsService.Desktops"/> whose id is <see cref="DesktopId"/>, when that list is loaded.</summary>
    public VirtualDesktop? Desktop => _desktops?.Find(DesktopId);

    public bool Activated => _activated;

    /// <summary>
    /// Whether the window flashed to ask for attention, as a chat or a finished download does, and has not been
    /// activated since. The taskbar shows this as a highlighted button.
    /// </summary>
    public bool Attention => _attention;

    public AppWindow? Parent => _parent;

    /// <summary>The screen the window is on: the one Windows says it is nearest.</summary>
    public IReadOnlyList<ScreenInfo> Screens => _source.Screen(_info.Monitor) is { } screen ? [screen] : [];

    public bool Maximized
    {
        get => _info.Maximized;
        set => _source.SetMaximized(Hwnd, value);
    }

    public bool Minimized
    {
        get => _info.Minimized;
        set => _source.SetMinimized(Hwnd, value);
    }

    /// <summary>Whether the window covers its screen without a frame. Windows has no request that puts another program's window into fullscreen.</summary>
    public bool Fullscreen => _info.Fullscreen;

    /// <summary>The window closed. This comes after it has left <see cref="WindowListService.Windows"/>.</summary>
    public event EventHandler? Closed;

    public void Activate() => _source.Activate(Hwnd);

    public void Close() => _source.Close(Hwnd);

    /// <summary>Moves the window onto a monitor. Its logical size stays, and a maximized window is maximized again there.</summary>
    public void MoveTo(ScreenInfo? screen) => _source.MoveToMonitor(Hwnd, screen?.Id ?? "");

    /// <summary>Moves the window onto a virtual desktop. This uses the supported desktop API.</summary>
    public void MoveToDesktop(VirtualDesktop? desktop)
    {
        if (desktop is null || !Guid.TryParse(desktop.Id, out var id) || id == Guid.Empty)
            return;
        if (_source.MoveToDesktop(Hwnd, id))
            NotifyDesktop();
    }

    internal void NotifyDesktop()
    {
        Changed(nameof(DesktopId));
        Changed(nameof(Desktop));
    }

    internal void NotifyClosed() => Closed?.Invoke(this, EventArgs.Empty);

    internal void Update(WindowInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Title != info.Title, nameof(Title));
        Changed(old.AppId != info.AppId, nameof(AppId));
        Changed(old.Executable != info.Executable, nameof(Executable));
        Changed(old.Icon != info.Icon, nameof(Icon));
        Changed(old.Maximized != info.Maximized, nameof(Maximized));
        Changed(old.Minimized != info.Minimized, nameof(Minimized));
        Changed(old.Fullscreen != info.Fullscreen, nameof(Fullscreen));
        Changed(old.Monitor != info.Monitor, nameof(Screens));
    }

    internal void SetParent(AppWindow? parent) => Set(ref _parent, parent, nameof(Parent));

    internal void SetActivated(bool activated) => Set(ref _activated, activated, nameof(Activated));

    internal void SetAttention(bool attention) => Set(ref _attention, attention, nameof(Attention));

    public override string ToString() => $"AppWindow({_info.AppId}: {_info.Title})";
}
