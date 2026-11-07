using Winshell.Native;

namespace Winshell.ShellTests;

/// <summary>One change in Windows can send several broadcasts. The shell follows that change once.</summary>
public class SystemChangesTests
{
    [Fact]
    public void The_broadcasts_of_one_change_are_followed_once()
    {
        var changes = new SystemChanges();

        Assert.True(changes.Add(SystemChanges.From(Win32.WM_SETTINGCHANGE, 0)));
        Assert.False(changes.Add(SystemChanges.From(Win32.WM_SETTINGCHANGE, 0)));
        Assert.False(changes.Add(SystemChanges.From(Win32.WM_THEMECHANGED, 0)));
        Assert.False(changes.Add(SystemChanges.From(Win32.WM_SYSCOLORCHANGE, 0)));

        Assert.Equal(SystemChange.Screens | SystemChange.Theme, changes.Take());
    }

    [Fact]
    public void A_later_change_is_followed_again()
    {
        var changes = new SystemChanges();
        changes.Add(SystemChange.Theme);
        changes.Take();

        Assert.True(changes.Add(SystemChange.Theme));
        Assert.Equal(SystemChange.Theme, changes.Take());
        Assert.Equal(SystemChange.None, changes.Take());
    }

    [Fact]
    public void Each_broadcast_asks_for_what_it_changes()
    {
        Assert.Equal(SystemChange.Screens, SystemChanges.From(Win32.WM_DISPLAYCHANGE, 0));
        Assert.Equal(SystemChange.Screens | SystemChange.Theme, SystemChanges.From(Win32.WM_SETTINGCHANGE, 0));
        Assert.Equal(SystemChange.WorkArea | SystemChange.Theme,
            SystemChanges.From(Win32.WM_SETTINGCHANGE, Win32.SPI_SETWORKAREA));
        Assert.Equal(SystemChange.Theme, SystemChanges.From(Win32.WM_THEMECHANGED, 0));
        Assert.Equal(SystemChange.Theme, SystemChanges.From(Win32.WM_DWMCOLORIZATIONCOLORCHANGED, 0));
    }

    [Fact]
    public void An_unrelated_message_posts_nothing()
    {
        var changes = new SystemChanges();

        Assert.False(changes.Add(SystemChanges.From(Win32.WM_SIZE, 0)));
        Assert.True(changes.Add(SystemChanges.From(Win32.WM_SYSCOLORCHANGE, 0)));
    }
}
