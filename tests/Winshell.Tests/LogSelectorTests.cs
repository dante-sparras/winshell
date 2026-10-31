using System.Diagnostics;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary><c>winshell log</c> never prints a different instance's log than the one asked for.</summary>
public sealed class LogSelectorTests : IDisposable
{
    private const string StoppedId = "abc123stopped";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public LogSelectorTests()
    {
        var dir = Path.Combine(_root, "run", StoppedId);
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "log.txt");
        File.WriteAllText(log, "the stopped instance's log\n");
        File.WriteAllText(Path.Combine(dir, "instance.json"), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = StoppedId,
            ["pid"] = int.MaxValue - 1,
            ["shellId"] = "stopped",
            ["configPath"] = Path.Combine(_root, "shell.qml"),
            ["launchTime"] = DateTime.Now.AddHours(-1),
            ["pipe"] = "winshell-" + StoppedId,
            ["log"] = log,
            ["processStart"] = 1,
            ["exitedAt"] = DateTime.Now.AddMinutes(-30),
        }));
    }

    [Theory]
    [InlineData("--pid", "1234", "No instance with process id 1234")]
    [InlineData("-i", "deadbeef", "No instance with id deadbeef")]
    public void A_selector_that_matches_nothing_is_an_error(string option, string value, string message)
    {
        var (code, output, error) = Run(["log", option, value]);

        Assert.Equal(1, code);
        Assert.Contains(message, error);
        Assert.DoesNotContain("the stopped instance's log", output);
    }

    [Fact]
    public void A_selector_can_name_a_stopped_instance()
    {
        var (code, output, _) = Run(["log", "-i", StoppedId[..6]]);

        Assert.Equal(0, code);
        Assert.Contains("the stopped instance's log", output);
    }

    [Fact]
    public void With_no_selector_a_stopped_instance_is_shown_and_named()
    {
        var (code, output, error) = Run(["log"]);

        Assert.Equal(0, code);
        Assert.Contains("the stopped instance's log", output);
        Assert.Contains($"Showing the log of stopped instance {StoppedId}", error);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (int Code, string Output, string Error) Run(string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winshell.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        info.Environment["WINSHELL_LOCAL_DIR"] = _root;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill();
            throw new TimeoutException("winshell did not exit");
        }
        return (process.ExitCode, output.Result, error.Result);
    }
}
