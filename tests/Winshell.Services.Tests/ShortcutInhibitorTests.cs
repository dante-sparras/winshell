using Winshell.Input;
using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>The hotkey registrar captures keys for one window and posts them on.</summary>
[Collection(KeyboardHookCollection.Name)]
public sealed class ShortcutInhibitorTests : IDisposable
{
    public void Dispose()
    {
        var leaked = WindowsHotkeyRegistrar.IsHookInstalled;
        WindowsHotkeyRegistrar.EmergencyUnhook();
        Assert.False(leaked);
    }

    [Fact]
    public void The_hook_is_needed_only_while_a_window_is_captured()
    {
        var loop = new ManualEventLoop();
        using var registrar = Registrar(loop);
        var client = new Client();

        Assert.False(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        Assert.True(registrar.SetInhibition(client, 0x42));
        Assert.True(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        Assert.Equal(0, registrar.RegistrationCount);
        Assert.Equal(0, registrar.Window);

        Assert.True(registrar.SetInhibition(client, 0));
        Assert.False(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
    }

    [Fact]
    public void Captured_keys_are_posted_and_a_windows_hotkey_waits_until_capture_ends()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        var probe = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(probe, Parse("Win")).Status);
        var client = new Client();
        Assert.True(registrar.SetInhibition(client, 0x42));

        Assert.True(registrar.HandleKey(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None));
        Assert.True(registrar.HandleKey(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win));
        Assert.Equal(0, probe.Presses);
        Assert.Equal(2, posts.Count);
        Assert.Equal(Win32.WM_KEYDOWN, posts[0].Message);
        Assert.Equal(Win32.WM_KEYUP, posts[1].Message);
        Assert.Equal((nint)VirtualKeys.LeftWin, posts[0].WParam);
        Assert.Equal((nint)0x42, posts[0].Hwnd);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);

        Assert.True(registrar.SetInhibition(client, 0));
        Assert.False(registrar.HandleKey(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win));
        Assert.Equal(0, probe.Presses);
        Assert.False(registrar.HandleKey(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None));
        Assert.True(registrar.HandleKey(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win));
        Assert.Equal(1, probe.Presses);
    }

