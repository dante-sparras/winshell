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
    private AppWindow? _activeToplevel;

    public WindowListService(QmlEngine engine)
    {
        _engine = engine;
        WindowTracker.EnsureStarted(engine.Loop, engine.Log);
        WindowTracker.Changed += Sync;
        Sync();
    }

    [Qml] public ObjectModel Toplevels { get; } = new();

    [Qml] public AppWindow? ActiveToplevel { get { Track(); return _activeToplevel; } }

    internal ScreenRegistry? Screens => _engine.GetService<ScreenRegistry>();

    /// <summary>The AppWindow of a window, tracked so a binding sees it appear.</summary>
    internal AppWindow? Find(nint hwnd)
    {
        // Reading the model's values makes a binding depend on them.
        _ = Toplevels.Values;
        return _byHwnd.GetValueOrDefault(hwnd);
    }

    private void Sync()
    {
        if (IsDestroyed)
            return;
        var windows = WindowTracker.Windows;
        foreach (var (hwnd, toplevel) in _byHwnd.ToArray())
        {
            if (windows.ContainsKey(hwnd))
                continue;
            _byHwnd.Remove(hwnd);
            Toplevels.Remove(toplevel);
            if (_activeToplevel == toplevel)
                SetField(ref _activeToplevel, null, nameof(ActiveToplevel));
            toplevel.Emit("closed");
            toplevel.Destroy();
        }
        var added = new List<AppWindow>();
        foreach (var (hwnd, info) in windows)
        {
            if (!_byHwnd.TryGetValue(hwnd, out var toplevel))
            {
                toplevel = new AppWindow(this, hwnd);
                _byHwnd[hwnd] = toplevel;
                added.Add(toplevel);
            }
            toplevel.Update(info);
        }
        // Owners are known once every window is.
        foreach (var (hwnd, info) in windows)
            _byHwnd[hwnd].SetParent(info.Owner == 0 ? null : _byHwnd.GetValueOrDefault(info.Owner));
        foreach (var toplevel in added)
            Toplevels.Add(toplevel);
        var active = _byHwnd.GetValueOrDefault(WindowTracker.Active);
        SetField(ref _activeToplevel, active, nameof(ActiveToplevel));
        foreach (var toplevel in _byHwnd.Values)
            toplevel.SetActivated(toplevel == active);
    }

    protected override void OnDestroy()
    {
        WindowTracker.Changed -= Sync;
        foreach (var toplevel in _byHwnd.Values)
            toplevel.Destroy();
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

    internal AppWindow(WindowListService manager, nint hwnd)
    {
        Manager = manager;
        Hwnd = hwnd;
    }

    internal WindowListService Manager { get; }

    internal nint Hwnd { get; }

    [Qml] public string Title { get { Track(); return _info?.Title ?? ""; } }

    /// <summary>The AppUserModelID the window carries, or its program's name in lower case.</summary>
    [Qml] public string AppId { get { Track(); return _info?.AppId ?? ""; } }

    [Qml] public bool Activated { get { Track(); return _activated; } }

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

    /// <summary>Whether the window covers its screen without a frame. Windows has no way to ask a window to go fullscreen, so setting it does nothing.</summary>
    [Qml]
    public bool Fullscreen
    {
        get { Track(); return _info?.Fullscreen ?? false; }
        set => _ = value;
    }

    [Qml] public void Activate() => WindowTracker.Activate(Hwnd);

    [Qml] public void Close() => WindowTracker.Close(Hwnd);

    /// <summary>Windows has no way to ask a window to go fullscreen; ignored, as a compositor may.</summary>
    [Qml]
    public void FullscreenOn(ShellScreen? screen) => _ = screen;

    /// <summary>A hint for minimize animations. Windows takes it from the taskbar button; ignored.</summary>
    [Qml]
    public void SetRectangle(QObject? window, object? rect) => _ = (window, rect);

    [Qml]
    public void UnsetRectangle()
    {
    }

    internal void Update(WindowInfo info)
    {
        var old = _info;
        _info = info;
        if (old is null)
            return;
        if (old.Title != info.Title) NotifyChanged("title");
        if (old.AppId != info.AppId) NotifyChanged("appId");
        if (old.Maximized != info.Maximized) NotifyChanged("maximized");
        if (old.Minimized != info.Minimized) NotifyChanged("minimized");
        if (old.Fullscreen != info.Fullscreen) NotifyChanged("fullscreen");
        if (old.Monitor != info.Monitor) NotifyChanged("screens");
    }

    internal void SetParent(AppWindow? parent) => SetField(ref _parent, parent, nameof(Parent));

    internal void SetActivated(bool activated) => SetField(ref _activated, activated, nameof(Activated));

    public override string ToString() => $"AppWindow({_info?.AppId}: {_info?.Title})";
}
