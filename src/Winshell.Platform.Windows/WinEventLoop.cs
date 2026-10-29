using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows;

/// <summary>
/// The UI thread's loop: Windows messages, posted work, timers and frames. It sleeps in
/// MsgWaitForMultipleObjectsEx until a message, a post or the next timer.
/// </summary>
public sealed class WinEventLoop : EventQueue, IDisposable
{
    private readonly object _wakeLock = new();
    private nint _wake;
    private bool _quit;
    private int _exitCode;

    public WinEventLoop()
    {
        _wake = Win32.CreateEventW(0, false, false, 0);
        Woken += Wake;
    }

    public void Quit(int code)
    {
        _exitCode = code;
        _quit = true;
        Wake();
    }

    public int Run()
    {
        var handles = new[] { _wake };
        while (!_quit)
        {
            RunDue();
            if (HasFrameRequests)
                RunFrame();
            if (_quit)
                break;
            var timeout = Win32.INFINITE;
            if (NextDue is { } due)
                timeout = (uint)Math.Clamp(Math.Ceiling((due - Now).TotalMilliseconds), 0, int.MaxValue);
            if (HasFrameRequests)
                timeout = 0;
            Win32.MsgWaitForMultipleObjectsEx(1, handles, timeout, Win32.QS_ALLINPUT, Win32.MWMO_INPUTAVAILABLE);
            // Messages first, but a flood of them must not starve timers and frames.
            for (var i = 0; i < 200 && Win32.PeekMessageW(out var msg, 0, 0, 0, Win32.PM_REMOVE); i++)
            {
                if (msg.message == Win32.WM_QUIT)
                {
                    _quit = true;
                    _exitCode = (int)msg.wParam;
                    break;
                }
                Win32.TranslateMessage(msg);
                Win32.DispatchMessageW(msg);
            }
        }
        return _exitCode;
    }

    // A worker can post while the loop is disposed, so the lock keeps SetEvent off a closed, possibly reused handle.
    private void Wake()
    {
        lock (_wakeLock)
        {
            if (_wake != 0)
                Win32.SetEvent(_wake);
        }
    }

    public void Dispose()
    {
        lock (_wakeLock)
        {
            if (_wake == 0)
                return;
            Win32.CloseHandle(_wake);
            _wake = 0;
        }
    }
}
