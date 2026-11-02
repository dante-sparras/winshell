using System.ComponentModel;
using Winshell.DefaultShell.Taskbar;
using Winshell.Input;

namespace Winshell.DefaultShell.Tests;

/// <summary>
/// Alt+Tab, Win+Tab, Win+D and Win+X. The preference is off unless the test turns it on, and the hotkeys are claimed
/// on a fake registrar, so this never registers them with Windows or touches a real window.
/// </summary>
public sealed class DesktopChordTests
{
    private static readonly string[] Chords = ["Alt+Tab", "Alt+Shift+Tab", "Win+Tab", "Win+D", "Win+X"];

    [Fact]
    public void The_chords_register_nothing_while_the_preference_is_off()
    {
        using var session = new DesktopChordSession();
        var fake = new FakeHotkeys();
        session.Sync(false, () => Hotkey(fake), () => [], () => null, () => true);

        Assert.Empty(fake.Claims);
        Assert.False(session.Switcher.SwitcherOpen);
        Assert.False(session.Switcher.PowerOpen);
    }

    [Fact]
    public void The_chords_switch_minimize_and_open_the_menu_on_fakes()
    {
        var inbox = new Win("Inbox");
        var notes = new Win("Notes");
        var windows = new List<IOpenWindow> { inbox, notes };
        var target = new Target();
        var messages = new List<string>();
        var log = new Log
        {
            MinimumLevel = LogLevel.Warn,
            Sink = (level, category, message) => messages.Add($"{level} {category}: {message}"),
        };
        var fake = new FakeHotkeys();
        using var session = new DesktopChordSession();
        session.Sync(true, () => new Hotkey(new ManualEventLoop(), log, fake), () => windows, () => inbox, () => true);

        Assert.All(Chords, chord => Assert.Contains(fake.Claims, claim => claim.Text == chord));
        Assert.False(session.Switcher.SwitcherOpen);
        Assert.False(session.Switcher.PowerOpen);
        Assert.Empty(target.Urls);

        fake.Press("Alt+Tab");
        Assert.True(session.Switcher.SwitcherOpen);
        Assert.Same(notes, session.Switcher.Chosen(windows));

        fake.Press("Alt+Tab");
        Assert.Same(inbox, session.Switcher.Chosen(windows));

        fake.Press("Alt+Shift+Tab");
        Assert.Same(notes, session.Switcher.Chosen(windows));
        session.Switcher.Choose(session.Switcher.Chosen(windows));
        Assert.False(session.Switcher.SwitcherOpen);
        Assert.Equal(1, notes.Activations);
        Assert.Equal(0, inbox.Activations);

        fake.Press("Win+D");
        Assert.False(session.Switcher.SwitcherOpen);
        Assert.Equal(1, inbox.Minimizations);
        Assert.Equal(1, notes.Minimizations);
        Assert.True(inbox.Minimized);
        Assert.True(notes.Minimized);

        fake.Press("Win+D");
        Assert.Equal(1, inbox.Restores);
        Assert.Equal(1, notes.Restores);
        Assert.False(inbox.Minimized);
        Assert.False(notes.Minimized);

        fake.Press("Win+Tab");
        Assert.True(session.Switcher.SwitcherOpen);
        Assert.Same(inbox, session.Switcher.Chosen(windows));
        Assert.True(DesktopChordLayout.ShowDesktops(supported: true, count: 2));
        session.Switcher.CloseSwitcher();
        Assert.False(session.Switcher.SwitcherOpen);

        fake.Press("Win+X");
        Assert.True(session.Switcher.PowerOpen);
        Assert.False(session.Switcher.SwitcherOpen);
        session.Switcher.Run(DesktopSwitcher.Commands.Single(command => command.Name == "Settings"), target, windows);
        Assert.Contains("ms-settings:", target.Urls);
        Assert.False(session.Switcher.PowerOpen);
        Assert.DoesNotContain(messages, message => message.StartsWith("Error", StringComparison.Ordinal) || message.StartsWith("Fatal", StringComparison.Ordinal));
    }

    [Fact]
    public void Win_D_skips_a_window_that_was_already_minimized_or_has_since_closed()
    {
        var open = new Win("Open");
        var minimized = new Win("Minimized") { Minimized = true };
        var closing = new Win("Closing");
        var windows = new List<IOpenWindow> { open, minimized, closing };
        var switcher = new DesktopSwitcher();

        switcher.ShowDesktop(windows);

        Assert.Equal(1, open.Minimizations);
        Assert.Equal(1, closing.Minimizations);
        Assert.Equal(0, minimized.Minimizations);
        Assert.Equal([open, closing], switcher.Parked);

        windows.Remove(closing);
        switcher.ShowDesktop(windows);

        Assert.Equal(1, open.Restores);
        Assert.Equal(0, closing.Restores);
        Assert.Equal(0, minimized.Restores);
        Assert.True(minimized.Minimized);
        Assert.Empty(switcher.Parked);
    }

