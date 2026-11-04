using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>winshell ipc</c> with no command says so, and where its usage is.</summary>
[Trait("Category", "Slow")]
public sealed class IpcUsageTests
{
    [Fact]
    public void Ipc_with_no_command_says_so_on_stderr_and_exits_2()
    {
        var (code, output, error) = WinshellCli.Run(["ipc"]);

        Assert.Equal(2, code);
        Assert.Equal("", output);
        Assert.Equal("winshell ipc needs a command. Run winshell ipc --help." + Environment.NewLine, error);
    }
}
