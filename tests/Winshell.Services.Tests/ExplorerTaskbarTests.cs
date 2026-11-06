using System.ComponentModel;
using Winshell.ExplorerTaskbar;

namespace Winshell.Services.Tests;

/// <summary>Hiding, re-hiding and recovering Explorer's taskbar go through the module, against a stand-in.</summary>
public class ExplorerTaskbarTests
{
    private sealed class Fake : IExplorerTaskbarBackend
    {
        public int Hides { get; private set; }

        public int Restores { get; private set; }

        public int HiddenQueries { get; private set; }

        public int HiddenRestores { get; private set; }

        public bool AnyHiddenResult { get; set; }

        public bool FailHide { get; set; }

        public void Hide()
        {
            Hides++;
            if (FailHide)
                throw new InvalidOperationException("The taskbar could not be hidden.");
        }

        public void Restore()
        {
            Restores++;
        }

        public bool AnyHidden()
        {
            HiddenQueries++;
            return AnyHiddenResult;
        }

        public void RestoreHidden()
        {
            HiddenRestores++;
        }
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
        var taskbar = new ExplorerTaskbarService(fake);
        var changes = Watch(taskbar);

        taskbar.Hidden = false;
        taskbar.Rehide();
        taskbar.Restore();
        taskbar.Dispose();
        taskbar.Dispose();

        Assert.False(taskbar.Hidden);
        Assert.Equal(0, fake.Hides);
        Assert.Equal(0, fake.Restores);
        Assert.Equal(0, fake.HiddenQueries);
        Assert.Equal(0, fake.HiddenRestores);
        Assert.Empty(changes);
    }

    [Fact]
    public void Hiding_and_restoring_each_happen_once_and_exit_puts_it_back()
    {
        var fake = new Fake();
        var taskbar = new ExplorerTaskbarService(fake);
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
    public void A_new_taskbar_window_is_taken_while_hidden_and_never_otherwise()
    {
        var fake = new Fake();
        var taskbar = new ExplorerTaskbarService(fake);

        taskbar.Rehide();
        Assert.Equal(0, fake.Hides);

        taskbar.Hidden = true;
        taskbar.Rehide();
        Assert.Equal(2, fake.Hides);

        taskbar.Hidden = false;
        taskbar.Rehide();
        Assert.Equal(2, fake.Hides);
    }

    [Fact]
    public void A_hide_that_fails_puts_back_what_it_took_and_stays_shown()
    {
        var fake = new Fake { FailHide = true };
        var taskbar = new ExplorerTaskbarService(fake);

        Assert.Throws<InvalidOperationException>(() => taskbar.Hidden = true);

        Assert.False(taskbar.Hidden);
        Assert.Equal(1, fake.Hides);
        Assert.Equal(1, fake.Restores);
    }

    [Fact]
    public void Recovery_asks_whether_any_taskbar_is_hidden_and_puts_those_back()
    {
        var fake = new Fake { AnyHiddenResult = true };
        var taskbar = new ExplorerTaskbarService(fake);

        Assert.True(taskbar.AnyHidden());
        fake.AnyHiddenResult = false;
        Assert.False(taskbar.AnyHidden());
        taskbar.RestoreHidden();

        Assert.False(taskbar.Hidden);
        Assert.Equal(2, fake.HiddenQueries);
        Assert.Equal(1, fake.HiddenRestores);
        Assert.Equal(0, fake.Hides);
        Assert.Equal(0, fake.Restores);
    }
}
