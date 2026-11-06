namespace Winshell.Tests;

/// <summary>
/// <c>-d</c> before <c>--</c> is the daemonize flag. As the value of <c>-c</c> or <c>-p</c> it names the config or the
/// path, and the background child keeps that value.
/// </summary>
public class DaemonChildTests
{
    [Theory]
    [InlineData("-c")]
    [InlineData("--config")]
    [InlineData("-p")]
    [InlineData("--path")]
    public void A_daemon_child_keeps_a_value_spelled_like_the_flag(string option)
    {
        var parsed = CliParser.Parse(["run", "-d", option, "-d"]);

        Assert.True(parsed.Daemonize);
        Assert.Equal("-d", option is "-p" or "--path" ? parsed.Path : parsed.Config);
        Assert.Equal(["--no-build", "run", option, "-d"], parsed.ChildArguments);
    }

    [Fact]
    public void A_daemon_child_keeps_the_programs_own_flag_after_double_dash()
    {
        var parsed = CliParser.Parse(["run", "-d", "--", "-d"]);

        Assert.Equal(["--no-build", "run", "--", "-d"], parsed.ChildArguments);
        Assert.Equal(["--", "-d"], parsed.Rest);
    }

    [Fact]
    public void A_child_that_was_told_not_to_build_is_not_told_again()
    {
        var parsed = CliParser.Parse(["run", "--daemonize", "--no-build", "-c", "-d"]);

        Assert.Equal(["run", "--no-build", "-c", "-d"], parsed.ChildArguments);
    }

    [Fact]
    public void A_daemon_start_without_a_command_word_still_names_run()
    {
        var parsed = CliParser.Parse(["-d", "-p", "-d"]);

        Assert.Null(parsed.Command);
        Assert.True(parsed.ShellStartAllowed);
        Assert.Equal("-d", parsed.Path);
        Assert.Equal(["--no-build", "run", "-p", "-d"], parsed.ChildArguments);
    }
}
