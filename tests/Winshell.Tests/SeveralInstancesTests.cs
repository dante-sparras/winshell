namespace Winshell.Tests;

/// <summary>
/// With several instances and no selector, kill, log and ipc must not pick one. The choice is pure so this
/// test does not start a shell.
/// </summary>
public class SeveralInstancesTests
{
    [Fact]
    public void One_instance_is_used_when_no_selector_was_given()
    {
        var only = Instance("only", @"C:\only\shell.qml");
        var choice = Cli.ChooseUnselected([only]);

        Assert.Equal(0, choice.Code);
        Assert.Same(only, choice.Info);
    }

    [Fact]
    public void No_instance_is_a_miss()
    {
        var choice = Cli.ChooseUnselected([]);

        Assert.Equal(1, choice.Code);
        Assert.Equal("No running instances", choice.Error);
        Assert.Null(choice.Info);
    }

    [Fact]
    public void Several_instances_are_a_command_line_error_that_names_each()
    {
        var choice = Cli.ChooseUnselected([
            Instance("aaa", @"C:\a\shell.qml"),
            Instance("bbb", @"C:\b\shell.qml"),
        ]);

        Assert.Equal(2, choice.Code);
        Assert.Null(choice.Info);
        Assert.Equal(
            @"Several instances are running; pick one with -i: aaa (C:\a\shell.qml), bbb (C:\b\shell.qml)",
            choice.Error);
    }

    private static InstanceInfo Instance(string id, string config) =>
        new(id, 1, "shell", config, DateTime.UnixEpoch, "pipe-" + id, "log-" + id);
}
