using Winshell.Input;
using Winshell.Native;

namespace Winshell.Services.Tests;

public sealed class HotkeyBookTests : IDisposable
{
    public void Dispose()
    {
        var leaked = WindowsHotkeyRegistrar.IsHookInstalled;
        WindowsHotkeyRegistrar.EmergencyUnhook();
        Assert.False(leaked);
    }

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

        var down = book.Hook(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None, 0);
        Assert.Empty(down.Pressed);
        Assert.False(down.Swallow);

        var up = book.Hook(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win, 0);
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
        book.Hook(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None, 0);
        var up = book.Hook(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win, 0);

        Assert.Contains(either, up.Pressed);
        Assert.True(up.MaskWin);
        Assert.True(up.Swallow);
        var injected = 0;
        Assert.False(WindowsHotkeyRegistrar.Consume(up, () => injected++));
        Assert.Equal(1, injected);
    }

    [Fact]
    public void An_unused_windows_tap_is_not_masked()
    {
        var book = new HotkeyBook();
        book.Add(new Probe(new ManualEventLoop()), Parse("Win"));
        book.Hook(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None, 0);
        book.Hook((int)'E', isUp: false, HotkeyModifiers.Win, 0);
        var up = book.Hook(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win, 0);

        Assert.False(up.MaskWin);
        Assert.False(up.Swallow);
        Assert.False(WindowsHotkeyRegistrar.Consume(up));
    }

    [Fact]
    public void A_matched_sequence_key_is_still_eaten()
    {
        var book = new HotkeyBook();
        var listener = new Probe(new ManualEventLoop());
        book.Add(listener, Parse("Ctrl+K, C"));
        book.Trigger(Parse("Ctrl+K").Chords[0].OsKey, 0);
        var hit = book.Hook((int)'C', isUp: false, HotkeyModifiers.None, 0);

        Assert.False(hit.MaskWin);
        Assert.True(hit.Swallow);
        Assert.True(WindowsHotkeyRegistrar.Consume(hit));
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
        book.Hook(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None, 0);
        book.Hook((int)'E', isUp: false, HotkeyModifiers.Win, 0);
        book.Hook(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.Win, 0);
        var up = book.Hook(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win, 0);
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
        var shift = book.Hook(0x10, isUp: false, HotkeyModifiers.Shift, 0);
        Assert.False(shift.Swallow);
        Assert.True(book.NeedsHook);

        var miss = book.Hook((int)'D', isUp: false, HotkeyModifiers.None, 0);
        Assert.False(miss.Swallow);
        Assert.Empty(miss.Pressed);
        Assert.False(book.NeedsHook);

        book.Trigger(Parse("Ctrl+K").Chords[0].OsKey, 0);
        var hit = book.Hook((int)'C', isUp: false, HotkeyModifiers.None, 0);
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
        book.Hook((int)'B', isUp: false, HotkeyModifiers.None, 0);
        book.Disarm(listener, token);
        Assert.True(book.NeedsHook);
        var done = book.Hook((int)'C', isUp: false, HotkeyModifiers.None, 0);
        Assert.Contains(listener, done.Pressed);
    }

    [Fact]
    public void A_sequence_expires_when_the_deadline_is_reached()
    {
        var book = new HotkeyBook { SequenceTimeout = TimeSpan.FromMilliseconds(1000) };
        var listener = new Probe(new ManualEventLoop());
        book.Add(listener, Parse("A, B"));
        book.Trigger(Parse("A").Chords[0].OsKey, 0);
        var late = book.Hook((int)'B', isUp: false, HotkeyModifiers.None, 1000);
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
        book.Hook(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None, 0);
        var up = book.Hook(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win, 0);
        Assert.Contains(finisher, up.Pressed);
        Assert.Contains(up.Armed, notice => notice.Listener == starter);
        Assert.True(up.Swallow);
        Assert.True(book.NeedsHook);
    }

    [Fact]
    public void RegisterHotKey_reports_a_taken_chord_and_shares_one_registration()
    {
        var loop = new ManualEventLoop();
        using var registrar = new WindowsHotkeyRegistrar(loop);
        var text = ClaimableChord(registrar, loop);
        var sequence = Parse(text);
        var first = new Probe(loop);
        var second = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(first, sequence).Status);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(second, sequence).Status);
        Assert.Equal(1, registrar.RegistrationCount);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        Assert.True(registrar.TryGetId(sequence.Chords[0].OsKey, out var id));

        Win32.SendMessageW(registrar.Window, Win32.WM_HOTKEY, id, 0);
        loop.Process();
        Assert.Equal(1, first.Presses);
        Assert.Equal(1, second.Presses);

        registrar.Release(first);
        Assert.Equal(1, registrar.RegistrationCount);
        Assert.NotEqual(0, registrar.Window);
        registrar.Release(second);
        Assert.Equal(0, registrar.RegistrationCount);
        Assert.Equal(0, registrar.Window);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
    }

    [Fact]
    public void Ctrl_Alt_Delete_is_not_registered()
    {
        var loop = new ManualEventLoop();
        using var registrar = new WindowsHotkeyRegistrar(loop);
        var probe = new Probe(loop);
        var claim = registrar.Claim(probe, Parse("Ctrl+Alt+Delete"));
        try
        {
            Assert.NotEqual(HotkeyClaimStatus.Active, claim.Status);
            Assert.Contains("Ctrl+Alt+Delete", claim.Error, StringComparison.Ordinal);
            if (claim.Error.Contains("already in use", StringComparison.Ordinal))
                Assert.Equal(HotkeyClaimStatus.Taken, claim.Status);
            else
                Assert.Equal(HotkeyClaimStatus.Invalid, claim.Status);
        }
        finally
        {
            registrar.Release(probe);
        }
        Assert.Equal(0, registrar.RegistrationCount);
    }

    [Fact]
    public void Windows_key_installs_a_hook_and_dispose_removes_it()
    {
        var loop = new ManualEventLoop();
        var probe = new Probe(loop);
        var registrar = new WindowsHotkeyRegistrar(loop);
        try
        {
            var claim = registrar.Claim(probe, Parse("Left Win"));
            Assert.Equal(HotkeyClaimStatus.Active, claim.Status);
            Assert.Equal(0, registrar.RegistrationCount);
            Assert.True(WindowsHotkeyRegistrar.IsHookInstalled);
        }
        finally
        {
            registrar.Dispose();
        }
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);

        registrar = new WindowsHotkeyRegistrar(loop);
        try
        {
            Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(new Probe(loop), Parse("Win")).Status);
            Assert.True(WindowsHotkeyRegistrar.IsHookInstalled);
        }
        finally
        {
            registrar.Dispose();
        }
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
    }

    [Fact]
    public void The_keyboard_hook_runs_on_its_own_thread_until_it_is_removed()
    {
        var loop = new ManualEventLoop();
        var registrar = new WindowsHotkeyRegistrar(loop);
        Thread? thread;
        try
        {
            Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(new Probe(loop), Parse("Win")).Status);
            thread = WindowsHotkeyRegistrar.InstalledHookThread;
            Assert.NotNull(thread);
            Assert.NotEqual(Environment.CurrentManagedThreadId, thread.ManagedThreadId);
            Assert.True(thread.IsAlive);
        }
        finally
        {
            registrar.Dispose();
        }
        Assert.False(thread!.IsAlive);
        Assert.Null(WindowsHotkeyRegistrar.InstalledHookThread);

        registrar = new WindowsHotkeyRegistrar(loop);
        try
        {
            Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(new Probe(loop), Parse("Win")).Status);
            thread = WindowsHotkeyRegistrar.InstalledHookThread;
            Assert.NotNull(thread);
            WindowsHotkeyRegistrar.EmergencyUnhook();
            Assert.False(thread.IsAlive);
            Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        }
        finally
        {
            registrar.Dispose();
        }
    }

    [Fact]
    public void A_sequence_arms_without_a_hook_when_native_install_is_off()
    {
        var loop = new ManualEventLoop();
        using var registrar = new WindowsHotkeyRegistrar(loop) { InstallNativeHook = false };
        var text = ClaimableChord(registrar, loop);
        var sequence = Parse(text + ", C");
        var probe = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(probe, sequence).Status);
        Assert.False(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        Assert.True(registrar.TryGetId(sequence.Chords[0].OsKey, out var id));

        registrar.OnHotkey(id);
        Assert.True(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        Assert.Equal(0, probe.Presses);

        Assert.True(registrar.HandleKey((int)'C', isUp: false, HotkeyModifiers.None));
        Assert.Equal(1, probe.Presses);
        Assert.False(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
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

    /// <summary>Claims and releases an unused chord so the test can register it. Fails if F21 through F24 are all taken.</summary>
    private static string ClaimableChord(WindowsHotkeyRegistrar registrar, ManualEventLoop loop)
    {
        var probe = new Probe(loop);
        foreach (var name in new[] { "F24", "F23", "F22", "F21" })
        {
            var text = "Ctrl+Alt+Shift+" + name;
            var claim = registrar.Claim(probe, Parse(text));
            registrar.Release(probe);
            if (claim.Status == HotkeyClaimStatus.Active)
                return text;
        }
        throw new InvalidOperationException("Ctrl+Alt+Shift+F21 through F24 are already registered.");
    }

    private sealed class Probe(IEventLoop loop) : IHotkeyListener
    {
        public IEventLoop Loop { get; } = loop;

        public int Presses { get; private set; }

        public void OnPressed(bool deliversRelease) => Presses++;

        public void OnReleased()
        {
        }
    }
}
