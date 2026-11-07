using System.Diagnostics;
using System.Globalization;

namespace Winshell.Tests;

/// <summary><c>winshell kill</c> waits until the instance's process has exited.</summary>
[Trait("Category", "Slow")]
public sealed class KillWaitTests : IDisposable, IClassFixture<ProbeBuild>
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

        result.AssertExitCode(0);
        Assert.Equal("", result.Error);
        Assert.StartsWith("Killed ", result.Output, StringComparison.Ordinal);
        Assert.True(process.HasExited);
    }

    [Fact]
    public void Force_ends_the_shell_and_leaves_the_programs_it_started()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, ProbeShell.HangMarker), "");
        var process = ProbeShell.Start(config, Path.Combine(_dir, "local"), "hung");
        _started.Add(process);
        var pidFile = Path.Combine(config, "child.pid");
        Wait.Until(() => File.Exists(pidFile), "the shell's program to start");
        using var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture));
        try
        {
            var result = Run("kill", "--force", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));

            result.AssertExitCode(0);
            Assert.True(process.HasExited);
            Assert.False(child.HasExited);
        }
        finally
        {
            child.Kill();
            child.WaitForExit(5000);
        }
    }

    private Process Start(string configDir)
    {
        var process = ProbeShell.Start(configDir, Path.Combine(_dir, "local"));
        _started.Add(process);
        return process;
    }

    private ChildProcess Run(params string[] args)
    {
        return WinshellCli.Run(args, ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")),
            ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")));
    }

    public void Dispose()
    {
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
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
