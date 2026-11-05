using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>An option Winshell does not have, and a spare word, exit 2 before a shell starts.</summary>
[Trait("Category", "Slow")]
public class UnknownOptionTests
{
    // Each run names a path that is not there, so a bug in the check fails with 1 and cannot start a shell.
    [Theory]
    [InlineData("run -p {missing} --pth", "Unknown option \"--pth\".")]
    [InlineData("run -p {missing} --log-times", "Unknown option \"--log-times\".")]
    [InlineData("run -p {missing} --any-display", "Unknown option \"--any-display\".")]
    [InlineData("run -p {missing} --newest", "Unknown option \"--newest\".")]
    [InlineData("run -p {missing} --log-rules x", "Unknown option \"--log-rules\".")]
    [InlineData("run -p {missing} -m MyShell.exe", "Unknown option \"-m\".")]
    [InlineData("run -p {missing} --manifest MyShell.exe", "Unknown option \"--manifest\".")]
    [InlineData("run -p {missing} --debug 1", "Unknown option \"--debug\".")]
    [InlineData("list -a", "Unknown option \"-a\".")]
    [InlineData("list --all", "Unknown option \"--all\".")]
    [InlineData("reload --pid 0 --hard", "Unknown option \"--hard\".")]
    [InlineData("list --force", "winshell list does not take \"--force\".")]
    [InlineData("list extra", "Unexpected argument \"extra\".")]
    [InlineData("kill --pid 0 extra", "Unexpected argument \"extra\".")]
    [InlineData("run -p {missing} extra", "Unexpected argument \"extra\".")]
    [InlineData("doctor extra", "Unexpected argument \"extra\".")]
    [InlineData("log one two", "Unexpected argument \"two\".")]
    public void A_bad_command_line_exits_2(string command, string error)
    {
        var missing = Path.Combine(Path.GetTempPath(), "winshell-unknown-option-missing");
        var args = command.Replace("{missing}", missing, StringComparison.Ordinal).Split(' ');
        var result = Run(args);

        result.AssertExitCode(2);
        Assert.Equal("", result.Output);
        Assert.Equal(error + Environment.NewLine, result.Error);
    }

    private static ChildProcess Run(string[] args) =>
        WinshellCli.Run(
            args,
            ("WINSHELL_CONFIG_DIR", Path.Combine(Path.GetTempPath(), "winshell-unknown-option-absent")));
}
