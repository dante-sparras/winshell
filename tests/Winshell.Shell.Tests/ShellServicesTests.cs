namespace Winshell.ShellTests;

public sealed class ShellServicesTests
{
    [Fact]
    public async Task A_singleton_is_one_object_for_the_shell()
    {
        var same = await ShellFixture.On(shell =>
            (ReferenceEquals(shell.Power, shell.Power),
             ReferenceEquals(shell.PowerMode, shell.PowerMode),
             ReferenceEquals(shell.WindowList, shell.WindowList)));

        Assert.Equal((true, true, true), same);
    }

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
        Assert.True(Shell.DarkFor(ColorScheme.Dark, false));
        Assert.False(Shell.DarkFor(ColorScheme.Light, true));
        Assert.True(Shell.DarkFor(ColorScheme.System, true));
        Assert.False(Shell.DarkFor(ColorScheme.System, false));
    }
}
