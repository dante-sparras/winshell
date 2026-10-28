using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.Unavailable;

/// <summary>Quickshell modules that have no Windows counterpart. The types load and stay idle.</summary>
internal static class UnavailableModules
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Quickshell.Services.Greetd")
            .Singleton(e => new GreetdService(e), "Greetd")
            .Enum(static (GreetdState s) => s.ToString());

        registry.Module("Quickshell.Services.Polkit")
            .Type<PolkitAgent>("PolkitAgent")
            .Uncreatable<AuthFlow>("AuthFlow");

        registry.Module("Quickshell.I3")
            .Singleton(e => new I3Service(e), "I3")
            .Uncreatable<I3Monitor>("I3Monitor")
            .Uncreatable<I3Workspace>("I3Workspace")
            .Uncreatable<I3Event>("I3Event")
            .Type<I3IpcListener>("I3IpcListener");

        registry.Module("Quickshell.DBusMenu")
            .Uncreatable<DBusMenuHandle>("DBusMenuHandle")
            .Uncreatable<DBusMenuItem>("DBusMenuItem");

        registry.Module("Quickshell.WindowManager")
            .Singleton(e => new WindowManagerService(e), "WindowManager")
            .Uncreatable<Windowset>("Windowset")
            .Uncreatable<WindowsetProjection>("WindowsetProjection")
            .Uncreatable<ScreenProjection>("ScreenProjection");
    }

    public static void RegisterWayland(QmlModule wayland) =>
        wayland.Type<ScreencopyView>("ScreencopyView")
            .Type<ShortcutInhibitor>("ShortcutInhibitor");
}

public enum GreetdState
{
    Inactive = 0,
    Authenticating = 1,
    ReadyToLaunch = 2,
    Launching = 3,
    Launched = 4,
}

/// <summary>Quickshell's <c>Greetd</c>. Windows has no greetd socket.</summary>
[QmlSignal("authMessage", "message", "error", "responseRequired", "echoResponse")]
[QmlSignal("authFailure", "message")]
[QmlSignal("readyToLaunch")]
[QmlSignal("launched")]
[QmlSignal("error", "error")]
public sealed class GreetdService : QObject
{
    public GreetdService(QmlEngine engine)
    {
        Engine = engine;
        engine.Log.Warn("quickshell.service.greetd", "Windows has no greetd socket, so Greetd stays inactive.");
    }

    [Qml] public bool Available { get { Track(); return false; } }

    [Qml] public int State { get { Track(); return (int)GreetdState.Inactive; } }

    [Qml] public string User { get { Track(); return ""; } }

    [Qml] public void CreateSession(string user) => _ = user;

    [Qml] public void CancelSession() { }

    [Qml] public void Respond(string response) => _ = response;

    [Qml]
    public void Launch(IReadOnlyList<string>? command, IReadOnlyList<string>? environment = null, bool quit = true)
    {
        _ = command;
        _ = environment;
        _ = quit;
    }
}

/// <summary>Quickshell's <c>AuthFlow</c>. No request is created, because Windows cannot host a polkit agent.</summary>
public sealed class AuthFlow : QObject, IRetainable
{
    RetainableHook? IRetainable.RetainHook { get; set; }

    void IRetainable.FinishRetain() => Destroy();

    [Qml] public string Cookie => "";

    [Qml] public bool Failed => false;

    [Qml] public string ActionId => "";

    [Qml] public IReadOnlyList<object?> Identities => [];

    [Qml] public bool IsResponseRequired => false;

    [Qml] public string SupplementaryMessage => "";

    [Qml] public bool ResponseVisible => false;

    [Qml] public bool IsCancelled => false;

    [Qml] public string InputPrompt => "";

    [Qml] public bool IsCompleted => false;

    [Qml] public string IconName => "";

    [Qml] public bool IsSuccessful => false;

    [Qml] public string Message => "";

    [Qml] public object? SelectedIdentity { get; set; }

    [Qml] public bool SupplementaryIsError => false;

    [Qml] public void CancelAuthenticationRequest() { }

    [Qml] public void Submit(string value) => _ = value;
}

/// <summary>Quickshell's <c>PolkitAgent</c>. Windows authentication is not a polkit agent.</summary>
[QmlSignal("authenticationRequestStarted")]
public sealed class PolkitAgent : QObject
{
    private string _path = "/org/quickshell/Polkit";

