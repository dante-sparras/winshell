using Winshell.Input;

namespace Winshell.Services.Tests;

/// <summary>
/// A hotkey over a stand-in for Windows, so no chord is registered and no hook is installed. The claim rules run for real.
/// </summary>
public sealed class HotkeyTests : IDisposable
{
    private readonly ManualEventLoop _loop = new();
    private readonly FakeBackend _backend = new();
    private readonly HotkeyRegistrar _registrar;
    private readonly List<string> _warnings = [];
    private readonly Log _log;

    public HotkeyTests()
    {
        _log = new Log { Sink = (_, category, message) => _warnings.Add($"{category}: {message}") };
        _registrar = new HotkeyRegistrar(_loop, _log, _backend);
    }

    public void Dispose()
    {
        _registrar.Dispose();
    }

    private Hotkey Create()
    {
        return new Hotkey(_loop, _log, _registrar);
    }

    [Fact]
    public void A_sequence_claims_at_once_and_a_new_one_replaces_it()
    {
        using var hotkey = Create();

        hotkey.Sequence = "ctrl+shift+k";
        Assert.Equal(Key("Ctrl+Shift+K"), Assert.Single(_backend.Registered));
        Assert.True(hotkey.Active);

        hotkey.Sequence = "alt+f4";
        Assert.Equal(Key("Alt+F4"), Assert.Single(_backend.Registered));
    }

    [Fact]
    public void Turning_it_off_gives_the_keys_back_and_on_claims_them_again()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";

        hotkey.Enabled = false;
        Assert.Empty(_backend.Registered);
        Assert.False(hotkey.Active);

