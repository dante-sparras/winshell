using System.Globalization;
using Winshell.Native;
using Winshell.Platform.Windows.Services.Input;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

[Collection(ThemeCollection.Name)]
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

    [Fact]
    public void Foreground_loss_pauses_capture_and_regaining_it_resumes()
    {
        using var shell = new Shell();
        shell.Arm();
        Assert.True(shell.Inhibitor.Active);
        Assert.True(shell.Inhibitor.Enabled);
        Assert.Equal(0, Cancels(shell));

        shell.Front = false;
        Assert.False(shell.Registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None));
        Assert.Empty(shell.Posts);
        shell.Loop.Process();
        Assert.False(shell.Inhibitor.Active);
        Assert.True(shell.Inhibitor.Enabled);
        Assert.True(shell.Registrar.NeedsHook);
        Assert.Equal(0, Cancels(shell));

        shell.Front = true;
        Assert.True(shell.Registrar.HandleKey((int)'A', isUp: false, HotkeyModifiers.None));
        shell.Loop.Process();
        Assert.True(shell.Inhibitor.Active);
        Assert.Equal((nint)'A', Assert.Single(shell.Posts).WParam);

        shell.Window.OnActiveChanged(false);
        Assert.False(shell.Inhibitor.Active);
        Assert.False(shell.Registrar.NeedsHook);
        Assert.True(shell.Inhibitor.Enabled);
        Assert.Equal(0, Cancels(shell));

        shell.Window.OnActiveChanged(true);
        Assert.True(shell.Inhibitor.Active);
        Assert.True(shell.Inhibitor.Enabled);
        Assert.Equal(0, Cancels(shell));
    }

    [Fact]
    public void A_lost_hook_thread_cancels_once_and_enabling_again_resumes()
    {
        using var shell = new Shell();
        shell.Arm();
        shell.Registrar.NoteHookLost();
        shell.Loop.Process();

        Assert.False(shell.Inhibitor.Enabled);
        Assert.False(shell.Inhibitor.Active);
        Assert.False(shell.Registrar.NeedsHook);
        Assert.Equal(1, Cancels(shell));

        shell.Registrar.NoteHookLost();
        shell.Loop.Process();
        Assert.Equal(1, Cancels(shell));

        shell.Inhibitor.Enabled = true;
        Assert.True(shell.Inhibitor.Active);
        Assert.True(shell.Inhibitor.Enabled);
        shell.Registrar.NoteHookLost();
        Assert.False(shell.Inhibitor.Enabled);
        Assert.Equal(2, Cancels(shell));
    }

    [Fact]
    public void Destroying_the_window_the_inhibitor_or_the_registrar_ends_capture()
    {
        using (var shell = new Shell())
        {
            shell.Arm();
            shell.Window.Destroy();
            Assert.False(shell.Inhibitor.Active);
            Assert.True(shell.Inhibitor.Enabled);
            Assert.False(shell.Registrar.NeedsHook);
            Assert.Equal(0, Cancels(shell));
        }

        using (var shell = new Shell())
        {
            shell.Arm();
            shell.Inhibitor.Destroy();
            Assert.False(shell.Registrar.NeedsHook);
            Assert.Equal(0, Cancels(shell));
        }

        using var exit = new Shell();
        exit.Arm();
        exit.Registrar.Dispose();
        Assert.False(exit.Inhibitor.Active);
        Assert.True(exit.Inhibitor.Enabled);
        Assert.Equal(0, Cancels(exit));
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
    }

    [Fact]
    public void Qml_exposes_the_quickshell_members_and_a_refused_hook_warns_once()
    {
        using var shell = new Shell(installNativeHook: true, allowHookInstall: false);
        var inhibitor = shell.Inhibitor;
        Assert.False(inhibitor.Enabled);
        Assert.False(inhibitor.Active);
        Assert.Same(shell.Window, inhibitor.Window);
        var type = MetaType.Of(typeof(ShortcutInhibitor));
        Assert.False(type.Properties["active"].CanWrite);
        Assert.True(type.Properties["enabled"].CanWrite);
        Assert.True(type.Properties["window"].CanWrite);
        Assert.NotNull(inhibitor.GetSignal("cancelled"));
        Assert.Throws<QmlException>(() => inhibitor.WriteProperty("active", true));

        shell.Arm();
        Assert.False(inhibitor.Active);
        Assert.True(inhibitor.Enabled);
        Assert.False(shell.Registrar.NeedsHook);
        Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        Assert.Equal(1, Warnings(shell, "did not install"));

        shell.Window.Emit("windowConnected");
        Assert.Equal(1, Warnings(shell, "did not install"));
        Assert.Equal(0, Cancels(shell));
    }

    [Fact]
    public void A_missing_registrar_warns_once_and_stays_inactive()
    {
        var warnings = new List<string>();
        var loop = new ManualEventLoop();
        var directory = Path.Combine(Path.GetTempPath(), "winshell-inhibitor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var engine = new QmlEngine(Registry(), loop, new Log { Sink = (_, category, message) => warnings.Add(category + ":" + message) }, directory);
        try
        {
            var root = (ShellRoot)engine.CreateRoot(WriteShell(directory));
            var window = Assert.IsType<FloatingWindow>(root.Children.Snapshot[0]);
            var inhibitor = Assert.IsType<ShortcutInhibitor>(root.Children.Snapshot[1]);
            inhibitor.HandleOf = _ => 0x42;
            window.OnActiveChanged(true);
            inhibitor.Enabled = true;
            Assert.False(inhibitor.Active);
            Assert.True(inhibitor.Enabled);
            Assert.Equal(1, warnings.Count(warning => warning.Contains("cannot be inhibited", StringComparison.Ordinal)));
            window.Emit("windowConnected");
            Assert.Equal(1, warnings.Count(warning => warning.Contains("cannot be inhibited", StringComparison.Ordinal)));
            Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
        }
        finally
        {
            engine.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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

    private static int Cancels(Shell shell) =>
        Convert.ToInt32(shell.Root.ReadProperty("cancels"), CultureInfo.InvariantCulture);

    private static int Warnings(Shell shell, string text) =>
        shell.Warnings.Count(warning => warning.Contains(text, StringComparison.Ordinal));

    private static ModuleRegistry Registry()
    {
        var registry = StandardModules.Create();
        InputModule.Register(registry);
        return registry;
    }

    private static string WriteShell(string directory)
    {
        var path = Path.Combine(directory, "shell.qml");
        File.WriteAllText(path, """
            import QtQuick
            import Winshell
            import Winshell.Input
            ShellRoot {
                id: root
                property int cancels: 0
                FloatingWindow { id: game }
                ShortcutInhibitor {
                    id: inhibitor
                    window: game
                    onCancelled: root.cancels = root.cancels + 1
                }
            }
            """);
        return path;
    }

    private sealed class Shell : IDisposable
    {
        private readonly string _directory;
        private bool _disposed;

        public Shell(bool installNativeHook = false, bool allowHookInstall = true)
        {
            Loop = new ManualEventLoop();
            Registrar = new WindowsHotkeyRegistrar(Loop)
            {
                InstallNativeHook = installNativeHook,
                AllowHookInstall = allowHookInstall,
            };
            Registrar.IsForeground = hwnd => Front && hwnd == Hwnd;
            Registrar.HeldByWindows = _ => false;
            Registrar.ForwardKey = key =>
            {
                Posts.Add(key);
                return true;
            };
            var log = new Log { Sink = (_, category, message) => Warnings.Add(category + ":" + message) };
            _directory = Path.Combine(Path.GetTempPath(), "winshell-inhibitor-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var registry = Registry();
            Engine = new QmlEngine(registry, Loop, log, _directory);
            Engine.AddService<IHotkeyRegistrar>(Registrar);
            Root = (ShellRoot)Engine.CreateRoot(WriteShell(_directory));
            Window = Assert.IsType<FloatingWindow>(Root.Children.Snapshot[0]);
            Inhibitor = Assert.IsType<ShortcutInhibitor>(Root.Children.Snapshot[1]);
            Inhibitor.HandleOf = _ => Hwnd;
        }

        public const nint Hwnd = 0x5100;

        public ManualEventLoop Loop { get; }

        public WindowsHotkeyRegistrar Registrar { get; }

        public QmlEngine Engine { get; }

        public ShellRoot Root { get; }

        public FloatingWindow Window { get; }

        public ShortcutInhibitor Inhibitor { get; }

        public List<PostedKey> Posts { get; } = [];

        public List<string> Warnings { get; } = [];

        public bool Front { get; set; } = true;

        public void Arm()
        {
            Window.OnActiveChanged(true);
            Inhibitor.Enabled = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Engine.Dispose();
            Registrar.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
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
