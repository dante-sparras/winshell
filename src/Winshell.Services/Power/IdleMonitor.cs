using Winshell.Native;

namespace Winshell.Power;

/// <summary>Tells when there has been no input for a while.</summary>
public sealed class IdleMonitor : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Func<double> _idleSeconds;
    private readonly Func<bool> _heldAwake;
    private readonly Log _log;
    private bool _enabled = true;
    private double _timeout;
    private bool _respectKeepAwake = true;
    private bool _isIdle;
    private bool _disposed;
    private IDisposable? _timer;

    public IdleMonitor(IEventLoop loop)
        : this(loop, Log.Default, IdleSeconds, () => KeepAwake.AnyActive)
    {
    }

    internal IdleMonitor(IEventLoop loop, Log log, Func<double> idleSeconds, Func<bool> heldAwake)
    {
        _loop = loop;
        _log = log;
        _idleSeconds = idleSeconds;
        _heldAwake = heldAwake;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                Check();
        }
    }

    /// <summary>The seconds without input after which <see cref="IsIdle"/> turns on.</summary>
    public double Timeout
    {
        get => _timeout;
        set
        {
            if (Set(ref _timeout, value))
                Check();
        }
    }

    /// <summary>When a <see cref="KeepAwake"/> is on, this monitor does not report idle.</summary>
    public bool RespectKeepAwake
    {
        get => _respectKeepAwake;
        set => Set(ref _respectKeepAwake, value);
    }

    public bool IsIdle => _isIdle;

    private static double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = 8 };
        if (!Win32.GetLastInputInfo(ref info))
            return 0;
        return unchecked(Win32.GetTickCount() - info.dwTime) / 1000.0;
    }

    private void Check()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
            return;
        // A change to Enabled or Timeout checks again, so a monitor that cannot be idle does not poll.
        if (!_enabled || _timeout <= 0)
        {
            Set(ref _isIdle, false, nameof(IsIdle));
            return;
        }
        var next = TimeSpan.FromSeconds(1);
        try
        {
            var seconds = _idleSeconds();
            var idle = seconds >= _timeout && !(_respectKeepAwake && _heldAwake());
            Set(ref _isIdle, idle, nameof(IsIdle));
            next = NextCheck(idle, _timeout - seconds);
        }
#pragma warning disable CA1031 // A failed idle read must not stop the poll.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Warn("power", e.Message);
        }
        _timer = _loop.Schedule(next, Check);
    }

    /// <summary>
    /// When to look again. Before the timeout, at the moment it can pass, at most 5 s ahead. Once idle, or held
    /// awake past it, once a second.
    /// </summary>
    internal static TimeSpan NextCheck(bool idle, double remaining) =>
        !idle && remaining > 0 ? TimeSpan.FromSeconds(Math.Clamp(remaining, 0.25, 5)) : TimeSpan.FromSeconds(1);

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
