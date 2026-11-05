using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>winshell ipc</c> with no command says so, and where its usage is.</summary>
[Trait("Category", "Slow")]
public sealed class IpcUsageTests
{
    [Fact]
    public void Ipc_with_no_command_says_so_on_stderr_and_exits_2()
    {
        var result = WinshellCli.Run(["ipc"]);

        result.AssertExitCode(2);
        Assert.Equal("", result.Output);
        Assert.Equal("winshell ipc needs a command. Run winshell ipc --help." + Environment.NewLine, result.Error);
    }
}
