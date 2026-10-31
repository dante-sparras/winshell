using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Windows virtual keys become Qt key codes without a real key press. The held modifiers are the
/// inputs; nothing here reads the keyboard.
/// </summary>
public class KeyTranslationTests
{
    private static readonly Keys.Held None = new(false, false, false, false, false, false);

    // Left Ctrl and right Alt are both down, which is also how the generic Ctrl and Alt bits read.
    private static readonly Keys.Held AltGr = new(false, true, true, true, true, false);

    private static readonly Keys.Held AltGrShift = new(true, true, true, true, true, false);

    // Left Ctrl and left Alt. Right Alt is up, so this is a real chord, not AltGr.
    private static readonly Keys.Held CtrlAlt = new(false, true, true, true, false, false);

    [Fact]
    public void AltGr_does_not_arrive_as_ctrl_alt()
    {
        var key = Keys.Translate(0x51, extended: false, AltGr);
        Assert.Equal('Q', key.Key);
        Assert.Equal(0, key.Modifiers);
    }

    [Fact]
    public void AltGr_with_shift_keeps_only_shift()
    {
        var key = Keys.Translate(0x51, extended: false, AltGrShift);
        Assert.Equal('Q', key.Key);
        Assert.Equal(QtEnums.ShiftModifier, key.Modifiers);
    }

    [Fact]
    public void Left_ctrl_and_left_alt_stay_ctrl_alt()
    {
        var key = Keys.Translate(0x51, extended: false, CtrlAlt);
        Assert.Equal(QtEnums.ControlModifier | QtEnums.AltModifier, key.Modifiers);
    }

    [Fact]
    public void Right_alt_without_left_ctrl_stays_alt()
    {
        var held = new Keys.Held(false, false, true, false, true, false);
        var key = Keys.Translate(0x51, extended: false, held);
        Assert.Equal(QtEnums.AltModifier, key.Modifiers);
    }

    [Fact]
    public void Right_ctrl_and_right_alt_stay_ctrl_alt()
    {
        var held = new Keys.Held(false, true, true, false, true, false);
        var key = Keys.Translate(0x51, extended: false, held);
        Assert.Equal(QtEnums.ControlModifier | QtEnums.AltModifier, key.Modifiers);
    }

    [Fact]
    public void Numpad_enter_is_key_enter_and_the_main_enter_is_return()
    {
        var numpad = Keys.Translate(0x0D, extended: true, None);
        var main = Keys.Translate(0x0D, extended: false, None);
        Assert.Equal(0x01000005, numpad.Key);
        Assert.Equal(QtEnums.KeypadModifier, numpad.Modifiers);
        Assert.Equal(0x01000004, main.Key);
        Assert.Equal(0, main.Modifiers);
    }

    [Fact]
    public void Numpad_digits_and_operators_have_the_keypad_modifier()
    {
        var digit = Keys.Translate(0x60, extended: false, None);
        var plus = Keys.Translate(0x6B, extended: false, None);
        var divide = Keys.Translate(0x6F, extended: true, None);
        Assert.Equal('0', digit.Key);
        Assert.Equal(QtEnums.KeypadModifier, digit.Modifiers);
        Assert.Equal('+', plus.Key);
        Assert.Equal(QtEnums.KeypadModifier, plus.Modifiers);
        Assert.Equal('/', divide.Key);
        Assert.Equal(QtEnums.KeypadModifier, divide.Modifiers);
    }

    [Fact]
    public void The_number_row_and_oem_minus_are_not_keypad_keys()
    {
        var digit = Keys.Translate(0x30, extended: false, None);
        var minus = Keys.Translate(0xBD, extended: false, None);
        Assert.Equal('0', digit.Key);
        Assert.Equal(0, digit.Modifiers);
        Assert.Equal('-', minus.Key);
        Assert.Equal(0, minus.Modifiers);
    }

    [Fact]
    public void Numpad_navigation_has_the_keypad_modifier_and_the_dedicated_keys_do_not()
    {
        var numpad = Keys.Translate(0x25, extended: false, None);
        var dedicated = Keys.Translate(0x25, extended: true, None);
        var insert = Keys.Translate(0x2D, extended: false, None);
        Assert.Equal(0x01000012, numpad.Key);
        Assert.Equal(QtEnums.KeypadModifier, numpad.Modifiers);
        Assert.Equal(0x01000012, dedicated.Key);
        Assert.Equal(0, dedicated.Modifiers);
        Assert.Equal(0x01000006, insert.Key);
        Assert.Equal(QtEnums.KeypadModifier, insert.Modifiers);
    }

    [Fact]
    public void Num_lock_has_the_keypad_modifier()
    {
        var key = Keys.Translate(0x90, extended: true, None);
        Assert.Equal(0x01000025, key.Key);
        Assert.Equal(QtEnums.KeypadModifier, key.Modifiers);
    }

    [Fact]
    public void A_handled_alt_letter_eats_only_its_own_system_character()
    {
        Assert.True(Keys.EatSysChar(handledScan: 0x1E, charScan: 0x1E));
        Assert.False(Keys.EatSysChar(handledScan: 0x1E, charScan: 0x30));
        Assert.False(Keys.EatSysChar(handledScan: -1, charScan: 0x1E));
    }
}
