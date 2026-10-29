using System.Diagnostics;

namespace Winshell.Tests;

/// <summary><c>--pid</c> and <c>--tail</c> that are not whole numbers exit 2, before any instance is touched.</summary>
public class CliUsageTests
{
    [Theory]
    [InlineData("kill", "--pid", "abc", "--pid needs a whole number")]
    [InlineData("kill", "--pid", "1.5", "--pid needs a whole number")]
    [InlineData("kill", "--pid", "999999999999", "--pid needs a whole number")]
    [InlineData("kill", "--pid", "-1", "--pid needs a whole number")]
    [InlineData("kill", "--pid", "", "--pid needs a whole number")]
    [InlineData("log", "--tail", "ten", "--tail needs a whole number")]
    [InlineData("log", "--tail", "-5", "--tail needs a whole number")]
    [InlineData("log", "-t", "ten", "-t needs a whole number")]
    [InlineData("kill", "--pid", null, "--pid needs a value")]
    [InlineData("log", "--tail", null, "--tail needs a value")]
    public void A_bad_number_exits_2_with_a_message(string command, string option, string? value, string message)
    {
        var args = value is null ? new[] { command, option } : new[] { command, option, value };
        var (code, output, error) = Run(args);

        Assert.Equal(2, code);
        Assert.Equal("", output);
        Assert.Equal(message + Environment.NewLine, error);
    }

    [Fact]
    public void A_whole_number_pid_that_is_not_an_instance_exits_1()
    {
        var (code, output, error) = Run(["kill", "--pid", "0"]);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Equal("No instance with process id 0" + Environment.NewLine, error);
    }

    [Fact]
    public void Tail_of_int_max_prints_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-cli-usage-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(path, "one\r\ntwo\r\n");
        try
        {
            var (code, output, error) = Run(["log", path, "--tail", "2147483647"]);

            Assert.Equal(0, code);
            Assert.Equal("", error);
            Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine, output);
        }
        finally
        {
            File.Delete(path);
        }
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
        info.Environment["WINSHELL_CONFIG_DIR"] = Path.Combine(Path.GetTempPath(), "winshell-cli-usage-absent");
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
