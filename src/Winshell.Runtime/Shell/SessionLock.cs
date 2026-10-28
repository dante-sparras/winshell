namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>SessionLock</c>: while <c>locked</c>, one <c>surface</c> covers each screen. On Windows the
/// platform keeps those surfaces in front and the session out of reach, and locks Windows itself if the shell dies
/// while locked.
/// </summary>
[QmlDefaultProperty("surface")]
public sealed class SessionLock : Reloadable
{
    /// <summary>The lock that holds the session; only one may at a time.</summary>
    private static SessionLock? _current;

    private QmlComponent? _surface;
    private bool _locked;
    private bool _secure;
    private bool _active;
    private ScreenRegistry? _screens;
    private readonly Dictionary<ShellScreen, SessionLockSurface> _surfaces = [];

    [Qml]
    public QmlComponent? Surface
    {
        get { Track(); return _surface; }
        set
        {
            if (SetField(ref _surface, value) && _active)
            {
                DestroySurfaces();
                SyncSurfaces();
            }
        }
    }

    [Qml]
    public bool Locked
    {
        get { Track(); return _locked; }
        set
        {
            if (SetField(ref _locked, value) && ReloadDone)
                Apply();
        }
    }

    [Qml] public bool Secure { get { Track(); return _secure; } }

    protected override void OnReload(QObject? oldInstance)
    {
        // The new shell's lock takes over from the old one, so a reload never unlocks the session.
        if (oldInstance is SessionLock { _active: true } old && _current == old)
        {
            old._active = false;
            _current = this;
            _active = true;
        }
        base.OnReload(oldInstance);
        Apply();
    }

    private void Apply()
    {
        if (Engine is null)
            return;
        if (_locked && !_active)
        {
            if (_current is not null && _current != this && !_current.IsDestroyed)
            {
                Engine.Log.Warn("sessionlock", "Another SessionLock is already locked");
                return;
            }
            _current = this;
            _active = true;
        }
        if (_active && !_locked)
        {
            Unlock();
            return;
        }
        if (!_active)
            return;
        Engine.GetService<IShellPlatform>()?.SetSessionLocked(true);
        if (_screens is null)
        {
            _screens = Engine.GetService<ScreenRegistry>();
            _screens?.AddObserver("screens", ScreensObserver.Instance);
        }
        SyncSurfaces();
    }

    internal void SyncSurfaces()
    {
        if (!_active || _surface is null || Engine is null)
            return;
        var screens = _screens?.Screens ?? [];
        foreach (var (screen, surface) in _surfaces.ToArray())
        {
            if (!screens.Contains(screen))
            {
                _surfaces.Remove(screen);
                surface.Destroy();
            }
        }
        foreach (var screen in screens)
        {
            if (_surfaces.ContainsKey(screen))
                continue;
            QObject made;
            try
            {
                made = _surface.Create(null, null, null, o =>
                {
                    o.SetOwner(this);
                    if (o is SessionLockSurface s)
                        s.LockScreen = screen;
                });
            }
            catch (Exception e) when (e is QmlException or QmlCompileException)
            {
                Engine.Log.Warn("sessionlock", e.Message);
                continue;
            }
            if (made is not SessionLockSurface surface)
            {
                Engine.Log.Warn("sessionlock", "SessionLock.surface must create a SessionLockSurface");
                made.Destroy();
                continue;
            }
            _surfaces[screen] = surface;
        }
        // Every screen is covered once the surfaces are shown, which happens on the next turn of the loop.
        Engine.Loop.Post(() =>
        {
            var secure = _active && _surfaces.Count > 0 && _surfaces.Values.All(s => s.BackingWindowVisible);
            SetField(ref _secure, secure, "secure");
        });
    }

    private void Unlock()
    {
        _active = false;
        if (_current == this)
            _current = null;
        DestroySurfaces();
        SetField(ref _secure, false, "secure");
        Engine?.GetService<IShellPlatform>()?.SetSessionLocked(false);
    }

    private void DestroySurfaces()
    {
        foreach (var surface in _surfaces.Values)
            surface.Destroy();
        _surfaces.Clear();
    }

    protected override void OnDestroy()
    {
        _screens?.RemoveObserver("screens", ScreensObserver.Instance);
        // Destroyed while locked and not taken over by a reload: the session stays locked.
        if (_active)
            DestroySurfaces();
        base.OnDestroy();
    }

    private sealed class ScreensObserver : IPropertyObserver
    {
        public static readonly ScreensObserver Instance = new();

        public void OnPropertyChanged(QObject sender, string property) => _current?.SyncSurfaces();
    }
}

/// <summary>The <c>SessionLockSurface</c>: the lock's window on one screen, covering all of it.</summary>
public sealed class SessionLockSurface : ShellWindow
{
    public SessionLockSurface() => Color = QColor.White;

    protected override WindowKind Kind => WindowKind.Panel;

    internal ShellScreen? LockScreen { get; set; }

    [Qml]
    public override ShellScreen? Screen
    {
        get { Track("screen"); return LockScreen; }
        set { }
    }

    [Qml]
    public override bool Visible
    {
        get { Track(); return BackingWindowVisible; }
        set { }
    }

    protected override void OnPlacementChanged()
    {
        if (Backend is null)
            return;
        using var _ = DependencyTracker.Suspend();
        Backend.SetPanel(new PanelPlacement(
            LockScreen?.Info,
            Edges.Top | Edges.Left | Edges.Right | Edges.Bottom,
            0, 0, 0, 0,
            ImplicitWidth,
            ImplicitHeight,
            ExclusionMode: 1,
            ExclusiveZone: 0,
            AboveWindows: true,
            Focusable: true,
            Namespace: "winshell-lock"));
    }

    protected override void OnBackendCreated()
    {
        base.OnBackendCreated();
        Backend?.Raise();
        Backend?.SetKeyboardFocus(true);
    }
}