    [Qml] public string Path { get { Track(); return _path; } set => SetField(ref _path, string.IsNullOrEmpty(value) ? "/org/quickshell/Polkit" : value); }

    [Qml] public bool IsRegistered { get { Track(); return false; } }

    [Qml] public bool IsActive { get { Track(); return false; } }

    [Qml] public AuthFlow? Flow { get { Track(); return null; } }

    public override void ComponentComplete() =>
        (Engine?.Log ?? Log.Default).Warn("quickshell.service.polkit", "Windows cannot register a polkit agent, so PolkitAgent stays idle.");
}

/// <summary>Quickshell's <c>I3</c>. There is no i3 socket on Windows.</summary>
[QmlSignal("connected")]
[QmlSignal("rawEvent", "event")]
public sealed class I3Service : QObject
{
    public I3Service(QmlEngine engine)
    {
        Engine = engine;
        Monitors = new ObjectModel { Engine = engine };
        Workspaces = new ObjectModel { Engine = engine };
        engine.Log.Warn("quickshell.service.i3", "Windows has no i3 socket, so I3 stays empty.");
    }

    [Qml] public ObjectModel Monitors { get; }

    [Qml] public ObjectModel Workspaces { get; }

    [Qml] public I3Monitor? FocusedMonitor { get { Track(); return null; } }

    [Qml] public I3Workspace? FocusedWorkspace { get { Track(); return null; } }

    [Qml] public string SocketPath { get { Track(); return ""; } }

    [Qml] public void Dispatch(string request) => _ = request;

    [Qml] public I3Monitor? FindMonitorByName(string name) { _ = name; return null; }

    [Qml] public I3Workspace? FindWorkspaceByName(string name) { _ = name; return null; }

    [Qml] public I3Monitor? MonitorFor(QObject? screen) { _ = screen; return null; }

    [Qml] public void RefreshMonitors() { }

    [Qml] public void RefreshWorkspaces() { }
}

public sealed class I3Monitor : QObject
{
    [Qml] public int Id => 0;

    [Qml] public string Name => "";

    [Qml] public int X => 0;

    [Qml] public int Y => 0;

    [Qml] public int Width => 0;

    [Qml] public int Height => 0;

    [Qml] public double Scale => 1;

    [Qml] public bool Focused => false;

    [Qml] public bool Power => false;

    [Qml] public I3Workspace? ActiveWorkspace => null;

    [Qml] public I3Workspace? FocusedWorkspace => null;
}

public sealed class I3Workspace : QObject
{
    [Qml] public int Id => 0;

    [Qml] public string Name => "";

    [Qml] public int Number => 0;

    [Qml] public int Num => 0;

    [Qml] public bool Active => false;

    [Qml] public bool Focused => false;

    [Qml] public bool Urgent => false;

    [Qml] public I3Monitor? Monitor => null;

    [Qml] public object? LastIpcObject => null;

    [Qml] public void Activate() { }
}

public sealed class I3Event : QObject
{
    [Qml] public string Type => "";

    [Qml] public string Data => "";
}

/// <summary>Quickshell's <c>I3IpcListener</c>. Events never arrive without an i3 socket.</summary>
[QmlSignal("ipcEvent", "event")]
public sealed class I3IpcListener : QObject
{
    private string[] _subscriptions = [];

    [Qml]
    public string[] Subscriptions
    {
        get { Track(); return _subscriptions; }
        set => SetField(ref _subscriptions, value ?? []);
    }
}

public sealed class DBusMenuHandle : QsMenuHandle
{
    [Qml] public DBusMenuItem? Menu => null;
}

/// <summary>Quickshell's <c>DBusMenuItem</c>. Windows menus are not DBus menus.</summary>
[QmlSignal("layoutUpdated")]
public sealed class DBusMenuItem : QsMenuEntry
{
    [Qml] public DBusMenuHandle? MenuHandle => null;

    [Qml]
    public void UpdateLayout() =>
        (Engine?.Log ?? Log.Default).Warn("quickshell.dbusmenu", "Windows menus are not DBus menus, so there is no layout to refresh.");
}

