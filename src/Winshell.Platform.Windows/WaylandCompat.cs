using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows;

/// <summary>
/// The <c>WlrLayershell</c> attached object of a PanelWindow. On Windows, the layer decides whether the panel sits
/// above normal windows or under them, and keyboard focus decides whether it can take focus.
/// </summary>
public sealed class WlrLayershellAttached(QObject owner) : QObject
{
    private int _layer = 2;
    private string _namespace = "quickshell";
    private int _keyboardFocus;

    private PanelWindow? Panel => owner as PanelWindow;

    [Qml]
    public int Layer
    {
        get { Track(); return _layer; }
        set
        {
            if (!SetField(ref _layer, value) || Panel is not { } p)
                return;
            p.Layer = value;
            p.AboveWindows = value >= 2;
        }
    }

    [Qml]
    public string Namespace
    {
        get { Track(); return _namespace; }
        set
        {
            if (SetField(ref _namespace, value ?? "") && Panel is { } p)
                p.Namespace = _namespace;
        }
    }

    [Qml]
    public int KeyboardFocus
    {
        get { Track(); return _keyboardFocus; }
        set
        {
            if (SetField(ref _keyboardFocus, value) && Panel is { } p)
                p.Focusable = value != 0;
        }
    }
}

/// <summary>
/// The <c>BackgroundEffect</c> attached object: a blur region behind the window. On Windows 11 a blurred
/// background is the Acrylic material.
/// </summary>
public sealed class BackgroundEffectAttached(QObject owner) : QObject
{
    private Region? _blurRegion;

    [Qml]
    public Region? BlurRegion
    {
        get { Track(); return _blurRegion; }
        set
        {
            if (!SetField(ref _blurRegion, value))
                return;
            if (owner is QsWindow window)
                window.Backdrop = value is null ? WindowBackdrop.None : WindowBackdrop.Acrylic;
        }
    }
}

/// <summary>Quickshell's <c>IdleInhibitor</c>: keeps the screen on and the PC awake while enabled.</summary>
public sealed class IdleInhibitor : QObject
{
    private bool _enabled;
    private QsWindow? _window;
    private static int _active;

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (!SetField(ref _enabled, value))
                return;
            _active += value ? 1 : -1;
            Apply();
        }
    }

    [Qml]
    public QsWindow? Window { get { Track(); return _window; } set => SetField(ref _window, value); }

    private static void Apply()
    {
        // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED while any inhibitor is on.
        Win32.SetThreadExecutionState(_active > 0 ? 0x80000003 : 0x80000000);
    }

    internal static bool AnyActive => _active > 0;

    protected override void OnDestroy()
    {
        if (_enabled)
        {
            _active--;
            Apply();
        }
    }
}

/// <summary>Quickshell's <c>IdleMonitor</c>: tells when there has been no input for a while.</summary>
public sealed class IdleMonitor : QObject
{
    private bool _enabled = true;
    private double _timeout;
    private bool _respectInhibitors = true;
    private bool _isIdle;
    private IDisposable? _timer;

    [Qml] public bool Enabled { get { Track(); return _enabled; } set { if (SetField(ref _enabled, value)) Schedule(); } }

    [Qml] public double Timeout { get { Track(); return _timeout; } set { if (SetField(ref _timeout, value)) Schedule(); } }

    [Qml] public bool RespectInhibitors { get { Track(); return _respectInhibitors; } set => SetField(ref _respectInhibitors, value); }

    [Qml] public bool IsIdle { get { Track(); return _isIdle; } }

    public override void ComponentComplete() => Schedule();

    private static double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = 8 };
        if (!Win32.GetLastInputInfo(ref info))
            return 0;
        return unchecked(Win32.GetTickCount() - info.dwTime) / 1000.0;
    }

    private void Schedule()
    {
        _timer?.Dispose();
        if (!IsComplete || Engine is null || IsDestroyed)
            return;
        Check();
    }

    private void Check()
    {
        if (IsDestroyed || Engine is null)
            return;
        var idle = _enabled && _timeout > 0 && IdleSeconds() >= _timeout && !(_respectInhibitors && IdleInhibitor.AnyActive);
        SetField(ref _isIdle, idle, nameof(IsIdle));
        // Poll faster while idle, so the end of idleness shows quickly.
        var next = idle ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(Math.Clamp(_timeout - IdleSeconds(), 0.25, 5));
        _timer = Engine.Loop.Schedule(next, Check);
    }

    protected override void OnDestroy() => _timer?.Dispose();
}

internal static class WaylandCompat
{
    public static void Register(ModuleRegistry registry)
    {
        var wayland = registry.Module("Quickshell.Wayland");
        wayland.Uncreatable<WlrLayershellAttached>("WlrLayershell", "Use WlrLayershell as an attached object of PanelWindow", attached: o => new WlrLayershellAttached(o), attachedType: typeof(WlrLayershellAttached))
            .Uncreatable<BackgroundEffectAttached>("BackgroundEffect", "BackgroundEffect is only available as an attached object", attached: o => new BackgroundEffectAttached(o), attachedType: typeof(BackgroundEffectAttached))
            .Type<IdleInhibitor>("IdleInhibitor")
            .Type<IdleMonitor>("IdleMonitor")
            .Add(StandardModules.EnumType("WlrLayer", [new("Background", 0), new("Bottom", 1), new("Top", 2), new("Overlay", 3)], wayland.Name))
            .Add(StandardModules.EnumType("WlrKeyboardFocus", [new("None", 0), new("Exclusive", 1), new("OnDemand", 2)], wayland.Name));
        Services.Unavailable.UnavailableModules.RegisterWayland(wayland);
    }
}

/// <summary>Where the Quickshell.Services modules for Windows register.</summary>
public static class ServiceModules
{
    public static void Register(ModuleRegistry registry)
    {
        Services.Power.UPowerModule.Register(registry);
        Services.Network.NetworkingModule.Register(registry);
        Services.Bluetooth.BluetoothModule.Register(registry);
        Services.Audio.Pipewire.Register(registry);
        Services.Media.Mpris.Register(registry);
        Services.WindowManagerModules.Register(registry);
        Services.Pam.PamModule.Register(registry);
        Services.Tray.SystemTrayModule.Register(registry);
        Services.Notifications.NotificationModule.Register(registry);
        Services.Unavailable.UnavailableModules.Register(registry);
    }
}
