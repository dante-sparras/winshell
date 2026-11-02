using Winshell.DefaultShell.Taskbar;
using Winshell.Input;

namespace Winshell.DefaultShell.Tests;

/// <summary>
/// Win+T, Win+A, Win+N, Win+S and Win+number. The hotkeys are claimed on a fake registrar, so nothing is registered
/// with Windows. Win+Alt+number is not claimed: Windows keeps it.
/// </summary>
public sealed class TaskbarChordTests
{
    [Fact]
    public void The_main_taskbar_claims_each_chord_once_and_not_Win_Alt()
    {
        var expected = new List<string> { "Win+T", "Win+A", "Win+N", "Win+S" };
        for (var number = 1; number <= 9; number++)
            expected.Add("Win+" + number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        expected.Add("Win+0");
        for (var number = 1; number <= 9; number++)
            expected.Add("Win+Shift+" + number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        expected.Add("Win+Shift+0");

        Assert.Equal(expected, TaskbarChords.Sequences);
        Assert.Equal(expected.Count, expected.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(TaskbarChords.Sequences, sequence => sequence.Contains("Alt", StringComparison.Ordinal));
        Assert.DoesNotContain(TaskbarChords.Sequences, DesktopChords.Sequences.Contains);

        var fake = new FakeHotkeys();
        var actions = new Actions();
        using var registration = TaskbarChords.Register(() => new Hotkey(new ManualEventLoop(), new Log(), fake), actions);

        Assert.Equal(expected, fake.Claims.Select(claim => claim.Text));
        fake.Press("Win+T");
        fake.Press("Win+A");
        fake.Press("Win+N");
        fake.Press("Win+S");
        fake.Press("Win+1");
        fake.Press("Win+0");
        fake.Press("Win+Shift+2");

        Assert.Equal(1, actions.Focus);
        Assert.Equal(1, actions.Quick);
        Assert.Equal(1, actions.Notifications);
        Assert.Equal(1, actions.Search);
        Assert.Equal([(0, false), (9, false), (1, true)], actions.Invoked);
    }

    [Fact]
    public void A_chord_Explorer_keeps_is_reported_and_does_not_throw()
    {
        var messages = new List<(LogLevel Level, string Category, string Message)>();
        var log = new Log
        {
            MinimumLevel = LogLevel.Warn,
            Sink = (level, category, message) => messages.Add((level, category, message)),
        };
        var fake = new FakeHotkeys();
        fake.Taken.Add("Win+T");

        using var registration = TaskbarChords.Register(() => new Hotkey(new ManualEventLoop(), log, fake), new Actions());

        Assert.DoesNotContain(fake.Claims, claim => claim.Text == "Win+T");
        Assert.Contains(fake.Claims, claim => claim.Text == "Win+S");
        Assert.Contains(messages, message =>
            message.Level == LogLevel.Warn && message.Category == "hotkey" && message.Message.Contains("Win+T", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Level is LogLevel.Error or LogLevel.Fatal);
    }

    [Fact]
    public void Desktop_chords_are_absent_until_the_preference_is_on_and_Win_S_stays()
    {
        var fake = new FakeHotkeys();
        var loop = new ManualEventLoop();
        var log = new Log();
        using var taskbar = TaskbarChords.Register(() => new Hotkey(loop, log, fake), new Actions());
        using var session = new DesktopChordSession();

        session.Sync(false, () => new Hotkey(loop, log, fake), () => [], () => null, () => true);

        Assert.Contains("Win+S", fake.Claims.Select(claim => claim.Text));
        Assert.All(DesktopChords.Sequences, sequence => Assert.DoesNotContain(fake.Claims, claim => claim.Text == sequence));

        session.Sync(true, () => new Hotkey(loop, log, fake), () => [], () => null, () => true);
        session.Sync(true, () => new Hotkey(loop, log, fake), () => [], () => null, () => true);
        Assert.All(DesktopChords.Sequences, sequence => Assert.Contains(fake.Claims, claim => claim.Text == sequence));
        Assert.Equal(DesktopChords.Sequences.Count, fake.Claims.Count(claim => DesktopChords.Sequences.Contains(claim.Text)));

        session.Sync(false, () => new Hotkey(loop, log, fake), () => [], () => null, () => true);
        Assert.All(DesktopChords.Sequences, sequence => Assert.DoesNotContain(fake.Claims, claim => claim.Text == sequence));
        Assert.Contains("Win+S", fake.Claims.Select(claim => claim.Text));
        Assert.False(session.Switcher.SwitcherOpen);
        Assert.False(session.Switcher.PowerOpen);
    }

    private sealed class Actions : ITaskbarChordActions
    {
        public int Focus { get; private set; }

        public int Quick { get; private set; }

        public int Notifications { get; private set; }

        public int Search { get; private set; }

        public List<(int Index, bool NewWindow)> Invoked { get; } = [];

        public void FocusApps() => Focus++;

        public void ToggleQuickSettings() => Quick++;

        public void ToggleNotifications() => Notifications++;

        public void OpenSearch() => Search++;

        public void InvokeApp(int index, bool newWindow) => Invoked.Add((index, newWindow));
    }
}
