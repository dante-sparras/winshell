using Winshell.Input;

namespace Winshell.Services.Tests;

/// <summary>A hotkey over a stand-in registrar, so no chord is registered with Windows and no hook is installed.</summary>
public sealed class HotkeyTests
{
    private readonly ManualEventLoop _loop = new();
    private readonly FakeHotkeys _fake = new();
    private readonly List<string> _warnings = [];
    private readonly HashSet<int> _held = [];

    private Hotkey Create() => new(_loop, new Log { Sink = (_, category, message) => _warnings.Add($"{category}: {message}") }, _fake, _held.Contains);

    [Fact]
    public void A_sequence_claims_at_once_and_a_new_one_replaces_it()
    {
        using var hotkey = Create();

        hotkey.Sequence = "ctrl+shift+k";
        Assert.Equal("Ctrl+Shift+K", Assert.Single(_fake.Claims).Text);
        Assert.True(hotkey.Active);

        hotkey.Sequence = "alt+f4";
        Assert.Equal("Alt+F4", Assert.Single(_fake.Claims).Text);
    }

    [Fact]
    public void Turning_it_off_gives_the_keys_back_and_on_claims_them_again()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";

        hotkey.Enabled = false;
        Assert.Empty(_fake.Claims);
        Assert.False(hotkey.Active);

        hotkey.Enabled = true;
        Assert.Single(_fake.Claims);
    }

    [Fact]
    public void An_empty_sequence_is_idle()
    {
        using var hotkey = Create();
        hotkey.Sequence = "Ctrl+K";

        hotkey.Sequence = "   ";

        Assert.Empty(_fake.Claims);
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
        Assert.Empty(_fake.Claims);
        Assert.Contains(_warnings, warning => warning.StartsWith("hotkey: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chord_someone_else_owns_is_reported_and_not_active()
    {
        _fake.Taken = "Alt+F4";
        using var hotkey = Create();

        hotkey.Sequence = "alt+f4";

        Assert.True(hotkey.Taken);
        Assert.False(hotkey.Active);
        Assert.Contains("already in use", hotkey.Error, StringComparison.Ordinal);
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
        _held.UnionWith(['K', 0x11]);

        ((IHotkeyListener)hotkey).OnPressed(false);
        _loop.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(hotkey.IsPressed);

        _held.Remove('K');
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

        Assert.Empty(_fake.Claims);
        Assert.Equal(0, released);
        Assert.Null(_loop.NextDue);
    }

    [Fact]
    public void Hotkeys_on_a_loop_share_a_registrar_that_holds_nothing_until_a_claim()
    {
        var registrar = WindowsHotkeyRegistrar.For(_loop);

        Assert.Same(registrar, WindowsHotkeyRegistrar.For(_loop));
        Assert.NotSame(registrar, WindowsHotkeyRegistrar.For(new ManualEventLoop()));
        Assert.Equal(0, registrar.Window);
        Assert.False(registrar.NeedsHook);
    }

    private sealed class FakeHotkeys : IHotkeyRegistrar
    {
        public string? Taken { get; set; }

        public List<(IHotkeyListener Listener, string Text)> Claims { get; } = [];

        public HotkeyClaim Claim(IHotkeyListener listener, HotkeySequence? sequence)
        {
            Release(listener);
            if (sequence is null || sequence.Chords.Count == 0)
                return HotkeyClaim.Idle;
            if (Taken == sequence.Text)
                return HotkeyClaim.Taken($"{sequence.Text} is already in use by Windows or another app.");
            Claims.Add((listener, sequence.Text));
            return HotkeyClaim.Active;
        }

        public void Release(IHotkeyListener listener) => Claims.RemoveAll(claim => claim.Listener == listener);
    }
}
