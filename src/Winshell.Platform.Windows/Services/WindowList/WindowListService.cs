using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.WindowList;

/// <summary>
/// The <c>WindowList</c>: the other programs' windows, as the taskbar and Alt+Tab list them. Each
/// engine has its own objects, kept current from the process's <see cref="WindowTracker"/>.
/// </summary>
public sealed class WindowListService : QObject
{
    private readonly QmlEngine _engine;
    private readonly Dictionary<nint, AppWindow> _byHwnd = [];
    private AppWindow? _activeWindow;

    public WindowListService(QmlEngine engine)
    {
        _engine = engine;
        WindowTracker.EnsureStarted(engine.Loop, engine.Log);
        WindowTracker.Changed += Sync;
        Sync();
    }

    [Qml] public ObjectModel Windows { get; } = new();

    [Qml] public AppWindow? ActiveWindow { get { Track(); return _activeWindow; } }

    internal ScreenRegistry? Screens => _engine.GetService<ScreenRegistry>();

    /// <summary>The AppWindow of a window, tracked so a binding sees it appear.</summary>
    internal AppWindow? Find(nint hwnd)
    {
        // Reading the model's values makes a binding depend on them.
        _ = Windows.Values;
        return _byHwnd.GetValueOrDefault(hwnd);
    }

    private void Sync()
    {
        if (IsDestroyed)
            return;
        var windows = WindowTracker.Windows;
        foreach (var (hwnd, window) in _byHwnd.ToArray())
        {
            if (windows.ContainsKey(hwnd))
                continue;
            _byHwnd.Remove(hwnd);
            Windows.Remove(window);
            if (_activeWindow == window)
                SetField(ref _activeWindow, null, nameof(ActiveWindow));
            window.Emit("closed");
            window.Destroy();
        }
        var added = new List<AppWindow>();
        foreach (var (hwnd, info) in windows)
        {
            if (!_byHwnd.TryGetValue(hwnd, out var window))
            {
                window = new AppWindow(this, hwnd) { Engine = _engine };
                _byHwnd[hwnd] = window;
                added.Add(window);
            }
            window.Update(info);
        }
        // Owners are known once every window is.
        foreach (var (hwnd, info) in windows)
            _byHwnd[hwnd].SetParent(info.Owner == 0 ? null : _byHwnd.GetValueOrDefault(info.Owner));
        foreach (var window in added)
            Windows.Add(window);
        var active = _byHwnd.GetValueOrDefault(WindowTracker.Active);
        SetField(ref _activeWindow, active, nameof(ActiveWindow));
        foreach (var window in _byHwnd.Values)
        {
            window.SetActivated(window == active);
            window.SetAttention(WindowTracker.WantsAttention(window.Hwnd));
        }
    }

    internal void NotifyDesktopsChanged()
    {
        foreach (var window in _byHwnd.Values)
            window.NotifyDesktop();
    }

    protected override void OnDestroy()
    {
        WindowTracker.Changed -= Sync;
        foreach (var window in _byHwnd.Values)
            window.Destroy();
        _byHwnd.Clear();
    }
}

/// <summary>The <c>AppWindow</c>: another program's window.</summary>
[QmlSignal("closed")]
public sealed class AppWindow : QObject
{
    private WindowInfo? _info;
    private AppWindow? _parent;
    private bool _activated;
    private bool _attention;

    internal AppWindow(WindowListService manager, nint hwnd)
    {
        Manager = manager;
        Hwnd = hwnd;
    }

    internal WindowListService Manager { get; }

    internal nint Hwnd { get; }

    [Qml] public string Title { get { Track(); return _info?.Title ?? ""; } }

    /// <summary>The AppUserModelID the window carries, or its process package's, or its program's name in lower case.</summary>
    [Qml] public string AppId { get { Track(); return _info?.AppId ?? ""; } }

    /// <summary>The full path of the program that owns the window, or empty when it cannot be read.</summary>
    [Qml] public string Executable { get { Track(); return _info?.Executable ?? ""; } }

    /// <summary>A path the icon provider draws: a packaged app's Start icon, or the program's own icon.</summary>
    [Qml] public string Icon { get { Track(); return _info?.Icon ?? ""; } }

