using System.Runtime.InteropServices;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>A key as the low-level keyboard hook sees it.</summary>
/// <param name="VirtualKey">The virtual-key code.</param>
/// <param name="Up">Whether the key came up.</param>
/// <param name="ExtraInfo">The tag whoever injected the key put on it, or 0.</param>
internal readonly record struct HookedKey(int VirtualKey, bool Up, nint ExtraInfo);

/// <summary>
/// The low-level keyboard hook, on a <see cref="HookThread"/>. Windows calls one static procedure, so each hook
/// started here calls the handler given last: the process keeps one.
/// </summary>
internal static unsafe class LowLevelKeyboard
{
    private static Func<HookedKey, bool>? Handler;

    /// <summary>Starts the hook on a thread of its own. Null if Windows refused it.</summary>
    /// <param name="onKey">
    /// Runs on the hook thread with each key, and returns true to swallow it. Its exception lets the key through and
    /// does not reach Windows, so it logs its own.
    /// </param>
    /// <param name="name">The hook thread's name.</param>
    /// <param name="lost">Runs as <see cref="HookThread.Start"/> describes.</param>
    internal static HookThread? Start(Func<HookedKey, bool> onKey, string name, Action lost)
    {
        Volatile.Write(ref Handler, onKey);
        return HookThread.Start(Win32.WH_KEYBOARD_LL, &Proc, name, lost);
    }

    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && lParam != 0 && Volatile.Read(ref Handler) is { } handler)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            try
            {
                if (handler(new HookedKey((int)info->vkCode, (info->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_UP) != 0,
                        (nint)info->dwExtraInfo)))
                    return 1;
            }
#pragma warning disable CA1031 // Windows called in; nothing above this frame can catch it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }
}
