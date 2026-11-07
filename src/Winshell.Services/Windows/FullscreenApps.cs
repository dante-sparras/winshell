using Winshell.Native;

namespace Winshell.Windows;

/// <summary>
/// The screens another app is fullscreen on, read from the foreground window. It shares the window list's hooks
/// without making the list.
/// </summary>
internal sealed class FullscreenApps : IDisposable
{
    private readonly IEventLoop _loop;
    private readonly IWindowBackend _backend;
    private readonly IDisposable _winEvents;
    private IReadOnlySet<string> _screens;
    private bool _readPosted;
    private bool _disposed;

    /// <summary>Starts watching the foreground. Call it on the event loop's thread.</summary>
    internal FullscreenApps(IEventLoop loop, Log log, IWindowBackend? backend = null)
    {
        _loop = loop;
        _backend = backend ?? WindowBackend.Shared;
        _winEvents = _backend.ListenWinEvents(OnWinEvent, e => log.Warn("windows", e.Message));
        foreach (var refused in _backend.WatchForeground())
            log.Warn("windows", refused);
        _screens = _backend.FullscreenScreens();
    }

    /// <summary>The screens changed. Raised on the event loop.</summary>
    public event Action? Changed;

    /// <summary>Whether another app is fullscreen on the screen with this id.</summary>
    public bool On(string screenId)
    {
        return _screens.Contains(screenId);
    }

    private void OnWinEvent(uint eventType, nint hwnd, int idObject, int idChild)
    {
        if (hwnd == 0 || !WindowRules.IsWindowObject(idObject, idChild))
            return;
        // A move or resize is sent for every window, so only the foreground one can change the answer.
        var foreground = eventType == Win32.EVENT_SYSTEM_FOREGROUND
                         || (eventType == Win32.EVENT_OBJECT_LOCATIONCHANGE && hwnd == _backend.Foreground);
        if (!foreground || _readPosted)
            return;
        _readPosted = true;
        _loop.Post(Read);
    }

    private void Read()
    {
        _readPosted = false;
        if (_disposed)
            return;
        var next = _backend.FullscreenScreens();
        if (next.SetEquals(_screens))
            return;
        _screens = next;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _winEvents.Dispose();
    }
}
