using Winshell.Input;

namespace Winshell.Services.Tests;

[Collection(KeyboardHookCollection.Name)]
public sealed class HotkeyBookTests
{
    [Fact]
    public void Parser_uses_windows_names_and_canonical_order()
    {
        var chord = Parse("shift+alt+ctrl+win+k").Chords[0];
        Assert.Equal("Win+Ctrl+Alt+Shift+K", chord.Text);
        Assert.Equal('K', chord.VirtualKey);
        Assert.Equal(0x000Fu, chord.Win32Modifiers);
        Assert.False(chord.IsWinOnly);

        Assert.Equal("Alt+F4", Parse("alt+f4").Text);
        Assert.Equal(0x73, Parse("alt+f4").Chords[0].VirtualKey);
        Assert.Equal("Ctrl+K, C", Parse("Ctrl+K, C").Text);
        Assert.Equal("Ctrl+Comma, C", Parse("ctrl+comma, c").Text);
        Assert.Equal(0xBC, Parse("Comma").Chords[0].VirtualKey);
        Assert.Equal(0x87, Parse("F24").Chords[0].VirtualKey);
        Assert.Equal(0x2E, Parse("Delete").Chords[0].VirtualKey);
        Assert.Equal(0xAF, Parse("Volume Up").Chords[0].VirtualKey);

        var winE = Parse("Win+E").Chords[0];
        Assert.False(winE.IsWinOnly);
        Assert.Equal('E', winE.VirtualKey);
        Assert.Equal(0x0008u, winE.Win32Modifiers);

        var either = Parse("Win").Chords[0];
        Assert.True(either.IsWinOnly);
        Assert.Equal(WinKeySide.Either, either.Side);
        Assert.Equal("Win", either.Text);
        var left = Parse("Left Win").Chords[0];
        Assert.True(left.IsWinOnly);
        Assert.Equal(WinKeySide.Left, left.Side);
        Assert.Equal("Left Win", left.Text);

        Assert.False(HotkeySequence.TryParse("   ", out var idle, out var idleError));
        Assert.Null(idle);
        Assert.Null(idleError);
        Assert.False(HotkeySequence.TryParse(null, out _, out idleError));
        Assert.Null(idleError);
    }

    [Fact]
    public void Parser_rejects_linux_names_sided_modifiers_and_bad_shapes()
    {
        AssertRejected("Super+K", "Super", "Win");
        AssertRejected("Left Ctrl+K", "Left Ctrl", "Ctrl");
        AssertRejected("Right Alt+K", "Left Alt", "Alt");
        AssertRejected("LShift+K", "Left Shift", "Shift");
        AssertRejected("Left Win+E", "Left Win", "Win");
        AssertRejected("Ctrl+Shift", "needs a key");
        AssertRejected("Ctrl+", "Comma");
        AssertRejected("A, B, C, D, E, F, G, H, I", "8");
        AssertRejected("Ctrl+Ctrl+K", "repeated");
    }

    [Fact]
    public void Two_listeners_share_a_chord_until_the_last_one_releases_it()
    {
        var book = new HotkeyBook();
        var loop = new ManualEventLoop();
        var first = new Probe(loop);
        var second = new Probe(loop);
        var key = Parse("Ctrl+K").Chords[0].OsKey;
        book.Add(first, Parse("Ctrl+K"));
        book.Add(second, Parse("Ctrl+K"));
        var fired = book.Trigger(key, 0);
        Assert.Equal(2, fired.Pressed.Count);
        Assert.Contains(first, fired.Pressed);
        Assert.Contains(second, fired.Pressed);
        book.Remove(first);
        Assert.True(book.HasOs(key));
        book.Remove(second);
        Assert.False(book.HasOs(key));
    }

    [Fact]
    public void Windows_key_tap_fires_that_side_and_either_but_not_the_other_side()
    {
        var book = new HotkeyBook();
        var loop = new ManualEventLoop();
        var left = new Probe(loop);
        var right = new Probe(loop);
        var either = new Probe(loop);
        book.Add(left, Parse("Left Win"));
        book.Add(right, Parse("Right Win"));
        book.Add(either, Parse("Win"));

        var down = book.Hook(VirtualKeys.LeftWin, false, HotkeyModifiers.None, 0);
        Assert.Empty(down.Pressed);
        Assert.False(down.Swallow);

        var up = book.Hook(VirtualKeys.LeftWin, true, HotkeyModifiers.Win, 0);
        Assert.Contains(left, up.Pressed);
        Assert.Contains(either, up.Pressed);
        Assert.DoesNotContain(right, up.Pressed);
        Assert.True(up.Swallow);
    }

