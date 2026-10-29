namespace Winshell.Tests;

/// <summary>
/// Arguments of <c>ipc call</c> and <c>msg</c> belong to the function. A <c>--</c> ends options on every command.
/// These stay inside <see cref="Cli"/> so a live instance on this PC is never called.
/// </summary>
public class IpcArgumentTests
{
    [Fact]
    public void Call_arguments_that_look_like_options_stay_arguments()
    {
        var parsed = Cli.Parse(["call", "notes", "add", "-v", "-p", "50", "--after", "5"], "ipc");

        Assert.False(parsed.Verbose);
        Assert.Null(parsed.Path);
        Assert.Equal(["call", "notes", "add", "-v", "-p", "50", "--after", "5"], parsed.Positional);
    }

    [Fact]
    public void Options_before_the_function_name_still_count()
    {
        var parsed = Cli.Parse(["-v", "-p", "50", "call", "notes", "add"], "ipc");

        Assert.True(parsed.Verbose);
        Assert.Equal("50", parsed.Path);
        Assert.Equal(["call", "notes", "add"], parsed.Positional);
    }

    [Fact]
    public void A_flag_between_the_target_and_the_function_is_still_an_option()
    {
        var parsed = Cli.Parse(["call", "notes", "-v", "add"], "ipc");

        Assert.True(parsed.Verbose);
        Assert.Equal(["call", "notes", "add"], parsed.Positional);
    }

    [Fact]
    public void Msg_arguments_that_look_like_options_stay_arguments()
    {
        var parsed = Cli.Parse(["notes", "add", "-c", "nope", "-5"], "msg");

        Assert.Null(parsed.Config);
        Assert.Equal(["notes", "add", "-c", "nope", "-5"], parsed.Positional);
    }

    [Fact]
    public void Double_dash_ends_options()
    {
        var parsed = Cli.Parse(["-j", "--", "-j", "--force"], "list");

        Assert.True(parsed.Json);
        Assert.False(parsed.Force);
        Assert.Equal(["-j", "--force"], parsed.Positional);
    }

    [Theory]
    [InlineData("kill --help", true)]
    [InlineData("ipc --help", true)]
    [InlineData("--help", true)]
    [InlineData("ipc call target func --help", false)]
    [InlineData("msg target func --help", false)]
    [InlineData("ipc call target --help func", true)]
    public void Help_is_an_option_until_call_arguments_begin(string line, bool help)
    {
        Assert.Equal(help, Cli.HelpRequested(line.Split(' ')));
    }

    [Fact]
    public void Help_after_double_dash_is_an_argument()
    {
        Assert.False(Cli.HelpRequested(["--", "--help"]));
        Assert.False(Cli.VersionRequested(["--", "--version"]));
        Assert.True(Cli.VersionRequested(["--version"]));
        Assert.Null(Cli.FirstCommand(["--", "kill"]));
        Assert.Equal("ipc", Cli.FirstCommand(["ipc", "--", "call"]));
        Assert.Equal("kill", Cli.FirstCommand(["-c", "work", "kill"]));
    }

    [Fact]
    public void After_stays_an_argument_of_a_call_and_still_leaves_a_restart()
    {
        var call = new[] { "ipc", "call", "notes", "add", "--after", "5" };
        var kept = Cli.WithoutAfter(call, out var callAfter);
        Assert.Null(callAfter);
        Assert.Equal(call, kept);

        var restart = Cli.WithoutAfter(["-c", "bar", "--after", "4242", "-n"], out var restartAfter);
        Assert.Equal(4242, restartAfter);
        Assert.Equal(["-c", "bar", "-n"], restart);

        var dashed = new[] { "log", "--", "--after", "5" };
        var dashedKept = Cli.WithoutAfter(dashed, out var dashedAfter);
        Assert.Null(dashedAfter);
        Assert.Equal(dashed, dashedKept);
    }
}
