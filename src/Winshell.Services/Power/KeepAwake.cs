using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Power;

/// <summary>Keeps the screen on and the PC awake while enabled. Dispose it to let go.</summary>
public sealed class KeepAwake : ObservableObject, IDisposable
{
    private const int PowerRequestDisplayRequired = 0;
    private const int PowerRequestSystemRequired = 1;

    private static int _active;

    private readonly Log _log;
    private bool _enabled;
    private nint _request;

    public KeepAwake()
        : this(Log.Default)
    {
    }

    internal KeepAwake(Log log) => _log = log;

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

    /// <summary>Whether this instance holds a power request with Windows.</summary>
    internal bool HoldsRequest => _request != 0;

    // Each enabled instance holds its own request, and Windows keeps the PC awake while any is held.
    private unsafe void Acquire()
    {
        const string reason = "Winshell keeps the screen on";
        nint request;
        fixed (char* text = reason)
        {
            var context = new REASON_CONTEXT { Version = 0, Flags = 1, SimpleReasonString = (nint)text };
            request = Win32.PowerCreateRequest(ref context);
        }
        if (request == -1 || request == 0)
        {
            Fail("create");
            return;
        }
        if (!Win32.PowerSetRequest(request, PowerRequestDisplayRequired) || !Win32.PowerSetRequest(request, PowerRequestSystemRequired))
        {
            Fail("set");
            Win32.CloseHandle(request);
            return;
        }
        _request = request;
    }

    private void Release()
    {
        if (_request == 0)
            return;
        Win32.PowerClearRequest(_request, PowerRequestDisplayRequired);
        Win32.PowerClearRequest(_request, PowerRequestSystemRequired);
        Win32.CloseHandle(_request);
        _request = 0;
    }

    private void Fail(string step) =>
        _log.Warn("power", $"Cannot {step} the power request: Windows error {Marshal.GetLastPInvokeError()}");

    public void Dispose() => Enabled = false;
}
