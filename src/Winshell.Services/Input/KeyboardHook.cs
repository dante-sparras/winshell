using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Input;

/// <summary>
/// The process's one low-level keyboard hook, on a thread of its own. While attached, every key goes to
/// <paramref name="onKey"/>, which returns true to swallow it. A second hook cannot attach while another is.
/// </summary>
/// <param name="onKey">Runs on the hook thread with the key, whether it came up, and the modifiers Windows sees held.</param>
/// <param name="onLost">Runs on the hook thread when that thread ends without <see cref="Detach"/>.</param>
internal sealed unsafe class KeyboardHook(Func<int, bool, HotkeyModifiers, bool> onKey, Action onLost)
{
    private const uint LlkhfUp = 0x80;

    private static readonly object Gate = new();
    private static HookThread? Installed;
    private static KeyboardHook? Owner;

    /// <summary>Whether this hook is the one installed.</summary>
    public bool IsAttached
    {
        get { lock (Gate) return ReferenceEquals(Owner, this) && Installed is not null; }
    }

    internal static bool IsInstalled
    {
        get { lock (Gate) return Installed is not null; }
    }

    internal static Thread? InstalledThread
    {
        get { lock (Gate) return Installed?.Thread; }
    }

    // The procedure can run before the hook is recorded here. Do not hold the gate while waiting for it.
    public void Attach()
    {
        lock (Gate)
        {
            if (Owner == this && Installed is not null)
                return;
            if (Owner is not null && Owner != this)
                return;
            Owner = this;
        }
        var thread = HookThread.Start(Win32.WH_KEYBOARD_LL, &Proc, "Winshell keyboard hook", OnThreadExited);
        lock (Gate)
        {
            if (Owner == this && thread is not null)
            {
                Installed = thread;
                return;
            }
            if (Owner == this)
                Owner = null;
        }
        thread?.Stop();
    }

    public void Detach()
    {
        HookThread? thread;
        lock (Gate)
        {
            if (Owner != this)
                return;
            thread = Installed;
            Installed = null;
            Owner = null;
        }
        thread?.Stop();
    }

    /// <summary>Removes this process's keyboard hook, whoever installed it. Tests call this if a registrar is abandoned.</summary>
    internal static void EmergencyUnhook()
    {
        HookThread? thread;
        lock (Gate)
        {
            thread = Installed;
            Installed = null;
            Owner = null;
        }
        thread?.Stop();
    }

    private void OnThreadExited()
    {
        lock (Gate)
        {
            if (!ReferenceEquals(Owner, this))
                return;
            Installed = null;
            Owner = null;
        }
        onLost();
    }

    // Read the modifiers before the handler takes its lock: GetAsyncKeyState does not need it, and the lock stays short.
    private bool Handle(nint lParam)
    {
        var info = (KbdLlHookStruct*)lParam;
        return onKey((int)info->vkCode, (info->flags & LlkhfUp) != 0, ReadModifiers());
    }

    private static HotkeyModifiers ReadModifiers()
    {
        HotkeyModifiers mods = 0;
        if (Down(0x11) || Down(0xA2) || Down(0xA3))
            mods |= HotkeyModifiers.Ctrl;
        if (Down(0x12) || Down(0xA4) || Down(0xA5))
            mods |= HotkeyModifiers.Alt;
        if (Down(0x10) || Down(0xA0) || Down(0xA1))
            mods |= HotkeyModifiers.Shift;
        if (Down(VirtualKeys.LeftWin) || Down(VirtualKeys.RightWin))
            mods |= HotkeyModifiers.Win;
        return mods;
    }

    private static bool Down(int virtualKey) => Win32.GetAsyncKeyState(virtualKey) < 0;

    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        var swallow = code >= 0 && !IsOwnInjection(lParam) && NativeCallback.Guard(Log.Default, "hotkey", () =>
        {
            KeyboardHook? owner;
            lock (Gate)
                owner = Owner;
            return owner is not null && owner.Handle(lParam);
        }, false);
        if (swallow)
            return 1;
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool IsOwnInjection(nint lParam) => lParam != 0 && KeyMask.IsOurs(((KbdLlHookStruct*)lParam)->dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }
}
