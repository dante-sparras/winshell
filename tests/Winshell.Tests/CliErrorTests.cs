using System.Diagnostics;

namespace Winshell.Tests;

/// <summary>
/// A command other than <c>run</c> prints one sentence when the environment fails, and exits 1.
/// The shell's own crash reporting is left alone.
/// </summary>
public class CliErrorTests
{
    [Fact]
    public void An_empty_path_on_kill_is_one_sentence()
    {
        var (code, output, error) = Run("kill", "-p", "");

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.StartsWith("winshell kill:", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbose_includes_the_stack()
    {
        var (code, _, error) = Run("-v", "kill", "-p", "");

        Assert.Equal(1, code);
        Assert.Contains("winshell kill:", error, StringComparison.Ordinal);
        Assert.Contains("   at ", error, StringComparison.Ordinal);
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winshell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["WINSHELL_CONFIG_DIR"] = Path.Combine(Path.GetTempPath(), "winshell-cli-error-absent");
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
