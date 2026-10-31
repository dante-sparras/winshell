using Winshell.Platform.Windows.Services.Input;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// Alt+Tab, Win+Tab, Win+D and Win+X. The preference is off unless the test turns it on, and the hotkeys are claimed
/// on a fake registrar, so this never registers them with Windows or touches a real window.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class DesktopChordTests
{
    private static readonly string[] Chords = ["Alt+Tab", "Alt+Shift+Tab", "Win+Tab", "Win+D", "Win+X"];

    [Fact]
    public void The_chords_register_nothing_while_the_preference_is_off()
    {
        using var run = new RunningShell(chords: false);

        var claims = run.Hotkeys.Claims.Select(claim => claim.Text).ToHashSet();
        Assert.Contains("Win+S", claims);
        Assert.All(Chords, chord => Assert.DoesNotContain(chord, claims));
        var sequences = Walk(run.Host.Root!).OfType<Hotkey>().Select(hotkey => hotkey.Sequence).ToHashSet();
        Assert.All(Chords, chord => Assert.DoesNotContain(chord, sequences));
        Assert.DoesNotContain(run.Platform.Windows, window => window.Client is QObject obj && obj.ObjectName is "windowSwitcher" or "powerMenu");
        Assert.Empty(run.Services.Desktops.Calls);
    }

    [Fact]
    public void The_chords_switch_minimize_and_open_the_menu_on_fakes()
    {
        using var run = new RunningShell(chords: true);
        var inbox = run.Inbox;
        var notes = run.Notes;

        Assert.All(Chords, chord => Assert.Contains(chord, run.Hotkeys.Claims.Select(claim => claim.Text)));
        var switcher = run.Popup("windowSwitcher");
        var power = run.Popup("powerMenu");
        Assert.False(switcher.Visible);
        Assert.False(power.Visible);
        Assert.Empty(run.Urls.Opened);

        run.Press("Alt+Tab");
        Assert.True(switcher.Visible);
        Assert.True(Chosen(run.Card(switcher, "switch:Notes")));
        Assert.False(Chosen(run.Card(switcher, "switch:Inbox")));

        run.Press("Alt+Tab");
        Assert.True(Chosen(run.Card(switcher, "switch:Inbox")));

        run.Press("Alt+Shift+Tab");
        Assert.True(Chosen(run.Card(switcher, "switch:Notes")));
        switcher.Scene.KeyPress(new KeyEvent { Key = 0x01000004 });
        run.Settle();
        Assert.False(switcher.Visible);
        Assert.Contains("activate", notes.Calls);
        Assert.DoesNotContain("activate", inbox.Calls);

        run.Press("Win+D");
        Assert.False(switcher.Visible);
        Assert.Contains("minimize", inbox.Calls);
        Assert.Contains("minimize", notes.Calls);

        run.Press("Win+D");
        Assert.Contains("restore", inbox.Calls);
        Assert.Contains("restore", notes.Calls);

        run.Press("Win+Tab");
        Assert.True(switcher.Visible);
        Assert.True(Chosen(run.Card(switcher, "switch:Inbox")));
        Assert.True(run.Card(switcher, "desktop:Desktop 1").Visible);
        Assert.True(run.Card(switcher, "desktop:Desktop 2").Visible);
        run.Click(switcher, run.Card(switcher, "desktop:Desktop 2"));
        Assert.Contains("switch:Desktop 2", run.Services.Desktops.Calls);
        Assert.True(switcher.Visible);

        switcher.Scene.KeyPress(new KeyEvent { Key = 0x01000000 });
        run.Settle();
        Assert.False(switcher.Visible);

        run.Press("Win+X");
        Assert.True(power.Visible);
        Assert.False(switcher.Visible);
        run.Click(power, run.Card(power, "power:Settings"));
        Assert.Contains("ms-settings:", run.Urls.Opened);
        Assert.False(power.Visible);
        Assert.DoesNotContain(run.Messages, message =>
            !message.Contains("Unable to assign", StringComparison.Ordinal)
            && !message.StartsWith("hotkey:", StringComparison.Ordinal));
    }

    private static bool Chosen(Item item) => (bool)item.ReadProperty("chosen")!;

    private static IEnumerable<QObject> Walk(QObject obj)
    {
        yield return obj;
        foreach (var owned in obj.OwnedObjects)
        {
            foreach (var nested in Walk(owned))
                yield return nested;
        }
    }

    /// <summary>The default shell, with its folders in a temporary directory and its hotkeys claimed by <see cref="FakeHotkeys"/>.</summary>
    private sealed class RunningShell : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-desktop-chord-tests", Guid.NewGuid().ToString("N"));
        private readonly Theme _theme = Theme.Current;

        public RunningShell(bool chords)
        {
            var shell = Path.Combine(_directory, "shell");
            var config = Path.Combine(_directory, "config");
            Directory.CreateDirectory(shell);
            Directory.CreateDirectory(config);
            Theme.Current = Theme.Default(false) with { AnimationsEnabled = false };
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "DefaultShell")))
            {
                var target = Path.Combine(shell, Path.GetFileName(file));
                if (Path.GetFileName(file) == "shell.qml")
                {
                    var text = File.ReadAllText(file).Replace("//@ pragma ShellId winshell-default", $"""
                        //@ pragma ShellId winshell-desktop-chords-{Guid.NewGuid():N}
                        //@ pragma DataDir {Path.Combine(_directory, "data")}
                        //@ pragma StateDir {Path.Combine(_directory, "state")}
                        //@ pragma CacheDir {Path.Combine(_directory, "cache")}
                        """, StringComparison.Ordinal);
                    File.WriteAllText(target, text);
                }
                else
                {
                    File.Copy(file, target);
                }
            }
            if (chords)
                File.WriteAllText(Path.Combine(config, "preferences.toml"), "desktopChords = true\n");

            Services = new FakeServices();
            Inbox = new FakeAppWindow("Inbox", "inbox");
            Notes = new FakeAppWindow("Notes", "notes");
            Services.WindowList.Windows.Add(Inbox);
            Services.WindowList.Windows.Add(Notes);
            Services.WindowList.Activate(Inbox);
            if (chords)
            {
                Services.Desktops.Add("Desktop 1");
                Services.Desktops.Add("Desktop 2");
            }

            var log = new Log { Sink = (_, category, message) => Messages.Add($"{category}: {message}"), MinimumLevel = LogLevel.Warn };
            Host = new ShellHost(Path.Combine(shell, "shell.qml"), Loop, Platform, log, () =>
            {
                var registry = StandardModules.Create();
                WindowsModules.Register(registry);
                ServiceModules.Register(registry);
                Services.Register(registry);
                return registry;
            })
            { ConfigRoot = config };
            Host.Services.Add(engine =>
            {
                engine.AddService<IHotkeyRegistrar>(Hotkeys);
                engine.AddService<IPlatformServices>(Urls);
            });
            Host.WatchFiles = false;
            Assert.True(Host.Start(), string.Join(Environment.NewLine, Messages));
            Settle();
        }

        public FakeServices Services { get; }

        public FakeAppWindow Inbox { get; }

        public FakeAppWindow Notes { get; }

        public FakeHotkeys Hotkeys { get; } = new();

        public FakeUrls Urls { get; } = new();

        public RecordingPlatform Platform { get; } = new();

        public ManualEventLoop Loop { get; } = new();

        public ShellHost Host { get; }

        public List<string> Messages { get; } = [];

        public PopupWindow Popup(string name) =>
            Platform.Windows.Select(window => window.Client).OfType<PopupWindow>().Single(popup => popup.ObjectName == name);

        public Item Card(PopupWindow popup, string name) =>
            WalkItems(popup.ContentItem).Single(item => item.ObjectName == name);

        public void Press(string sequence)
        {
            var hotkey = Walk(Host.Root!).OfType<Hotkey>().Single(item => item.Sequence == sequence);
            // A chord comes up before the next press. OnPressed does not emit again while it is still down.
            Hotkeys.Press(hotkey);
            Hotkeys.ReleaseKey(hotkey);
            Settle();
        }

        public void Click(ShellWindow window, Item item)
        {
            var at = item.MapToScene(item.Width / 2, item.Height / 2);
            window.Scene.PointerPress(at.X, at.Y, QtEnums.LeftButton);
            window.Scene.PointerRelease(at.X, at.Y, QtEnums.LeftButton);
            Settle();
        }

        public void Settle()
        {
            Loop.Process();
            PolishQueue.Flush();
            Loop.Process();
        }

        public void Dispose()
        {
            Host.Dispose();
            Theme.Current = _theme;
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
        }

        private static IEnumerable<Item> WalkItems(Item item)
        {
            yield return item;
            foreach (var child in item.ChildItems)
            {
                foreach (var found in WalkItems(child))
                    yield return found;
            }
        }
    }

    /// <summary>Records claims and delivers a press without calling <c>RegisterHotKey</c>.</summary>
    private sealed class FakeHotkeys : IHotkeyRegistrar
    {
        public List<(IHotkeyListener Listener, string Text)> Claims { get; } = [];

        public HotkeyClaim Claim(IHotkeyListener listener, HotkeySequence? sequence)
        {
            Release(listener);
            if (sequence is null || sequence.Chords.Count == 0)
                return HotkeyClaim.Idle;
            Claims.Add((listener, sequence.Text));
            return HotkeyClaim.Active;
        }

        public void Release(IHotkeyListener listener) => Claims.RemoveAll(claim => claim.Listener == listener);

        public void Press(IHotkeyListener listener) => listener.OnPressed(true);

        public void ReleaseKey(IHotkeyListener listener) => listener.OnReleased();
    }
}
