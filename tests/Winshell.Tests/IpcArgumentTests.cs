namespace Winshell.Tests;

/// <summary>
/// Arguments of <c>ipc call</c> belong to the function. A <c>--</c> ends options on every command.
/// These stay inside <see cref="CliParser"/> so a live instance on this PC is never called.
/// </summary>
public class IpcArgumentTests
{
    [Fact]
    public void Call_arguments_that_look_like_options_stay_arguments()
    {
        var parsed = CliParser.Parse(["ipc", "call", "notes", "add", "-v", "-p", "50", "--after", "5"]);

        Assert.False(parsed.Verbose);
        Assert.Null(parsed.Path);
        Assert.Equal(["call", "notes", "add", "-v", "-p", "50", "--after", "5"], parsed.Positional);
    }

    [Fact]
    public void Options_before_the_function_name_still_count()
    {
        var parsed = CliParser.Parse(["ipc", "-v", "-p", "50", "call", "notes", "add"]);

        Assert.True(parsed.Verbose);
        Assert.Equal("50", parsed.Path);
        Assert.Equal(["call", "notes", "add"], parsed.Positional);
    }

    [Fact]
    public void A_flag_between_the_target_and_the_function_is_still_an_option()
    {
        var parsed = CliParser.Parse(["ipc", "call", "notes", "-v", "add"]);

        Assert.True(parsed.Verbose);
        Assert.Equal(["call", "notes", "add"], parsed.Positional);
    }

    [Fact]
    public void Double_dash_ends_options()
    {
        var parsed = CliParser.Parse(["ipc", "show", "-j", "--", "-j", "--force"]);

        Assert.True(parsed.Json);
        Assert.False(parsed.Force);
        Assert.Equal(["show", "-j", "--force"], parsed.Positional);
    }

    [Theory]
    [InlineData("kill --help", true)]
    [InlineData("ipc --help", true)]
    [InlineData("--help", true)]
    [InlineData("ipc call target func --help", false)]
    [InlineData("ipc call target --help func", true)]
    public void Help_is_an_option_until_call_arguments_begin(string line, bool help)
    {
        Assert.Equal(help, CliParser.Parse(line.Split(' ')).ShowHelp);
    }

    [Fact]
    public void Help_after_double_dash_is_an_argument()
    {
        Assert.False(CliParser.Parse(["--", "--help"]).ShowHelp);
        Assert.False(CliParser.Parse(["--", "--version"]).ShowVersion);
        Assert.True(CliParser.Parse(["--version"]).ShowVersion);
        var passed = CliParser.Parse(["--", "kill"]);
        Assert.Null(passed.Command);
        Assert.Equal(["--", "kill"], passed.Rest);
        var error = Assert.Throws<ArgumentException>(() => CliParser.Parse(["ipc", "--", "call"]));
        Assert.Equal("winshell ipc call needs a target and a function.", error.Message);
        var kill = CliParser.Parse(["-c", "work", "kill"]);
        Assert.Equal("kill", kill.Command);
        Assert.Equal("work", kill.Config);
    }
}
