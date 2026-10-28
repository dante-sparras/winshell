using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Power;

/// <summary><c>KeepAwake</c>: keeps the screen on and the PC awake while enabled.</summary>
public sealed class KeepAwake : QObject
{
    private bool _enabled;
    private ShellWindow? _window;
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
    public ShellWindow? Window { get { Track(); return _window; } set => SetField(ref _window, value); }

    private static void Apply()
    {
        // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED while any KeepAwake is on.
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

/// <summary><c>IdleMonitor</c>: tells when there has been no input for a while.</summary>
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
        var idle = _enabled && _timeout > 0 && IdleSeconds() >= _timeout && !(_respectInhibitors && KeepAwake.AnyActive);
        SetField(ref _isIdle, idle, nameof(IsIdle));
        // Poll faster while idle, so the end of idleness shows quickly.
        var next = idle ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(Math.Clamp(_timeout - IdleSeconds(), 0.25, 5));
        _timer = Engine.Loop.Schedule(next, Check);
    }

    protected override void OnDestroy() => _timer?.Dispose();
}
