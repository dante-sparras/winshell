using Microsoft.Win32;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>One settings broadcast reaches every window. The shell follows that change once.</summary>
[Collection(ThemeCollection.Name)]
public class SettingsBroadcastTests
{
    private const int Windows = 4;

    [Fact]
    public void One_settings_change_reads_the_theme_once_for_every_window()
    {
        using var loop = new WinEventLoop();
        using var platform = new WindowsPlatform(loop, new Log());

        for (var i = 0; i < Windows; i++)
        {
            platform.SystemChanged(Win32.WM_SETTINGCHANGE, 0);
            platform.SystemChanged(Win32.WM_THEMECHANGED, 0);
            platform.SystemChanged(Win32.WM_SYSCOLORCHANGE, 0);
            platform.SystemChanged(Win32.WM_DWMCOLORIZATIONCOLORCHANGED, 0);
        }
        platform.NoteUserPreference(UserPreferenceCategory.General);
        Assert.True(loop.RunDue());

        Assert.Equal(1, platform.ThemeReads);
        Assert.Equal(1, platform.TextRefreshes);
        Assert.Equal(1, platform.ScreenNotes);
        Assert.Equal(0, platform.WorkAreaNotes);

        platform.SystemChanged(Win32.WM_SETTINGCHANGE, 0);
        Assert.True(loop.RunDue());
        Assert.Equal(2, platform.ThemeReads);
        Assert.Equal(2, platform.TextRefreshes);
        Assert.Equal(2, platform.ScreenNotes);
    }

    [Fact]
    public void A_work_area_change_and_a_display_change_each_run_once()
    {
        using var loop = new WinEventLoop();
        using var platform = new WindowsPlatform(loop, new Log());

        for (var i = 0; i < Windows; i++)
            platform.SystemChanged(Win32.WM_DISPLAYCHANGE, 0);
        Assert.True(loop.RunDue());
        Assert.Equal(1, platform.ScreenNotes);
        Assert.Equal(0, platform.WorkAreaNotes);
        Assert.Equal(0, platform.TextRefreshes);
        Assert.Equal(0, platform.ThemeReads);

        for (var i = 0; i < Windows; i++)
            platform.SystemChanged(Win32.WM_SETTINGCHANGE, Win32.SPI_SETWORKAREA);
        Assert.True(loop.RunDue());
        Assert.Equal(1, platform.ScreenNotes);
        Assert.Equal(1, platform.WorkAreaNotes);
        Assert.Equal(1, platform.TextRefreshes);
        Assert.Equal(1, platform.ThemeReads);
    }

    [Fact]
    public void An_unrelated_preference_does_not_read_the_theme()
    {
        using var loop = new WinEventLoop();
        using var platform = new WindowsPlatform(loop, new Log());

        platform.NoteUserPreference(UserPreferenceCategory.Locale);
        Assert.False(loop.RunDue());
        Assert.Equal(0, platform.ThemeReads);
        Assert.Equal(0, platform.TextRefreshes);
        Assert.Equal(0, platform.ScreenNotes);
        Assert.Equal(0, platform.WorkAreaNotes);
    }
}
