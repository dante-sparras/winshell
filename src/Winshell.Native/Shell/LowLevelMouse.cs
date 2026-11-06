using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// The low-level mouse hook, on a <see cref="HookThread"/> that runs while anyone watches for presses. Every watch
/// shares the one hook, and the newest hears a press first, as it would from a hook of its own.
/// </summary>
internal static unsafe class LowLevelMouse
{
    private static readonly Lock Gate = new();
    private static Watch[] _watches = [];
    private static HookThread? _thread;

    /// <summary>Starts watching for presses of any mouse button, until the watch is disposed. Null if Windows refused the hook.</summary>
    /// <param name="onPress">
    /// Runs on the hook thread with each press, at its point on the virtual desktop in physical pixels.
    /// </param>
    /// <param name="failed">Gets each exception <paramref name="onPress"/> throws, on the hook thread.</param>
    internal static IDisposable? Start(Action<POINT> onPress, Action<Exception> failed)
    {
        lock (Gate)
        {
            _thread ??= HookThread.Start(Win32.WH_MOUSE_LL, &Proc, "Winshell mouse hook");
            if (_thread is null)
                return null;
            var watch = new Watch(onPress, failed);
            Volatile.Write(ref _watches, [watch, .. _watches]);
            return watch;
        }
    }

    private static void Stop(Watch watch)
    {
        lock (Gate)
        {
            var rest = Array.FindAll(_watches, w => w != watch);
            Volatile.Write(ref _watches, rest);
            if (rest.Length > 0 || _thread is null)
                return;
            _thread.Stop();
            _thread = null;
        }
    }

    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && lParam != 0
                      && (uint)wParam is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN
                      or Win32.WM_XBUTTONDOWN)
        {
            var pt = ((MSLLHOOKSTRUCT*)lParam)->pt;
            foreach (var watch in Volatile.Read(ref _watches))
                Callback.Run(watch.Failed, () => watch.OnPress(pt));
        }

        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    private sealed class Watch(Action<POINT> onPress, Action<Exception> failed) : IDisposable
    {
        private int _stopped;

        public Action<POINT> OnPress { get; } = onPress;

        public Action<Exception> Failed { get; } = failed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
                Stop(this);
        }
    }
}
