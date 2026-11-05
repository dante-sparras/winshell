using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winshell.Native;

/// <summary>One key going down or up, tagged with <paramref name="ExtraInfo"/> for a hook to recognize.</summary>
internal readonly record struct KeyStroke(ushort VirtualKey, bool Up, nint ExtraInfo);

/// <summary>Injects keys with <c>SendInput</c>.</summary>
internal static unsafe class KeyInput
{
    /// <summary>The size of one input, as <c>SendInput</c> is told it.</summary>
    internal static int Size => sizeof(INPUT);

    /// <summary>Sends the keys as one injection, which nothing else can split. Returns how many Windows inserted.</summary>
    internal static uint Send(ReadOnlySpan<KeyStroke> keys)
    {
        var inputs = new INPUT[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            inputs[i].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].Anonymous.ki.wVk = (VIRTUAL_KEY)keys[i].VirtualKey;
            inputs[i].Anonymous.ki.dwFlags = keys[i].Up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
            inputs[i].Anonymous.ki.dwExtraInfo = (nuint)keys[i].ExtraInfo;
        }

        fixed (INPUT* first = inputs)
        {
            return PInvoke.SendInput((uint)inputs.Length, first, sizeof(INPUT));
        }
    }
}
