using Winshell.Native;

namespace Winshell.Windows;

/// <summary>
/// Raises <see cref="Fired"/> on the UI thread when Windows switches virtual desktop. The hook stays for the
/// process, the same way the window list's hooks do.
/// </summary>
internal static class DesktopSwitchWatch
{
    private static IEventLoop? _loop;
    private static bool _posted;

    public static event Action? Fired;

    public static void Ensure(IEventLoop loop, Log log)
    {
        if (_loop is not null)
            return;
        _loop = loop;
        if (!WinEventHook.Start(Win32.EVENT_SYSTEM_DESKTOPSWITCH, Win32.EVENT_SYSTEM_DESKTOPSWITCH, OnEvent))
            log.Warn("desktops", "SetWinEventHook failed for a desktop switch; the list updates only after Winshell changes a desktop");
    }

    private static void OnEvent(uint eventType, nint hwnd, int idObject, int idChild) =>
        NativeCallback.Guard(Log.Default, "desktops", () =>
        {
            if (_posted || _loop is null)
                return;
            _posted = true;
            _loop.Post(Flush);
        });

    private static void Flush()
    {
        _posted = false;
        Fired?.Invoke();
    }
}
