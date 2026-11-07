using Winshell.Native;

namespace Winshell.Power;

/// <summary>Keeps the screen on and the PC awake while enabled. Dispose it to let go.</summary>
public sealed class KeepAwake : ObservableObject, IDisposable
{
    private static int _active;

    private readonly Log _log;
    private bool _enabled;
    private PowerRequest? _request;

    /// <summary>Creates a keep-awake that is off until <see cref="Enabled"/> is set.</summary>
    /// <param name="log">Where a power request Windows refuses is reported: the shell's log.</param>
    public KeepAwake(Log log)
    {
        _log = log;
    }

    /// <summary>Whether this keep-awake holds the screen on and the PC awake. False by default.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value))
                return;
            Interlocked.Add(ref _active, value ? 1 : -1);
            if (value)
                Acquire();
            else
                Release();
        }
    }

    internal static bool AnyActive => Volatile.Read(ref _active) > 0;

    // Each enabled instance holds its own request, and Windows keeps the PC awake while any is held.
    private void Acquire()
    {
        _request = PowerRequest.TryCreate("Winshell keeps the screen on", out var step, out var error);
        if (_request is null)
            _log.Warn("power", $"Cannot {step} the power request: Windows error {error}");
    }

    private void Release()
    {
        _request?.Dispose();
        _request = null;
    }

    /// <summary>Turns this keep-awake off, so it no longer holds the PC awake.</summary>
    public void Dispose()
    {
        Enabled = false;
    }
}
