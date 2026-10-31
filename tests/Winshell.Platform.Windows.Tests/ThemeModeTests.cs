namespace Winshell.Platform.Windows.Tests;

/// <summary>Which Personalize value the shell's light or dark comes from.</summary>
public class ThemeModeTests
{
    [Fact]
    public void Windows_mode_wins_over_app_mode()
    {
        Assert.True(WindowsPlatform.WindowsModeDark(0, 1));
        Assert.False(WindowsPlatform.WindowsModeDark(1, 0));
    }

    [Fact]
    public void A_missing_windows_mode_follows_app_mode_and_a_missing_pair_is_light()
    {
        Assert.True(WindowsPlatform.WindowsModeDark(null, 0));
        Assert.False(WindowsPlatform.WindowsModeDark(null, 1));
        Assert.False(WindowsPlatform.WindowsModeDark(null, null));
        Assert.False(WindowsPlatform.WindowsModeDark("dark", 1));
    }
}