    /// <summary>The virtual desktop this window is on, or empty when Windows does not report one. This uses the supported desktop API.</summary>
    [Qml] public string DesktopId { get { Track(); return VirtualDesktopBroker.WindowDesktopId(Hwnd); } }

    /// <summary>The desktop in <c>VirtualDesktops</c> whose id is <see cref="DesktopId"/>, when that list is loaded.</summary>
    [Qml]
    public VirtualDesktop? Desktop
    {
        get
        {
            Track();
            var id = VirtualDesktopBroker.WindowDesktopId(Hwnd);
            return id.Length == 0 ? null : Engine?.FindNativeSingleton<VirtualDesktopsService>()?.Find(id);
        }
    }

    [Qml] public bool Activated { get { Track(); return _activated; } }

    /// <summary>
    /// Whether the window flashed to ask for attention, as a chat or a finished download does, and has not been
    /// activated since. The taskbar shows this as a highlighted button.
    /// </summary>
    [Qml] public bool Attention { get { Track(); return _attention; } }

    [Qml] public AppWindow? Parent { get { Track(); return _parent; } }

    /// <summary>The screen the window is on: the one Windows says it is nearest.</summary>
    [Qml]
    public IReadOnlyList<ShellScreen> Screens
    {
        get
        {
            Track();
            var registry = Manager.Screens;
            if (registry is null || _info is null)
                return [];
            using (DependencyTracker.Suspend())
                return registry.Find(_info.Monitor) is { } screen ? [screen] : [];
        }
    }

    [Qml]
    public bool Maximized
    {
        get { Track(); return _info?.Maximized ?? false; }
        set => WindowTracker.SetMaximized(Hwnd, value);
    }

    [Qml]
    public bool Minimized
    {
        get { Track(); return _info?.Minimized ?? false; }
        set => WindowTracker.SetMinimized(Hwnd, value);
    }

    /// <summary>Whether the window covers its screen without a frame. Windows has no request that puts another program's window into fullscreen.</summary>
    [Qml] public bool Fullscreen { get { Track(); return _info?.Fullscreen ?? false; } }

    [Qml] public void Activate() => WindowTracker.Activate(Hwnd);

    [Qml] public void Close() => WindowTracker.Close(Hwnd);

    /// <summary>Moves the window onto a monitor. Its logical size stays, and a maximized window is maximized again there.</summary>
    [Qml] public void MoveTo(ShellScreen? screen) => WindowTracker.MoveToMonitor(Hwnd, screen?.Id ?? "");

    /// <summary>Moves the window onto a virtual desktop. This uses the supported desktop API.</summary>
    [Qml]
    public void MoveToDesktop(VirtualDesktop? desktop)
    {
        if (desktop is null || !Guid.TryParse(desktop.Id, out var id) || id == Guid.Empty)
            return;
        if (!VirtualDesktopBroker.MoveWindow(Hwnd, id))
            return;
        NotifyChanged("desktopId");
        NotifyChanged("desktop");
    }

    internal void NotifyDesktop()
    {
        NotifyChanged("desktopId");
        NotifyChanged("desktop");
    }

    internal void Update(WindowInfo info)
    {
        var old = _info;
        _info = info;
        if (old is null)
            return;
        if (old.Title != info.Title) NotifyChanged("title");
        if (old.AppId != info.AppId) NotifyChanged("appId");
        if (old.Executable != info.Executable) NotifyChanged("executable");
        if (old.Icon != info.Icon) NotifyChanged("icon");
        if (old.Maximized != info.Maximized) NotifyChanged("maximized");
        if (old.Minimized != info.Minimized) NotifyChanged("minimized");
        if (old.Fullscreen != info.Fullscreen) NotifyChanged("fullscreen");
        if (old.Monitor != info.Monitor) NotifyChanged("screens");
    }

    internal void SetParent(AppWindow? parent) => SetField(ref _parent, parent, nameof(Parent));

    internal void SetActivated(bool activated) => SetField(ref _activated, activated, nameof(Activated));

    internal void SetAttention(bool attention) => SetField(ref _attention, attention, nameof(Attention));

    public override string ToString() => $"AppWindow({_info?.AppId}: {_info?.Title})";
}