    [Fact]
    public void An_empty_list_still_opens_and_a_missing_window_starts_at_the_end_going_back()
    {
        var inbox = new Win("Inbox");
        var notes = new Win("Notes");
        var windows = new List<IOpenWindow> { inbox, notes };
        var switcher = new DesktopSwitcher();

        switcher.Cycle(1, [], null);
        Assert.True(switcher.SwitcherOpen);
        Assert.Equal(0, switcher.Selected);
        Assert.Null(switcher.Chosen([]));

        switcher.CloseSwitcher();
        switcher.Cycle(-1, windows, null);
        Assert.Same(notes, switcher.Chosen(windows));

        switcher.CloseSwitcher();
        switcher.Cycle(1, windows, null);
        Assert.Same(inbox, switcher.Chosen(windows));
    }

    [Fact]
    public void Win_X_stays_closed_without_a_taskbar_and_each_row_does_its_own_job()
    {
        var switcher = new DesktopSwitcher();
        var target = new Target();
        var window = new Win("Inbox");
        var windows = new List<IOpenWindow> { window };

        switcher.ToggleTaskView(windows, window);
        switcher.TogglePower(false);
        Assert.False(switcher.PowerOpen);
        Assert.False(switcher.SwitcherOpen);

        switcher.TogglePower(true);
        Assert.True(switcher.PowerOpen);
        Assert.Equal(
            ["Installed apps", "System", "Network", "Settings", "File Explorer", "Search", "Desktop", "Taskbar settings"],
            DesktopSwitcher.Commands.Select(command => command.Name));

        switcher.Run(Command("Installed apps"), target, windows);
        switcher.Run(Command("File Explorer"), target, windows);
        switcher.Run(Command("Search"), target, windows);
        switcher.Run(Command("Taskbar settings"), target, windows);
        Assert.Equal(["ms-settings:appsfeatures"], target.Urls);
        Assert.Equal(1, target.Explorers);
        Assert.Equal(1, target.Searches);
        Assert.Equal(1, target.Settings);
        Assert.False(switcher.PowerOpen);

        switcher.Run(Command("Desktop"), target, windows);
        Assert.Equal(1, window.Minimizations);
        Assert.False(switcher.SwitcherOpen);
    }

    [Fact]
    public void The_switcher_sits_above_the_taskbar_and_the_menu_at_its_top_left()
    {
        var bar = new QRect(100, 1000, 1600, 48);
        var switcher = DesktopChordLayout.Switcher(bar, 1920, windows: 2, desktops: true);
        var power = DesktopChordLayout.Power(bar, DesktopSwitcher.Commands.Count);

        Assert.True(switcher.Y < bar.Y);
        Assert.True(switcher.Y + switcher.Height <= bar.Y);
        Assert.Equal(bar.X, power.X);
        Assert.True(power.Y < bar.Y);
        Assert.Equal(280, power.Width);
        Assert.False(DesktopChordLayout.ShowDesktops(supported: false, count: 2));
        Assert.False(DesktopChordLayout.ShowDesktops(supported: true, count: 0));
    }

    private static PowerCommand Command(string name) => DesktopSwitcher.Commands.Single(command => command.Name == name);

    private static Hotkey Hotkey(FakeHotkeys fake) => new(new ManualEventLoop(), new Log(), fake);

    private sealed class Target : IDesktopCommandTarget
    {
        public List<string> Urls { get; } = [];

        public int Explorers { get; private set; }

        public int Searches { get; private set; }

        public int Settings { get; private set; }

        public void OpenUrl(string url) => Urls.Add(url);

        public void OpenExplorer() => Explorers++;

        public void OpenSearch() => Searches++;

        public void OpenSettings() => Settings++;
    }

    private sealed class Win(string title) : IOpenWindow
    {
        public string AppId => "";

        public string Executable => "";

        public string Title { get; } = title;

        public string Icon => "";

        public bool Activated => false;

        public bool Minimized { get; set; }

        public bool Attention => false;

        public IReadOnlyList<ScreenInfo> Screens => [];

        public int Activations { get; private set; }

        public int Minimizations { get; private set; }

        public int Restores { get; private set; }

        private PropertyChangedEventHandler? _changed;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void Activate() => Activations++;

        public void Minimize()
        {
            Minimized = true;
            Minimizations++;
        }

        public void Restore()
        {
            Minimized = false;
            Restores++;
        }

        public void Close()
        {
        }
    }
}