        hotkey.Enabled = true;
        Assert.Equal(Key("Ctrl+K"), Assert.Single(_backend.Registered));
    }

    [Fact]
    public void An_empty_sequence_is_idle()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";

        hotkey.Sequence = "   ";

        Assert.Empty(_backend.Registered);
        Assert.False(hotkey.Active);
        Assert.Equal("", hotkey.Error);
    }

    [Fact]
    public void A_sequence_that_cannot_be_read_says_why_even_while_off()
    {
        using var hotkey = Create();
        hotkey.Enabled = false;

        hotkey.Sequence = "Super+K";

        Assert.Contains("Win", hotkey.Error, StringComparison.Ordinal);
        Assert.Empty(_backend.Registered);
        Assert.Contains(_warnings, warning => warning.StartsWith("hotkey: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chord_someone_else_owns_is_reported_and_not_active()
    {
        _backend.Taken = "Alt+F4";
        using var hotkey = Create();

        hotkey.Sequence = "alt+f4";

        Assert.True(hotkey.Taken);
        Assert.False(hotkey.Active);
        Assert.Contains("already in use", hotkey.Error, StringComparison.Ordinal);
        Assert.Empty(_backend.Registered);
    }

    [Fact]
    public void Two_hotkeys_share_a_chord_and_the_last_release_gives_it_back()
    {
        using var first = Create();
        using var second = Create();
        var presses = new List<string>();
        first.Pressed += (_, _) => presses.Add("first");
        second.Pressed += (_, _) => presses.Add("second");
        first.Sequence = "Ctrl+K";
        second.Sequence = "Ctrl+K";
        var key = Key("Ctrl+K");
        Assert.Equal(key, Assert.Single(_backend.Registered));
        Assert.False(_backend.HookInstalled);

        _backend.Press(key);
        Assert.Equal(["first", "second"], presses);
        _loop.Advance(TimeSpan.FromMilliseconds(20));

        second.Enabled = false;
        Assert.Equal(key, Assert.Single(_backend.Registered));
        presses.Clear();
        _backend.Press(key);
        Assert.Equal(["first"], presses);

        first.Dispose();
        Assert.Empty(_backend.Registered);
        presses.Clear();
        _backend.Press(key);
        Assert.Empty(presses);
    }

    [Fact]
    public void The_hook_is_installed_only_while_a_claim_needs_it()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";
        Assert.True(hotkey.Active);
        Assert.False(_backend.HookInstalled);

        hotkey.Sequence = "Left Win";
        Assert.True(hotkey.Active);
        Assert.True(_backend.HookInstalled);
        Assert.Empty(_backend.Registered);

        hotkey.Enabled = false;
        Assert.False(_backend.HookInstalled);

        hotkey.Enabled = true;
        Assert.True(_backend.HookInstalled);
    }

    [Fact]
    public void A_sequence_holds_the_hook_until_the_next_chord_arrives()
    {
        using var hotkey = Create();
        var presses = 0;
        hotkey.Pressed += (_, _) => presses++;
        hotkey.Sequence = "Ctrl+K, C";
        Assert.False(_backend.HookInstalled);

        _backend.Press(Key("Ctrl+K"));
        Assert.True(_backend.HookInstalled);
        Assert.Equal(0, presses);

        Assert.True(_backend.Key('C', false));
        _loop.Process();
        Assert.Equal(1, presses);
        Assert.Equal(0, _backend.Masks);
        Assert.False(_backend.HookInstalled);
    }

    [Fact]
    public void A_later_chord_matches_the_modifiers_held_on_either_side()
    {
        using var hotkey = Create();
        var presses = 0;
        hotkey.Pressed += (_, _) => presses++;
        hotkey.Sequence = "Ctrl+K, Ctrl+Shift+C";
        _backend.Press(Key("Ctrl+K"));
        _backend.Held.UnionWith([0xA3, 0xA0]);

        Assert.True(_backend.Key('C', false));
        _loop.Process();

        Assert.Equal(1, presses);
    }

    [Fact]
    public void A_handled_windows_tap_is_masked_and_its_key_up_passed_on()
    {
        using var hotkey = Create();
        var presses = 0;
        hotkey.Pressed += (_, _) => presses++;
        hotkey.Sequence = "Win";

        Assert.False(_backend.Key(0x5B, false));
        _backend.Held.Add(0x5B);
        Assert.False(_backend.Key(0x5B, true));
        _loop.Process();

        Assert.Equal(1, presses);
        Assert.Equal(1, _backend.Masks);
    }

    [Fact]
    public void A_windows_key_used_as_a_modifier_is_not_masked()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Win";
        _backend.Key(0x5B, false);
        _backend.Held.Add(0x5B);

        Assert.False(_backend.Key('E', false));
        Assert.False(_backend.Key(0x5B, true));

        Assert.Equal(0, _backend.Masks);
    }

    [Fact]
    public void A_lost_hook_is_installed_again_while_a_claim_needs_it()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Win";

        _backend.LoseHook();

        Assert.True(_backend.HookInstalled);
    }

    [Fact]
    public void A_sequence_drops_the_hook_when_its_deadline_passes()
    {
        using var hotkey = Create();
        var presses = 0;
        hotkey.Pressed += (_, _) => presses++;
        hotkey.Sequence = "A, B";
        _backend.Press(Key("A"));
        Assert.True(_backend.HookInstalled);

        _loop.Advance(TimeSpan.FromSeconds(1));

        Assert.False(_backend.HookInstalled);
        Assert.False(_backend.Key('B', false));
        _loop.Process();
        Assert.Equal(0, presses);
    }

    [Fact]
    public void A_windows_key_hotkey_fails_when_the_hook_cannot_be_installed()
    {
        _backend.HookFails = true;
        using var hotkey = Create();

        hotkey.Sequence = "Win";

        Assert.False(hotkey.Active);
        Assert.False(hotkey.Taken);
        Assert.Contains("keyboard hook", hotkey.Error, StringComparison.Ordinal);
        Assert.False(_backend.HookInstalled);

        hotkey.Sequence = "Ctrl+K";
        Assert.True(hotkey.Active);
        Assert.Equal("", hotkey.Error);
        Assert.False(_backend.HookInstalled);
    }

    [Fact]
    public void Press_and_release_raise_their_events_once_each()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";
        var events = new List<string>();
        hotkey.Pressed += (_, _) => events.Add($"pressed {hotkey.IsPressed}");
        hotkey.Released += (_, _) => events.Add($"released {hotkey.IsPressed}");
        IHotkeyListener listener = hotkey;

        listener.OnPressed(true);
        listener.OnPressed(true);
        listener.OnReleased();
        listener.OnReleased();

        Assert.Equal(["pressed True", "released False"], events);
    }

    [Fact]
    public void Without_a_key_up_from_Windows_the_release_follows_the_keys()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";
        var released = 0;
        hotkey.Released += (_, _) => released++;
        _backend.Held.UnionWith(['K', 0x11]);

        ((IHotkeyListener)hotkey).OnPressed(false);
        _loop.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(hotkey.IsPressed);

        _backend.Held.Remove('K');
        _loop.Advance(TimeSpan.FromMilliseconds(20));

        Assert.False(hotkey.IsPressed);
        Assert.Equal(1, released);
        Assert.Null(_loop.NextDue);
    }

    [Fact]
    public void Changing_the_sequence_while_pressed_releases_first()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";
        var released = 0;
        hotkey.Released += (_, _) => released++;
        ((IHotkeyListener)hotkey).OnPressed(true);

        hotkey.Sequence = "Ctrl+L";

        Assert.False(hotkey.IsPressed);
        Assert.Equal(1, released);
    }

    [Fact]
    public void Disposing_releases_the_keys_without_a_released_event()
    {
        var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";
        var released = 0;
        hotkey.Released += (_, _) => released++;
        ((IHotkeyListener)hotkey).OnPressed(false);

        hotkey.Dispose();
        hotkey.Sequence = "Ctrl+L";

        Assert.Empty(_backend.Registered);
        Assert.Equal(0, released);
        Assert.Null(_loop.NextDue);
    }

    private static OsChord Key(string text)
    {
        Assert.True(HotkeySequence.TryParse(text, out var sequence, out var error), error);
        return sequence!.Chords[0].OsKey;
    }

    private sealed class FakeBackend : IHotkeyBackend
    {
        private Func<int, bool, bool>? _onKey;
        private Action? _onLost;

        public string? Taken { get; set; }

        public bool HookFails { get; set; }

        public HashSet<OsChord> Registered { get; } = [];

        public HashSet<int> Held { get; } = [];

        public bool HookInstalled { get; private set; }

        public int Masks { get; private set; }

        public event Action<OsChord>? ChordPressed;

        public HotkeyClaim Register(HotkeyChord chord)
        {
            if (Taken == chord.Text)
                return HotkeyClaim.Taken($"{chord.Text} is already in use by Windows or another app.");
            Registered.Add(chord.OsKey);
            return HotkeyClaim.Active;
        }

        public void Unregister(OsChord chord)
        {
            Registered.Remove(chord);
        }

        public bool InstallHook(Func<int, bool, bool> onKey, Action onLost)
        {
            if (HookFails && !HookInstalled)
                return false;
            _onKey = onKey;
            _onLost = onLost;
            HookInstalled = true;
            return true;
        }

        public void RemoveHook()
        {
            HookInstalled = false;
            _onKey = null;
            _onLost = null;
        }

        public bool IsKeyDown(int virtualKey)
        {
            return Held.Contains(virtualKey);
        }

        public void SendMask()
        {
            Masks++;
        }

        public void Press(OsChord chord)
        {
            ChordPressed?.Invoke(chord);
        }

        // Whether the hook swallowed the key.
        public bool Key(int virtualKey, bool up)
        {
            return HookInstalled && _onKey!(virtualKey, up);
        }

        public void LoseHook()
        {
            var onLost = _onLost;
            RemoveHook();
            onLost!();
        }

        public void Dispose()
        {
        }
    }
}
