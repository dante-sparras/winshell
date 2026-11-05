using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>--help</c> after a command prints that command's usage and does not run it.</summary>
[Trait("Category", "Slow")]
public class CliHelpTests
{
    [Theory]
    [InlineData("list", "--help", "list")]
    [InlineData("kill", "--help", "kill")]
    [InlineData("kill", "-h", "kill")]
    [InlineData("kill", "/?", "kill")]
    [InlineData("log", "--help", "log")]
    [InlineData("ipc", "--help", "ipc")]
    [InlineData("doctor", "--help", "doctor")]
    [InlineData("help", "--help", "Usage")]
    [InlineData("--help", null, "Usage")]
    [InlineData("/?", null, "Usage")]
    public void Help_exits_0_and_does_not_run_the_command(string command, string? help, string expected)
    {
        var args = help is null ? new[] { command } : new[] { command, help };
        var result = Run(args);

        result.AssertExitCode(0);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("No running instances", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("No running instances", result.Output, StringComparison.Ordinal);
        Assert.Equal("", result.Error);
    }

    [Fact]
    public void Help_names_the_exit_codes()
    {
        var result = Run(["--help"]);
        result.AssertExitCode(0);
        Assert.Contains("2 the command line was wrong", result.Output, StringComparison.Ordinal);
    }

    private static ChildProcess Run(string[] args) =>
        WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-cli-help-absent")));
}
