using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>--help</c> after a command prints that command's usage and does not run it.</summary>
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
        var (code, output, error) = Run(args);

        Assert.Equal(0, code);
        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.DoesNotContain("No running instances", error, StringComparison.Ordinal);
        Assert.DoesNotContain("No running instances", output, StringComparison.Ordinal);
        Assert.Equal("", error);
    }

    [Fact]
    public void Help_names_the_exit_codes()
    {
        var (code, output, _) = Run(["--help"]);
        Assert.Equal(0, code);
        Assert.Contains("2 the command line was wrong", output, StringComparison.Ordinal);
    }

    private static (int Code, string Output, string Error) Run(string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winshell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["WINSHELL_CONFIG_DIR"] = Path.Combine(Path.GetTempPath(), "winshell-cli-help-absent");
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("winshell did not exit");
        }
        return (process.ExitCode, output.Result, error.Result);
    }
}
