using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>winshell ipc</c> with no command shows how to use it.</summary>
public sealed class IpcUsageTests
{
    [Fact]
    public void Ipc_with_no_command_prints_its_usage_and_exits_2()
    {
        var (code, output, error) = WinshellCli.Run(["ipc"]);

        Assert.Equal(2, code);
        Assert.Contains("call <target> <function>", output + error);
    }
}