    [Fact]
    public void A_handled_windows_tap_is_masked_and_the_key_up_is_not_eaten()
    {
        var book = new HotkeyBook();
        var either = new Probe(new ManualEventLoop());
        book.Add(either, Parse("Win"));
        book.Hook(VirtualKeys.LeftWin, false, HotkeyModifiers.None, 0);
        var up = book.Hook(VirtualKeys.LeftWin, true, HotkeyModifiers.Win, 0);

        Assert.Contains(either, up.Pressed);
        Assert.True(up.MaskWin);
        Assert.True(up.Swallow);
        var injected = 0;
        Assert.False(HotkeyRegistrar.Consume(up, () => injected++));
        Assert.Equal(1, injected);
    }

    [Fact]
    public void An_unused_windows_tap_is_not_masked()
    {
        var book = new HotkeyBook();
        book.Add(new Probe(new ManualEventLoop()), Parse("Win"));
        book.Hook(VirtualKeys.LeftWin, false, HotkeyModifiers.None, 0);
        book.Hook((int)'E', false, HotkeyModifiers.Win, 0);
        var up = book.Hook(VirtualKeys.LeftWin, true, HotkeyModifiers.Win, 0);

        Assert.False(up.MaskWin);
        Assert.False(up.Swallow);
        Assert.False(HotkeyRegistrar.Consume(up));
    }

    [Fact]
    public void A_matched_sequence_key_is_still_eaten()
    {
        var book = new HotkeyBook();
        var listener = new Probe(new ManualEventLoop());
        book.Add(listener, Parse("Ctrl+K, C"));
        book.Trigger(Parse("Ctrl+K").Chords[0].OsKey, 0);
        var hit = book.Hook((int)'C', false, HotkeyModifiers.None, 0);

        Assert.False(hit.MaskWin);
        Assert.True(hit.Swallow);
        Assert.True(HotkeyRegistrar.Consume(hit));
    }

    [Fact]
    public void The_masking_stroke_uses_an_unassigned_key_and_a_hook_tag()
    {
        var stroke = KeyMask.Stroke();
        Assert.Equal(2, stroke.Length);
        Assert.Equal(KeyMask.VirtualKey, stroke[0].VirtualKey);
        Assert.Equal((ushort)0xE8, stroke[0].VirtualKey);
        Assert.False(stroke[0].Up);
        Assert.True(stroke[1].Up);
        Assert.Equal(stroke[0].VirtualKey, stroke[1].VirtualKey);
        Assert.Equal(KeyMask.ExtraInfo, stroke[0].ExtraInfo);
        Assert.Equal(KeyMask.ExtraInfo, stroke[1].ExtraInfo);
        Assert.NotEqual(0, KeyMask.ExtraInfo);
        Assert.True(KeyMask.IsOurs(KeyMask.ExtraInfo));
        Assert.False(KeyMask.IsOurs(0));
        Assert.Equal(nint.Size == 8 ? 40 : 28, KeyMask.InputSize);
    }

    [Fact]
    public void Windows_key_used_as_a_modifier_does_not_fire_and_repeats_do_not_reset_that()
    {
        var book = new HotkeyBook();
        var either = new Probe(new ManualEventLoop());
        book.Add(either, Parse("Win"));
        book.Hook(VirtualKeys.LeftWin, false, HotkeyModifiers.None, 0);
        book.Hook((int)'E', false, HotkeyModifiers.Win, 0);
        book.Hook(VirtualKeys.LeftWin, false, HotkeyModifiers.Win, 0);
        var up = book.Hook(VirtualKeys.LeftWin, true, HotkeyModifiers.Win, 0);
        Assert.Empty(up.Pressed);
        Assert.False(up.Swallow);
    }

    [Fact]
    public void Sequence_swallows_a_match_and_a_mismatch_or_modifier_does_not_steal_the_key()
    {
        var book = new HotkeyBook();
        var listener = new Probe(new ManualEventLoop());
        book.Add(listener, Parse("Ctrl+K, C"));
        book.Trigger(Parse("Ctrl+K").Chords[0].OsKey, 0);
        var shift = book.Hook(0x10, false, HotkeyModifiers.Shift, 0);
        Assert.False(shift.Swallow);
        Assert.True(book.NeedsHook);

        var miss = book.Hook((int)'D', false, HotkeyModifiers.None, 0);
        Assert.False(miss.Swallow);
        Assert.Empty(miss.Pressed);
        Assert.False(book.NeedsHook);

        book.Trigger(Parse("Ctrl+K").Chords[0].OsKey, 0);
        var hit = book.Hook((int)'C', false, HotkeyModifiers.None, 0);
        Assert.True(hit.Swallow);
        Assert.Contains(listener, hit.Pressed);
        Assert.False(book.NeedsHook);
    }

