using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Input;

/// <summary>
/// Gives one shell window the keyboard while that window is in front. Windows shortcuts and Winshell hotkeys
/// do not take those keys. Ctrl+Alt+Del and Win+L still reach Windows.
/// </summary>
[QmlSignal("cancelled")]
public sealed class ShortcutInhibitor : QObject, IInhibitClient
{
    private bool _enabled;
    private bool _active;
    private ShellWindow? _window;
    private bool _hookRefused;
    private bool _missingRegistrar;
    private IDisposable? _activeSub;
    private IDisposable? _visibleSub;
    private IDisposable? _connectedSub;
    private IDisposable? _destroyedSub;

    public ShortcutInhibitor()
    {
        // Runs before other destroyed handlers, so capture is gone before the rest of teardown.
        Connect("destroyed", _ => StopCapture());
    }

    /// <summary>The native handle of <see cref="Window"/>. Tests substitute a handle without creating a window.</summary>
    internal Func<ShellWindow, nint> HandleOf { get; set; } = static window =>
        window.Backend is Win32Window native ? native.Handle : 0;

    /// <summary>Asks to capture keys. Default false, as in Quickshell.</summary>
    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                Refresh();
        }
    }

    /// <summary>The shell window the keys go to. Capture requires it to be the foreground window.</summary>
    [Qml]
    public ShellWindow? Window
    {
        get { Track(); return _window; }
        set
        {
            if (ReferenceEquals(_window, value))
                return;
            Unwatch();
            _window = value;
            if (value is { IsDestroyed: false, IsDestroying: false })
                Watch(value);
            NotifyChanged("window");
            Refresh();
        }
    }

    /// <summary>
    /// True only while <see cref="Enabled"/> is true, <see cref="Window"/> is the foreground window, and the hook is installed.
    /// </summary>
    [Qml]
    public bool Active { get { Track(); return _active; } }

    public override void ComponentComplete() => Refresh();

    protected override void OnDestroy()
    {
        StopCapture();
        base.OnDestroy();
    }

    private void StopCapture()
    {
        Unwatch();
        Registrar?.SetInhibition(this, 0);
        _active = false;
    }

    void IInhibitClient.Cancelled()
    {
        if (IsDestroyed || !_enabled)
            return;
        _enabled = false;
        NotifyChanged("enabled");
        Refresh();
        Emit("cancelled");
    }

    void IInhibitClient.Recheck()
    {
        if (!IsDestroyed)
            Refresh();
    }

    private WindowsHotkeyRegistrar? Registrar => Engine?.GetService<IHotkeyRegistrar>() as WindowsHotkeyRegistrar;

    private void Watch(ShellWindow window)
    {
        _activeSub = window.Connect("activeChanged", _ => Refresh());
        _visibleSub = window.Connect("backingWindowVisibleChanged", _ => Refresh());
        _connectedSub = window.Connect("windowConnected", _ => Refresh());
        _destroyedSub = window.Connect("destroyed", _ => WindowGone());
    }

    private void Unwatch()
    {
        _activeSub?.Dispose();
        _visibleSub?.Dispose();
        _connectedSub?.Dispose();
        _destroyedSub?.Dispose();
        _activeSub = _visibleSub = _connectedSub = _destroyedSub = null;
    }

    private void WindowGone()
    {
        Unwatch();
        Refresh();
    }

    private void Refresh()
    {
        if (!IsComplete || IsDestroyed)
            return;
        var registrar = Registrar;
        var window = _window is { IsDestroyed: false, IsDestroying: false } candidate ? candidate : null;
        var hwnd = _enabled && window is { Active: true } ? HandleOf(window) : 0;
        if (registrar is null || hwnd == 0)
        {
            registrar?.SetInhibition(this, 0);
            _hookRefused = false;
            if (registrar is null && _enabled && window is { Active: true } && !_missingRegistrar)
            {
                _missingRegistrar = true;
                (Engine?.Log ?? Log.Default).Warn("hotkey", "Shortcuts cannot be inhibited.");
            }
            SetField(ref _active, false, nameof(Active));
            return;
        }
        // Stay registered while the window still says it is active, so a key after it is in front again is captured
        // on that key. active itself is true only while the window is the foreground window and the hook is in place.
        var installed = registrar.SetInhibition(this, hwnd);
        SetField(ref _active, installed && registrar.IsForeground(hwnd), nameof(Active));
        if (installed || registrar.IsDisposed)
        {
            _hookRefused = false;
            return;
        }
        if (_hookRefused)
            return;
        _hookRefused = true;
        (Engine?.Log ?? Log.Default).Warn("hotkey", "Windows did not install a keyboard hook, so shortcuts are not inhibited.");
    }
}