/// <summary>Quickshell's <c>WindowManager</c>. Windows has no ext-workspace protocol, so no window sets are published.</summary>
public sealed class WindowManagerService : QObject
{
    private readonly Dictionary<ShellScreen, ScreenProjection> _byScreen = [];

    public WindowManagerService(QmlEngine engine)
    {
        Engine = engine;
        engine.Log.Warn("quickshell.wm", "Windows has no ext-workspace window sets, so WindowManager stays empty.");
    }

    [Qml] public IReadOnlyList<Windowset> Windowsets { get { Track(); return []; } }

    [Qml] public IReadOnlyList<WindowsetProjection> WindowsetProjections { get { Track(); return []; } }

    [Qml("screenProjection")]
    public ScreenProjection ProjectionFor(ShellScreen? screen)
    {
        if (screen is null)
            return new ScreenProjection(null) { Engine = Engine };
        if (_byScreen.TryGetValue(screen, out var found))
            return found;
        var projection = new ScreenProjection(screen) { Engine = Engine };
        projection.SetOwner(this);
        _byScreen[screen] = projection;
        return projection;
    }
}

public class WindowsetProjection : QObject
{
    internal WindowsetProjection(IReadOnlyList<ShellScreen> screens) => Screens = screens;

    [Qml] public IReadOnlyList<ShellScreen> Screens { get; }

    [Qml] public IReadOnlyList<Windowset> Windowsets { get { Track(); return []; } }
}

public sealed class ScreenProjection : WindowsetProjection
{
    internal ScreenProjection(ShellScreen? screen)
        : base(screen is null ? [] : [screen])
    {
    }
}

public sealed class Windowset : QObject
{
    [Qml] public string Id => "";

    [Qml] public string Name => "";

    [Qml] public int[] Coordinates => [];

    [Qml] public bool Active => false;

    [Qml] public WindowsetProjection? Projection => null;

    [Qml] public bool ShouldDisplay => false;

    [Qml] public bool Urgent => false;

    [Qml] public bool CanActivate => false;

    [Qml] public bool CanDeactivate => false;

    [Qml] public bool CanRemove => false;

    [Qml] public bool CanSetProjection => false;

    [Qml] public void Activate() { }

    [Qml] public void Deactivate() { }

    [Qml] public void Remove() { }

    [Qml] public void SetProjection(WindowsetProjection? projection) => _ = projection;
}

/// <summary>Quickshell's <c>ScreencopyView</c>. Capture from a compositor protocol is not available here.</summary>
[QmlSignal("stopped")]
public sealed class ScreencopyView : Item
{
    private QObject? _captureSource;
    private bool _live;
    private bool _paintCursor;
    private QSize _constraintSize;

    [Qml] public QObject? CaptureSource { get { Track(); return _captureSource; } set => SetField(ref _captureSource, value); }

    [Qml] public bool Live { get { Track(); return _live; } set => SetField(ref _live, value); }

    [Qml] public bool PaintCursor { get { Track(); return _paintCursor; } set => SetField(ref _paintCursor, value); }

    [Qml] public bool HasContent { get { Track(); return false; } }

    [Qml] public QSize SourceSize { get { Track(); return new QSize(0, 0); } }

    [Qml] public QSize ConstraintSize { get { Track(); return _constraintSize; } set => SetField(ref _constraintSize, value); }

    [Qml]
    public void CaptureFrame() =>
        (Engine?.Log ?? Log.Default).Warn("quickshell.wayland.screencopy", "Screencopy needs a Wayland capture protocol, so CaptureFrame does nothing on Windows.");
}

/// <summary>Quickshell's <c>ShortcutInhibitor</c>. Windows does not grant this inhibit request.</summary>
public sealed class ShortcutInhibitor : QObject
{
    private bool _enabled;
    private QObject? _window;

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (!SetField(ref _enabled, value) || !value)
                return;
            (Engine?.Log ?? Log.Default).Warn("quickshell.wayland.shortcuts", "Windows does not let the shell inhibit compositor shortcuts, so ShortcutInhibitor stays inactive.");
        }
    }

    [Qml] public QObject? Window { get { Track(); return _window; } set => SetField(ref _window, value); }

    [Qml] public bool Active { get { Track(); return false; } }
}