    [Fact]
    public void Inhibition_drops_a_waiting_sequence_and_posts_the_next_key()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        var probe = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(probe, Parse("Win, K")).Status);
        Assert.False(registrar.HandleKey(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None));
        Assert.True(registrar.HandleKey(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win));
        Assert.Equal(0, probe.Presses);
        Assert.True(registrar.NeedsHook);

        var client = new Client();
        Assert.True(registrar.SetInhibition(client, 0x42));
        Assert.True(registrar.HandleKey((int)'K', isUp: false, HotkeyModifiers.None));
        Assert.Equal(0, probe.Presses);
        Assert.Equal((nint)'K', Assert.Single(posts).WParam);

        Assert.True(registrar.SetInhibition(client, 0));
        Assert.False(registrar.HandleKey((int)'K', isUp: false, HotkeyModifiers.None));
        Assert.Equal(0, probe.Presses);
        registrar.Release(probe);
        Assert.False(registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
    }

    [Fact]
    public void Win_L_and_Ctrl_Alt_Del_are_passed_on()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        var probe = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(probe, Parse("Win")).Status);
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        Assert.False(registrar.HandleKey(0x4C, isUp: false, HotkeyModifiers.Win));
        Assert.False(registrar.HandleKey(0x2E, isUp: false, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt));
        Assert.Empty(posts);
        Assert.Equal(0, probe.Presses);

        Assert.True(registrar.HandleKey(0x4C, isUp: false, HotkeyModifiers.None));
        Assert.True(registrar.HandleKey(0x2E, isUp: false, HotkeyModifiers.Ctrl));
        Assert.Equal(2, posts.Count);
        Assert.Equal(0, probe.Presses);
    }

    [Theory]
    [InlineData(0xA0)] // Left Shift
    [InlineData(0xA3)] // Right Ctrl
    [InlineData(0xA4)] // Left Alt
    [InlineData(0xA5)] // Right Alt, also AltGr
    [InlineData(0x14)] // Caps Lock
    [InlineData(0x90)] // Num Lock
    public void Keys_a_window_reads_from_its_key_state_stay_with_windows(int virtualKey)
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        var probe = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(probe, Parse("Win")).Status);
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        Assert.False(registrar.HandleKey(virtualKey, isUp: false, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(virtualKey, isUp: true, HotkeyModifiers.None));
        Assert.Empty(posts);
        Assert.Equal(0, probe.Presses);
    }

    [Fact]
    public void A_key_windows_saw_go_down_comes_up_through_windows()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        var held = new HashSet<int> { 0x09 };
        registrar.HeldByWindows = held.Contains;
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        Assert.False(registrar.HandleKey(0x09, isUp: false, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(0x09, isUp: true, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.None));
        Assert.Empty(posts);

        held.Clear();
        Assert.True(registrar.HandleKey(0x09, isUp: false, HotkeyModifiers.None));
        Assert.True(registrar.HandleKey(0x09, isUp: true, HotkeyModifiers.None));
        Assert.Equal(2, posts.Count);
    }

    [Fact]
    public void A_key_the_window_stopped_tracking_at_foreground_loss_comes_up_through_windows()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        var front = true;
        registrar.IsForeground = _ => front;
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        Assert.True(registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None));
        front = false;
        Assert.False(registrar.HandleKey((int)'A', isUp: true, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None));
        front = true;
        Assert.False(registrar.HandleKey((int)'A', isUp: true, HotkeyModifiers.None));
        Assert.Single(posts);
    }

    [Fact]
    public void Win_L_is_passed_on_while_the_window_holds_the_windows_key()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        // Windows does not report a key it never saw, so the modifiers read in the hook say None here.
        Assert.True(registrar.HandleKey(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(0x4C, isUp: false, HotkeyModifiers.None));
        Assert.Single(posts);
    }

    [Fact]
    public void A_key_that_cannot_be_posted_is_left_for_the_hotkey()
    {
        var loop = new ManualEventLoop();
        using var registrar = Registrar(loop);
        registrar.ForwardKey = _ => false;
        var probe = new Probe(loop);
        Assert.Equal(HotkeyClaimStatus.Active, registrar.Claim(probe, Parse("Win")).Status);
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        Assert.False(registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None));
        Assert.False(registrar.HandleKey(VirtualKeys.LeftWin, isUp: false, HotkeyModifiers.None));
        Assert.True(registrar.HandleKey(VirtualKeys.LeftWin, isUp: true, HotkeyModifiers.Win));
        Assert.Equal(1, probe.Presses);
    }

    [Fact]
    public void Posted_keys_use_the_window_message_layout()
    {
        var loop = new ManualEventLoop();
        var posts = new List<PostedKey>();
        using var registrar = Registrar(loop, posts);
        Assert.True(registrar.SetInhibition(new Client(), 0x42));

        Assert.True(registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None, scanCode: 0x1E));
        Assert.Equal(Win32.WM_KEYDOWN, posts[0].Message);
        Assert.Equal(0x001E0001L, (long)posts[0].LParam);

        Assert.True(registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None, scanCode: 0x1E));
        Assert.Equal(0x401E0001L, (long)posts[1].LParam);

        Assert.True(registrar.HandleKey((int)'A', isUp: true, HotkeyModifiers.None, scanCode: 0x1E));
        Assert.Equal(Win32.WM_KEYUP, posts[2].Message);
        Assert.Equal(0xC01E0001L, (long)posts[2].LParam);

        Assert.True(registrar.HandleKey((int)'B', isUp: false, HotkeyModifiers.Alt, scanCode: 0x1E, altDown: true, extended: true));
        Assert.Equal(Win32.WM_SYSKEYDOWN, posts[3].Message);
        Assert.Equal(0x211E0001L, (long)posts[3].LParam);
        Assert.Equal((nint)'B', posts[3].WParam);
    }

    private static WindowsHotkeyRegistrar Registrar(ManualEventLoop loop, List<PostedKey>? posts = null)
    {
        var registrar = new WindowsHotkeyRegistrar(loop) { InstallNativeHook = false };
        registrar.IsForeground = hwnd => hwnd == 0x42;
        registrar.HeldByWindows = _ => false;
        registrar.ForwardKey = key =>
        {
            posts?.Add(key);
            return true;
        };
        return registrar;
    }

    private static HotkeySequence Parse(string text)
    {
        Assert.True(HotkeySequence.TryParse(text, out var sequence, out var error), error);
        return sequence!;
    }

    private sealed class Client : IInhibitClient
    {
        public void Cancelled()
        {
        }

        public void Recheck()
        {
        }
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
