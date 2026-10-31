using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>winshell ipc</c> with no command shows how to use it.</summary>
public sealed class IpcUsageTests
{
    [Fact]
    public void Ipc_with_no_command_prints_its_usage_and_exits_2()
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winshell.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("ipc");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000), "winshell did not exit");

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("call <target> <function>", output.Result + error.Result);
    }
}
