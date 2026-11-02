using System.Diagnostics;
using System.Globalization;

namespace Winshell.Tests;

/// <summary><c>winshell kill</c> waits until the instance's process has exited.</summary>
public sealed class KillWaitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void Kill_returns_after_the_process_has_exited()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var process = Start(config);

        var result = Run("kill", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(0, result.Code);
        Assert.Equal("", result.Error);
        Assert.StartsWith("Killed ", result.Output, StringComparison.Ordinal);
        Assert.True(process.HasExited);
    }

    [Fact]
    public void Force_on_a_missing_pid_is_still_a_miss()
    {
        var result = Run("kill", "--force", "--pid", "0");

        Assert.Equal(1, result.Code);
        Assert.Equal("", result.Output);
        Assert.Equal("No instance with process id 0" + Environment.NewLine, result.Error);
    }

    private Process Start(string configDir)
    {
        var process = ProbeShell.Start(configDir, Path.Combine(_dir, "local"));
        _started.Add(process);
        return process;
    }

    private (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args, ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")), ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")));

    public void Dispose()
    {
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }
                process.WaitForExit(5000);
            }
            process.Dispose();
        }
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
