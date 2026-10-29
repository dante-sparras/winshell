using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>Starting a shell is <c>winshell run</c>. A bare <c>winshell</c> prints help, and options may precede the command.</summary>
public class CliRunCommandTests
{
    [Fact]
    public void No_arguments_print_help_and_do_not_start_a_shell()
    {
        var (code, output, error) = Run([]);

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("winshell run", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_before_the_command_do_not_start_a_shell()
    {
        // An unknown command, not kill: kill would stop a real instance on this PC.
        var (code, output, error) = Run(["-v", "nosuch"]);

        Assert.Equal(2, code);
        Assert.Equal("", output);
        Assert.Equal("Unknown command \"nosuch\". Run winshell --help." + Environment.NewLine, error);
    }

    [Fact]
    public void A_pid_option_before_kill_selects_that_process()
    {
        var (code, output, error) = Run(["--pid", "0", "kill"]);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Equal("No instance with process id 0" + Environment.NewLine, error);
    }

    [Fact]
    public void Run_with_a_missing_path_fails_before_a_shell_starts()
    {
        var missing = Path.Combine(Path.GetTempPath(), "winshell-cli-run-missing-" + Guid.NewGuid().ToString("N"));
        var (code, output, error) = Run(["run", "-p", missing]);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Contains("No shell.qml", error, StringComparison.Ordinal);
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
        info.Environment["WINSHELL_CONFIG_DIR"] = Path.Combine(Path.GetTempPath(), "winshell-cli-run-absent");
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
