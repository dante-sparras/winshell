using System.Runtime.InteropServices;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>A key as the low-level keyboard hook sees it.</summary>
/// <param name="VirtualKey">The virtual-key code.</param>
/// <param name="Up">Whether the key came up.</param>
internal readonly record struct HookedKey(int VirtualKey, bool Up);

/// <summary>
/// The process's one low-level keyboard hook, on a thread of its own. A second call does nothing until the first hook
/// is disposed: Windows calls one static procedure, so the process keeps one. Keys this process injected with
/// <see cref="KeyInput.Tap"/> pass the hook without reaching its handler.
/// </summary>
internal static unsafe class LowLevelKeyboard
{
    private static readonly Lock Gate = new();
    private static Slot? Owner;

    /// <summary>
    /// Installs the hook. Null when one is already installed or Windows refused it. Disposing the result removes it.
    /// </summary>
    /// <param name="onKey">
    /// Runs on the hook thread with each key, and returns true to swallow it. Its exception lets the key through.
    /// </param>
    /// <param name="failed">Gets each exception <paramref name="onKey"/> throws, on the hook thread.</param>
    /// <param name="lost">
    /// Runs on the hook thread when that thread ends without the hook being disposed. Windows can also remove the
    /// hook and leave the thread running, and then this does not run.
    /// </param>
    internal static IDisposable? Start(Func<HookedKey, bool> onKey, Action<Exception> failed, Action? lost = null)
    {
        var slot = new Slot(onKey, failed, lost);
        lock (Gate)
        {
            if (Owner is not null)
                return null;
            Volatile.Write(ref Owner, slot);
        }

        // The procedure can run before Start returns. Do not hold the gate while waiting for the thread.
        var thread = HookThread.Start(Win32.WH_KEYBOARD_LL, &Proc, "Winshell keyboard hook", () => OnLost(slot));
        lock (Gate)
        {
            if (!ReferenceEquals(Owner, slot) || thread is null)
            {
                if (ReferenceEquals(Owner, slot))
                    Volatile.Write(ref Owner, null);

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
            Volatile.Write(ref Owner, null);
            lost = slot.Lost;
        }

        lost?.Invoke();
    }

    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && lParam != 0 && Volatile.Read(ref Owner) is { } owner)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            var key = new HookedKey((int)info->vkCode, (info->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_UP) != 0);
            if (info->dwExtraInfo != KeyInput.Marker && Callback.Run(owner.Failed, () => owner.OnKey(key), false))
                return 1;
        }

        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    private sealed class Slot(Func<HookedKey, bool> onKey, Action<Exception> failed, Action? lost) : IDisposable
    {
        private HookThread? _thread;

        public Func<HookedKey, bool> OnKey { get; } = onKey;

        public Action<Exception> Failed { get; } = failed;

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
                Volatile.Write(ref Owner, null);
            }

            thread?.Stop();
        }
    }
}
