using System.Runtime.InteropServices;
using Winshell.Native;
using static Winshell.Windows.NativeMethods;

namespace Winshell.Windows;

/// <summary>
/// Raises <see cref="Fired"/> on the UI thread when Windows switches virtual desktop. The hook stays for the
/// process, the same way the window list's hooks do.
/// </summary>
internal static unsafe class DesktopSwitchWatch
{
    private static IEventLoop? _loop;
    private static bool _posted;

    public static event Action? Fired;

    public static void Ensure(IEventLoop loop, Log log)
    {
        if (_loop is not null)
            return;
        _loop = loop;
        if (SetWinEventHook(EVENT_SYSTEM_DESKTOPSWITCH, EVENT_SYSTEM_DESKTOPSWITCH, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT) == 0)
            log.Warn("desktops", "SetWinEventHook failed for a desktop switch; the list updates only after Winshell changes a desktop");
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time) =>
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
