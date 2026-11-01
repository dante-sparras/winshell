using Winshell.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows;

/// <summary>Turns Windows virtual keys into Qt key codes and modifiers.</summary>
internal static class Keys
{
    /// <summary>Modifier keys that are down. The window reads them; translation does not.</summary>
    public readonly record struct Held(bool Shift, bool Control, bool Alt, bool LeftControl, bool RightAlt, bool Meta);

    /// <summary>A Qt key code and the modifiers that go with it.</summary>
    public readonly record struct Translated(int Key, int Modifiers);

    /// <summary>Modifiers for a pointer event. A key event uses <see cref="Translate"/>, which also tells AltGr from Ctrl+Alt.</summary>
    public static int Modifiers()
    {
        var m = 0;
        if (Win32.GetKeyState(0x10) < 0)
            m |= QtEnums.ShiftModifier;
        if (Win32.GetKeyState(0x11) < 0)
            m |= QtEnums.ControlModifier;
        if (Win32.GetKeyState(0x12) < 0)
            m |= QtEnums.AltModifier;
        if (Win32.GetKeyState(0x5B) < 0 || Win32.GetKeyState(0x5C) < 0)
            m |= QtEnums.MetaModifier;
        return m;
    }

    public static Held ReadHeld()
    {
        static bool Down(int vk) => Win32.GetKeyState(vk) < 0;
        return new Held(
            Down(0x10),
            Down(0x11),
            Down(0x12),
            Down(0xA2), // VK_LCONTROL
            Down(0xA5), // VK_RMENU
            Down(0x5B) || Down(0x5C));
    }

    /// <summary>The Qt key for one virtual key. <paramref name="extended"/> is bit 24 of the key message.</summary>
    public static Translated Translate(int vk, bool extended, Held held) =>
        new(ToQt(vk, extended), ModifierBits(held, IsKeypad(vk, extended)));

    /// <summary>
    /// A handled Alt key must eat the <c>WM_SYSCHAR</c> that follows it, or <c>DefWindowProc</c> beeps.
    /// The scan codes are the ones in the two messages. Nothing is eaten when no Alt key was handled.
    /// </summary>
    public static bool EatSysChar(int handledScan, int charScan) =>
        handledScan >= 0 && handledScan == charScan;

    private static int ModifierBits(Held held, bool keypad)
    {
        // AltGr is left Ctrl together with right Alt. It types a character, so it is neither Ctrl nor Alt.
        var altGr = held.LeftControl && held.RightAlt;
        var m = 0;
        if (held.Shift)
            m |= QtEnums.ShiftModifier;
        if (held.Control && !altGr)
            m |= QtEnums.ControlModifier;
        if (held.Alt && !altGr)
            m |= QtEnums.AltModifier;
        if (held.Meta)
            m |= QtEnums.MetaModifier;
        if (keypad)
            m |= QtEnums.KeypadModifier;
        return m;
    }

    private static bool IsKeypad(int vk, bool extended)
    {
        // Numpad 0-9 and the numpad operators have their own virtual keys.
        if (vk is >= 0x60 and <= 0x6F)
            return true;
        // Numpad Enter is Enter with the extended bit. The main Enter key is not extended.
        if (vk == 0x0D)
            return extended;
        // Num Lock sits on the keypad, and Windows marks that key extended.
        if (vk == 0x90)
            return extended;
        // With Num Lock off the keypad sends navigation keys without the extended bit.
        // The dedicated navigation keys are extended.
        return !extended && vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E;
    }

    private static int ToQt(int vk, bool extended)
    {
        // Numpad Enter. The main Enter key stays Qt.Key_Return.
        if (vk == 0x0D && extended)
            return 0x01000005;
        return vk switch
        {
            >= 0x41 and <= 0x5A => vk,
            >= 0x30 and <= 0x39 => vk,
            >= 0x60 and <= 0x69 => '0' + (vk - 0x60),
            >= 0x70 and <= 0x87 => 0x01000030 + (vk - 0x70),
            0x08 => 0x01000003,
            0x09 => 0x01000001,
            0x0D => 0x01000004,
            0x1B => 0x01000000,
            0x20 => 0x20,
            0x21 => 0x01000016,
            0x22 => 0x01000017,
            0x23 => 0x01000011,
            0x24 => 0x01000010,
            0x25 => 0x01000012,
            0x26 => 0x01000013,
            0x27 => 0x01000014,
            0x28 => 0x01000015,
            0x2C => 0x01000009,
            0x2D => 0x01000006,
            0x2E => 0x01000007,
            0x13 => 0x01000008,
            0x10 or 0xA0 or 0xA1 => 0x01000020,
            0x11 or 0xA2 or 0xA3 => 0x01000021,
            0x12 or 0xA4 or 0xA5 => 0x01000023,
            0x5B => 0x01000053,
            0x5C => 0x01000054,
            0x5D => 0x01000055,
            0x14 => 0x01000024,
            0x90 => 0x01000025,
            0x91 => 0x01000026,
            0x6A => '*',
            0x6B => '+',
            0x6D => '-',
            0x6E => '.',
            0x6F => '/',
            0xBA => ';',
            0xBB => '=',
            0xBC => ',',
            0xBD => '-',
            0xBE => '.',
            0xBF => '/',
            0xC0 => '`',
            0xDB => '[',
            0xDC => '\\',
            0xDD => ']',
            0xDE => '\'',
            0xAD => 0x01000071,
            0xAE => 0x01000070,
            0xAF => 0x01000072,
            0xB0 => 0x01000083,
            0xB1 => 0x01000082,
            0xB2 => 0x01000081,
            0xB3 => 0x01000085,
            _ => 0x01ffffff,
        };
    }
}
