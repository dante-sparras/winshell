using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// Out-of-context WinEvent hooks that stay for the process. Windows calls a hook on the thread that set it, while
/// that thread pumps messages. A handler runs inside that call from Windows, so it must not throw: guard its body.
/// </summary>
internal static unsafe class WinEventHook
{
    private static readonly ConcurrentDictionary<nint, Action<uint, nint, int, int>> Handlers = new();

    /// <summary>
    /// Calls <paramref name="handler"/> with each event from <paramref name="min"/> through <paramref name="max"/>:
    /// its type, window, object id and child id. False when Windows refused the hook.
    /// </summary>
    internal static bool Start(uint min, uint max, Action<uint, nint, int, int> handler)
    {
        var hook = Win32.SetWinEventHook(min, max, 0, &OnEvent, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        if (hook == 0)
            return false;
        // Out of context, the first event arrives when this thread next pumps, so the handler is in place by then.
        Handlers[hook] = handler;
        return true;
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (Handlers.TryGetValue(hook, out var handler))
            handler(eventType, hwnd, idObject, idChild);
    }
}
