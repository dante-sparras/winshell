namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class ShellServicesTests
{
    [Fact]
    public async Task Winland_resolves_windows_from_the_shells_window_list()
    {
        var same = await ShellFixture.On(shell => ReferenceEquals(shell.WindowList, shell.Winland.WindowList));

        Assert.True(same);
    }

    [Fact]
    public async Task A_hotkey_with_no_sequence_takes_no_keys()
    {
        var registered = await ShellFixture.On(shell =>
        {
            using var hotkey = shell.NewHotkey();
            return hotkey.Active;
        });

        Assert.False(registered);
    }

    [Fact]
    public void The_color_scheme_overrides_Windows_mode()
    {
        Assert.True(ShellPlatform.DarkFor(ColorScheme.Dark, false));
        Assert.False(ShellPlatform.DarkFor(ColorScheme.Light, true));
        Assert.True(ShellPlatform.DarkFor(ColorScheme.System, true));
        Assert.False(ShellPlatform.DarkFor(ColorScheme.System, false));
    }
}
