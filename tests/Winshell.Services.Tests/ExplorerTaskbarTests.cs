using System.ComponentModel;
using Winshell.ExplorerTaskbar;

namespace Winshell.Services.Tests;

/// <summary>Hiding, re-hiding and restoring Explorer's taskbar go through the module, against a stand-in.</summary>
public class ExplorerTaskbarTests
{
    private readonly List<string> _warnings = [];

    private sealed class Fake : IExplorerTaskbarBackend
    {
        public event Action? TaskbarMayHaveChanged;

        public int Hides { get; private set; }

        public int Restores { get; private set; }

        public bool FailHide { get; set; }

        public bool FailRestore { get; set; }

        public void Hide()
        {
            Hides++;
            if (FailHide)
                throw new InvalidOperationException("The taskbar could not be hidden.");
        }

        public void Restore()
        {
            Restores++;
            if (FailRestore)
                throw new InvalidOperationException("The taskbar could not be shown.");
        }

        public void Dispose()
        {
        }

        public void ExplorerMadeATaskbar()
        {
            TaskbarMayHaveChanged?.Invoke();
        }
    }

    private ExplorerTaskbarService Service(Fake fake)
    {
        var log = new Log { Sink = (_, category, message) => _warnings.Add($"{category}: {message}") };
        return new ExplorerTaskbarService(new ManualEventLoop(), log, fake);
    }

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? "");
        return changes;
    }

    [Fact]
    public void Leaving_it_shown_never_calls_the_taskbar_and_exit_does_not_either()
    {
        var fake = new Fake();
        var taskbar = Service(fake);
        var changes = Watch(taskbar);

        taskbar.Hidden = false;
        fake.ExplorerMadeATaskbar();
        taskbar.Restore();
        taskbar.Dispose();
        taskbar.Dispose();

        Assert.False(taskbar.Hidden);
        Assert.Equal(0, fake.Hides);
        Assert.Equal(0, fake.Restores);
        Assert.Empty(changes);
    }

    [Fact]
    public void Hiding_and_restoring_each_happen_once_and_exit_puts_it_back()
    {
        var fake = new Fake();
        var taskbar = Service(fake);
        var changes = Watch(taskbar);

        taskbar.Hidden = true;
        taskbar.Hidden = true;
        Assert.True(taskbar.Hidden);
        Assert.Equal(1, fake.Hides);
        Assert.Equal(0, fake.Restores);

        taskbar.Hidden = false;
        taskbar.Hidden = false;
        Assert.False(taskbar.Hidden);
        Assert.Equal(1, fake.Restores);

        taskbar.Hidden = true;
        taskbar.Dispose();
        taskbar.Dispose();
        Assert.Equal(2, fake.Hides);
        Assert.Equal(2, fake.Restores);
        Assert.Equal([
            nameof(ExplorerTaskbarService.Hidden), nameof(ExplorerTaskbarService.Hidden),
            nameof(ExplorerTaskbarService.Hidden)
        ], changes);
    }

    [Fact]
    public void A_taskbar_Explorer_makes_is_hidden_while_hidden_and_never_otherwise()
    {
        var fake = new Fake();
        var taskbar = Service(fake);

        fake.ExplorerMadeATaskbar();
        Assert.Equal(0, fake.Hides);

        taskbar.Hidden = true;
        fake.ExplorerMadeATaskbar();
        Assert.Equal(2, fake.Hides);

        taskbar.Hidden = false;
        fake.ExplorerMadeATaskbar();
        Assert.Equal(2, fake.Hides);
    }

    [Fact]
    public void A_hide_that_fails_puts_back_what_it_took_and_stays_shown()
    {
        var fake = new Fake { FailHide = true };
        var taskbar = Service(fake);

        Assert.Throws<InvalidOperationException>(() => taskbar.Hidden = true);

        Assert.False(taskbar.Hidden);
        Assert.Equal(1, fake.Hides);
        Assert.Equal(1, fake.Restores);
    }

    [Fact]
    public void A_hide_whose_rollback_fails_reports_it_and_exit_tries_the_restore_again()
    {
        var fake = new Fake { FailHide = true, FailRestore = true };
        var taskbar = Service(fake);

        var thrown = Assert.Throws<InvalidOperationException>(() => taskbar.Hidden = true);

        Assert.Equal("The taskbar could not be hidden.", thrown.Message);
        Assert.Equal(
            [
                "explorer-taskbar: Could not put the taskbar back after hiding it failed: The taskbar could not be shown."
            ],
            _warnings);
        Assert.True(taskbar.Hidden);
        fake.FailRestore = false;
        taskbar.Dispose();
        Assert.Equal(2, fake.Restores);
        Assert.False(taskbar.Hidden);
    }
}
