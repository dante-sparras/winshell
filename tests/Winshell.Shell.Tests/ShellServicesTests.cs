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
    public async Task A_hotkey_with_no_sequence_takes_no_keys()
    {
        var registered = await ShellFixture.On(shell =>
        {
            using var hotkey = shell.NewHotkey();
            return hotkey.Active;
        });

        Assert.False(registered);
    }
}
