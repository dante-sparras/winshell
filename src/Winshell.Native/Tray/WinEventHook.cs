using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// An out-of-context WinEvent hook for one event type or a range of them, removed when disposed. Windows calls it on
/// the thread that set it, from that thread's message loop. A handler's exception does not reach Windows, so a handler
/// logs its own.
/// </summary>
internal sealed unsafe class WinEventHook : IDisposable
{
    private static readonly ConcurrentDictionary<nint, WinEventHook> Hooks = new();

    private readonly Action<uint, nint, int, int> _onEvent;
    private nint _hook;

    private WinEventHook(nint hook, Action<uint, nint, int, int> onEvent)
    {
        _hook = hook;
        _onEvent = onEvent;
    }

    /// <summary>Hooks <paramref name="eventType"/>, or returns null when Windows would not.</summary>
    /// <param name="onEvent">Gets the event type, the window, the object id and the child id.</param>
    internal static WinEventHook? Start(uint eventType, Action<uint, nint, int, int> onEvent) => Start(eventType, eventType, onEvent);

    /// <summary>Hooks the event types <paramref name="min"/> through <paramref name="max"/>, or returns null when Windows would not.</summary>
    /// <param name="onEvent">Gets the event type, the window, the object id and the child id.</param>
    internal static WinEventHook? Start(uint min, uint max, Action<uint, nint, int, int> onEvent)
    {
        var handle = Win32.SetWinEventHook(min, max, 0, &OnEvent, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        if (handle == 0)
            return null;
        var hook = new WinEventHook(handle, onEvent);
        Hooks[handle] = hook;
        return hook;
    }

    public void Dispose()
    {
        if (_hook == 0)
            return;
        Hooks.TryRemove(_hook, out _);
        Win32.UnhookWinEvent(_hook);
        _hook = 0;
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (Hooks.TryGetValue(hook, out var owner))
                owner._onEvent(eventType, hwnd, idObject, idChild);
        }
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