    [Fact]
    public void Extending_a_sequence_keeps_a_new_token_and_the_old_disarm_does_nothing()
    {
        var book = new HotkeyBook();
        var listener = new Probe(new ManualEventLoop());
        book.Add(listener, Parse("A, B, C"));
        var started = book.Trigger(Parse("A").Chords[0].OsKey, 0);
        var token = Assert.Single(started.Armed).Token;
        book.Hook((int)'B', false, HotkeyModifiers.None, 0);
        book.Disarm(listener, token);
        Assert.True(book.NeedsHook);
        var done = book.Hook((int)'C', false, HotkeyModifiers.None, 0);
        Assert.Contains(listener, done.Pressed);
    }

    [Fact]
    public void A_sequence_expires_when_the_deadline_is_reached()
    {
        var book = new HotkeyBook { SequenceTimeout = TimeSpan.FromMilliseconds(1000) };
        var listener = new Probe(new ManualEventLoop());
        book.Add(listener, Parse("A, B"));
        book.Trigger(Parse("A").Chords[0].OsKey, 0);
        var late = book.Hook((int)'B', false, HotkeyModifiers.None, 1000);
        Assert.Empty(late.Pressed);
        Assert.False(late.Swallow);
        Assert.False(book.NeedsHook);
    }

    [Fact]
    public void Windows_tap_finishes_a_waiting_sequence_and_still_starts_one()
    {
        var book = new HotkeyBook();
        var loop = new ManualEventLoop();
        var starter = new Probe(loop);
        var finisher = new Probe(loop);
        book.Add(starter, Parse("Win, K"));
        book.Add(finisher, Parse("Ctrl+K, Win"));
        book.Trigger(Parse("Ctrl+K").Chords[0].OsKey, 0);
        book.Hook(VirtualKeys.LeftWin, false, HotkeyModifiers.None, 0);
        var up = book.Hook(VirtualKeys.LeftWin, true, HotkeyModifiers.Win, 0);
        Assert.Contains(finisher, up.Pressed);
        Assert.Contains(up.Armed, notice => notice.Listener == starter);
        Assert.True(up.Swallow);
        Assert.True(book.NeedsHook);
    }

    [Fact]
    public void Ctrl_Alt_Delete_is_not_registered()
    {
        var loop = new ManualEventLoop();
        using var hotkey = new Hotkey(loop);
        hotkey.Sequence = "Ctrl+Alt+Delete";

        Assert.False(hotkey.Active);
        Assert.Contains("Ctrl+Alt+Delete", hotkey.Error, StringComparison.Ordinal);
        if (hotkey.Error.Contains("already in use", StringComparison.Ordinal))
            Assert.True(hotkey.Taken);
        else
            Assert.False(hotkey.Taken);
    }

    [Fact]
    public void A_second_windows_key_hotkey_waits_until_the_hook_is_free()
    {
        using var first = new Hotkey(new ManualEventLoop());
        using var second = new Hotkey(new ManualEventLoop());
        first.Sequence = "Win";
        Assert.True(first.Active);

        second.Sequence = "Win";
        Assert.False(second.Active);
        Assert.False(second.Taken);
        Assert.Contains("keyboard hook", second.Error, StringComparison.Ordinal);

        first.Dispose();
        second.Enabled = false;
        second.Enabled = true;
        Assert.True(second.Active);
        Assert.Equal("", second.Error);
    }

    private static void AssertRejected(string text, params string[] parts)
    {
        Assert.False(HotkeySequence.TryParse(text, out _, out var error));
        Assert.NotNull(error);
        foreach (var part in parts)
            Assert.Contains(part, error, StringComparison.Ordinal);
    }

    private static HotkeySequence Parse(string text)
    {
        Assert.True(HotkeySequence.TryParse(text, out var sequence, out var error), error);
        return sequence!;
    }

    private sealed class Probe(IEventLoop loop) : IHotkeyListener
    {
        public IEventLoop Loop { get; } = loop;

        public int Presses { get; private set; }

        public void OnPressed(bool deliversRelease)
        {
            Presses++;
        }

        public void OnReleased()
        {
        }
    }
}
