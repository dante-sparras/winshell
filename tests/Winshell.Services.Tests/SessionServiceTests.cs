using System.ComponentModel;
using Winshell.ExplorerTaskbar;
using Winshell.Session;
using Winshell.Updates;

namespace Winshell.Services.Tests;

/// <summary>Locking, sign-in, Explorer's taskbar and updates, each over a fake. Nothing here changes this PC.</summary>
public sealed class SessionServiceTests
{
    private readonly List<string> _warnings = [];
    private readonly Log _log;

    public SessionServiceTests() => _log = new Log { Sink = (_, category, message) => _warnings.Add($"{category}: {message}") };

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void Lock_asks_Windows_once()
    {
        var windows = new FakeLock(accepts: true);

        Assert.True(new SessionService(_log, windows).Lock());

        Assert.Equal(1, windows.Calls);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Lock_reports_when_Windows_refuses()
    {
        Assert.False(new SessionService(_log, new FakeLock(accepts: false)).Lock());

        Assert.Equal(["session: Windows did not lock the session."], _warnings);
    }

    [Fact]
    public void Nothing_is_registered_until_the_shell_turns_it_on_and_off_removes_it()
    {
        var windows = new FakeRegistry();
        var startup = new StartupService(_log, windows);
        var changes = Watch(startup);

        Assert.True(startup.Available);
        Assert.False(startup.RunAtSignIn);
        Assert.Equal(0, windows.Writes);

        startup.RunAtSignIn = true;
        Assert.True(windows.Registered);
        Assert.True(startup.RunAtSignIn);

        startup.RunAtSignIn = false;
        Assert.False(windows.Registered);
        Assert.Equal(2, windows.Writes);
        Assert.Equal([nameof(StartupService.RunAtSignIn), nameof(StartupService.RunAtSignIn)], changes);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void The_Startup_apps_page_turning_it_off_is_reported_and_left_alone()
    {
        var windows = new FakeRegistry { Registered = true, TurnedOff = true };
        var startup = new StartupService(_log, windows);
        Assert.True(startup.RunAtSignIn);
        Assert.True(startup.TurnedOffInWindows);

        windows.TurnedOff = false;
        startup.Refresh();

        Assert.False(startup.TurnedOffInWindows);
        Assert.Equal(0, windows.Writes);
    }

    [Fact]
    public void A_refused_write_keeps_the_state_Windows_has_and_warns()
    {
        var startup = new StartupService(_log, new FakeRegistry { Accepts = false });

        startup.RunAtSignIn = true;

        Assert.False(startup.RunAtSignIn);
        Assert.Equal(["startup: Windows did not let Winshell register to run at sign-in."], _warnings);
    }

    [Fact]
    public void An_entry_that_starts_another_winshell_is_not_this_one_running_at_sign_in()
    {
        var windows = new FakeRegistry { Registered = true, Elsewhere = true };
        var startup = new StartupService(_log, windows);

        Assert.True(startup.RegisteredElsewhere);
        Assert.False(startup.RunAtSignIn);

        startup.RunAtSignIn = true;

        Assert.Equal(1, windows.Writes);
        Assert.False(startup.RegisteredElsewhere);
        Assert.True(startup.RunAtSignIn);
    }

    [Fact]
    public void Without_a_registry_it_is_unavailable_and_registers_nothing()
    {
        var startup = new StartupService(_log, null);

        startup.RunAtSignIn = true;

        Assert.False(startup.Available);
        Assert.False(startup.RunAtSignIn);
        Assert.Single(_warnings);
    }

    [Fact]
    public void Hiding_Explorers_taskbar_goes_through_the_session_and_reports_the_change()
    {
        var taskbar = new FakeTaskbar();
        using var session = new ExplorerTaskbarSession(() => taskbar);
        var service = new ExplorerTaskbarService(session);
        var changes = Watch(service);

        service.Hidden = false;
        Assert.Equal(0, taskbar.Hides + taskbar.Restores);
        Assert.Empty(changes);

        service.Hidden = true;
        service.Hidden = true;
        Assert.True(session.Hidden);
        Assert.Equal(1, taskbar.Hides);

        service.Hidden = false;
        Assert.Equal(1, taskbar.Restores);
        Assert.Equal([nameof(ExplorerTaskbarService.Hidden), nameof(ExplorerTaskbarService.Hidden)], changes);
    }

    [Fact]
    public void A_Winshell_that_does_not_update_itself_says_so()
    {
        using var updates = new UpdatesService(new ManualEventLoop(), _log, null);

        updates.Automatic = true;
        updates.Check();
        updates.Restart();

        Assert.False(updates.Available);
        Assert.False(updates.Automatic);
        Assert.Equal(UpdateStatus.Idle, updates.Status);
        Assert.Equal(["updates: This Winshell does not update itself.", "updates: No update is ready."], _warnings);
    }

    [Fact]
    public void A_change_of_the_updater_reaches_the_service_on_its_loop()
    {
        var loop = new ManualEventLoop();
        using var updater = NewUpdater();
        using var updates = new UpdatesService(loop, _log, updater);
        var changes = Watch(updates);
        Assert.True(updates.Available);

        updater.ReportApplyFailure("1.2.3", "the file is locked");
        Assert.Empty(changes);
        loop.Process();

        Assert.Equal([nameof(UpdatesService.Status), nameof(UpdatesService.Version), nameof(UpdatesService.Error)], changes);
        Assert.Equal(UpdateStatus.Failed, updates.Status);
        Assert.Equal("1.2.3", updates.Version);
        Assert.Contains("the file is locked", updates.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Automatic_checks_follow_the_updater()
    {
        using var updater = NewUpdater();
        using var updates = new UpdatesService(new ManualEventLoop(), _log, updater);
        var changes = Watch(updates);

        updates.Automatic = true;
        updates.Automatic = true;
        Assert.True(updater.Automatic);
        updates.Automatic = false;

        Assert.Equal([nameof(UpdatesService.Automatic), nameof(UpdatesService.Automatic)], changes);
    }

    [Fact]
    public void Restart_without_a_ready_update_warns()
    {
        using var updater = NewUpdater();
        var restarts = 0;
        updater.Restart = () =>
        {
            restarts++;
            return null;
        };
        using var updates = new UpdatesService(new ManualEventLoop(), _log, updater);

        updates.Restart();

        Assert.Equal(0, restarts);
        Assert.Equal(["updates: No update is ready."], _warnings);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_updater()
    {
        var loop = new ManualEventLoop();
        using var updater = NewUpdater();
        var updates = new UpdatesService(loop, _log, updater);

        updater.ReportApplyFailure("1.2.3", "before");
        updates.Dispose();
        updater.ReportApplyFailure("1.2.4", "after");
        loop.Process();

        Assert.Equal(UpdateStatus.Idle, updates.Status);
    }

    private Updater NewUpdater()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winshell-updates-service-tests", Guid.NewGuid().ToString("N"));
        return new Updater("dante-sparras/winshell", folder, folder, "0.5.0", "win-x64", _log);
    }

    private sealed class FakeLock(bool accepts) : IWorkstationLock
    {
        public int Calls { get; private set; }

        public bool Lock()
        {
            Calls++;
            return accepts;
        }
    }

    private sealed class FakeRegistry : IStartupRegistry
    {
        public bool Registered { get; set; }

        public bool TurnedOff { get; set; }

        public bool Elsewhere { get; set; }

        public bool Accepts { get; set; } = true;

        public int Writes { get; private set; }

        public bool IsRegistered() => Registered;

        public bool PointsElsewhere() => Elsewhere;

        public bool TurnedOffInWindows() => TurnedOff;

        public bool Register()
        {
            if (!Accepts)
                return false;
            Writes++;
            Registered = true;
            Elsewhere = false;
            return true;
        }

        public bool Unregister()
        {
            if (!Accepts)
                return false;
            Writes++;
            Registered = false;
            return true;
        }
    }

    private sealed class FakeTaskbar : IExplorerTaskbar
    {
        public int Hides { get; private set; }

        public int Restores { get; private set; }

        public void Hide() => Hides++;

        public void Restore() => Restores++;
    }
}
