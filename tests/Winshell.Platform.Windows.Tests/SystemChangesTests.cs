using Microsoft.Win32;
using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows.Tests;

/// <summary>One settings broadcast reaches every window. The shell follows that change once.</summary>
public class SystemChangesTests
{
    [Fact]
    public void The_copies_of_a_broadcast_are_followed_once()
    {
        var changes = new SystemChanges();

        Assert.True(changes.Add(SystemChanges.From(Win32.WM_SETTINGCHANGE, 0)));
        Assert.False(changes.Add(SystemChanges.From(Win32.WM_SETTINGCHANGE, 0)));
        Assert.False(changes.Add(SystemChanges.From(Win32.WM_THEMECHANGED, 0)));
        Assert.False(changes.Add(SystemChanges.From(UserPreferenceCategory.General)));

        Assert.Equal(SystemChange.Text | SystemChange.Screens | SystemChange.Theme, changes.Take());
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
    public void Each_message_asks_for_what_it_did_before()
    {
        Assert.Equal(SystemChange.Screens, SystemChanges.From(Win32.WM_DISPLAYCHANGE, 0));
        Assert.Equal(SystemChange.Text | SystemChange.Screens | SystemChange.Theme, SystemChanges.From(Win32.WM_SETTINGCHANGE, 0));
        Assert.Equal(SystemChange.Text | SystemChange.WorkArea | SystemChange.Theme, SystemChanges.From(Win32.WM_SETTINGCHANGE, Win32.SPI_SETWORKAREA));
        Assert.Equal(SystemChange.Theme, SystemChanges.From(Win32.WM_THEMECHANGED, 0));
    }

    [Fact]
    public void An_unrelated_preference_posts_nothing()
    {
        var changes = new SystemChanges();

        Assert.False(changes.Add(SystemChanges.From(UserPreferenceCategory.Locale)));
        Assert.True(changes.Add(SystemChanges.From(UserPreferenceCategory.Color)));
    }
}
