using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winshell.Native;

/// <summary>Injects keys with <c>SendInput</c>.</summary>
internal static unsafe class KeyInput
{
    /// <summary>Unassigned virtual key. The same value AutoHotkey uses for its menu mask.</summary>
    private const VIRTUAL_KEY Unassigned = (VIRTUAL_KEY)0xE8;

    /// <summary>Tag on <c>dwExtraInfo</c>, so <see cref="LowLevelKeyboard"/> passes the injection by.</summary>
    internal const nuint Marker = 0x57154E4B;

    /// <summary>
    /// One down and up of an unassigned key, sent as one injection that nothing else can split. A focus takeover uses
    /// it to become the program that last sent input. Sent while the Windows key is down, it uses that press so Start
    /// does not open, without leaving the Windows key logically held.
    /// </summary>
    internal static void Tap()
    {
        var inputs = new INPUT[2];
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].Anonymous.ki.wVk = Unassigned;
            inputs[i].Anonymous.ki.dwFlags = i == 1 ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
            inputs[i].Anonymous.ki.dwExtraInfo = Marker;
        }

        fixed (INPUT* first = inputs)
        {
            _ = PInvoke.SendInput((uint)inputs.Length, first, sizeof(INPUT));
        }
    }
}
