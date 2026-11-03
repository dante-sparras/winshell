namespace Winshell.ShellTests;

/// <summary>An update restarts the shell with --after, unless another Winshell would keep its files.</summary>
public sealed class UpdateRestartDecisionTests
{
    [Fact]
    public void An_update_restarts_with_after_unless_another_winshell_is_running()
    {
        var arguments = ShellArguments.Parse(["-d", "--no-duplicate", "--theme", "dark"]);

        Assert.Equal(UpdateRestart.AnotherRunning, UpdateRestart.Decide(arguments, 4, anotherRunning: true, out var refused));
        Assert.Equal(["--after", "4", "--no-duplicate", "--theme", "dark"], refused);
        Assert.False(ShellArguments.Parse(refused).Daemonize);

        Assert.Null(UpdateRestart.Decide(arguments, 4, anotherRunning: false, out var next));
        Assert.Equal(refused, next);
    }
}
