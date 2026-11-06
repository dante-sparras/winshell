using System.Runtime.InteropServices;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>A key as the low-level keyboard hook sees it.</summary>
/// <param name="VirtualKey">The virtual-key code.</param>
/// <param name="Up">Whether the key came up.</param>
/// <param name="ExtraInfo">The tag whoever injected the key put on it, or 0.</param>
internal readonly record struct HookedKey(int VirtualKey, bool Up, nint ExtraInfo);

/// <summary>
/// The process's one low-level keyboard hook, on a thread of its own. A second call does nothing until the first hook
/// is disposed: Windows calls one static procedure, so the process keeps one.
/// </summary>
internal static unsafe class LowLevelKeyboard
{
    private static readonly Lock Gate = new();
    private static Slot? Owner;
    private static Func<HookedKey, bool>? Handler;

    /// <summary>
    /// Installs the hook. Null when one is already installed or Windows refused it. Disposing the result removes it.
    /// </summary>
    /// <param name="onKey">
    /// Runs on the hook thread with each key, and returns true to swallow it. Its exception lets the key through and
    /// does not reach Windows, so it logs its own.
    /// </param>
    /// <param name="lost">
    /// Runs on the hook thread when that thread ends without the hook being disposed. Windows can also remove the
    /// hook and leave the thread running, and then this does not run.
    /// </param>
    internal static IDisposable? Start(Func<HookedKey, bool> onKey, Action? lost = null)
    {
        var slot = new Slot(lost);
        lock (Gate)
        {
            if (Owner is not null)
                return null;
            Owner = slot;
            Volatile.Write(ref Handler, onKey);
        }

        // The procedure can run before Start returns. Do not hold the gate while waiting for the thread.
        var thread = HookThread.Start(Win32.WH_KEYBOARD_LL, &Proc, "Winshell keyboard hook", () => OnLost(slot));
        lock (Gate)
        {
            if (!ReferenceEquals(Owner, slot) || thread is null)
            {
                if (ReferenceEquals(Owner, slot))
                {
                    Owner = null;
                    Volatile.Write(ref Handler, null);
                }

                thread?.Stop();
                return null;
            }

            slot.Arm(thread);
            return slot;
        }
    }

    private static void OnLost(Slot slot)
    {
        Action? lost;
        lock (Gate)
        {
            if (!ReferenceEquals(Owner, slot))
                return;
            Owner = null;
            Volatile.Write(ref Handler, null);
            lost = slot.Lost;
        }

        lost?.Invoke();
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

    private sealed class Slot(Action? lost) : IDisposable
    {
        private HookThread? _thread;

        public Action? Lost { get; } = lost;

        public void Arm(HookThread thread)
        {
            _thread = thread;
        }

        public void Dispose()
        {
            HookThread? thread;
            lock (Gate)
            {
                if (!ReferenceEquals(Owner, this))
                    return;
                thread = _thread;
                Owner = null;
                Volatile.Write(ref Handler, null);
            }

            thread?.Stop();
        }
    }
}
