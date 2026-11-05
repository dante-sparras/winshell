using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit.Sdk;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell reload</c> builds a configuration project and then asks the instance to start the next process, or under
/// <c>winshell run</c> asks it to quit for its supervisor to start. A build that fails leaves that instance running and
/// tells it to show the notice. These tests edit and rebuild the probe, so they build one of their own that no other test
/// starts. A probe can hide Explorer's real taskbar, which is put back however a test ends.
/// </summary>
[Trait("Category", "Slow")]
public sealed class ReloadCommandTests : IDisposable, IClassFixture<ReloadCommandTests.OwnProbe>
{
    // Each test runs a build, which can take minutes on a busy runner.
    private const int BuildTimeout = 600_000;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _config;
    private readonly OwnProbe _probe;
    private readonly List<Process> _started = [];
    private readonly bool _taskbarHidden = ExplorerTaskbarRecovery.AnyHidden();

    /// <summary>
    /// The probe these tests build and edit, once for the class. The shared one is built first, so that no other build
    /// of the src/ projects is still to come when <c>winshell reload</c> runs one.
    /// </summary>
    public sealed class OwnProbe : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

        public OwnProbe()
        {
            ProbeShell.Ensure();
            Program = ProbeShell.Build(Folder);
        }

        public string Folder => Path.Combine(_dir, "probe");

        public string Program { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public ReloadCommandTests(OwnProbe probe)
    {
        _probe = probe;
        _local = Path.Combine(_dir, "local");
        _config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(_local);
        Directory.CreateDirectory(_config);
    }

    [Fact(Timeout = BuildTimeout)]
    public void A_build_that_fails_leaves_the_instance_running()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var source = Path.Combine(_probe.Folder, "Program.cs");
        var original = File.ReadAllText(source);
        var process = StartProbe();
        try
        {
            File.WriteAllText(source, original + "\nthis is not C#\n");
            var result = Run("reload", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));

            result.AssertExitCode(1);
            Assert.Equal("", result.Output);
            Assert.Contains("error ", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("did not reload", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("could not show the notice", result.Error, StringComparison.Ordinal);
            Assert.False(process.HasExited);
            Assert.Contains(Listed(), row => row.GetProperty("pid").GetInt32() == process.Id);
        }
        finally
        {
            File.WriteAllText(source, original);
        }
    }

    [Fact(Timeout = BuildTimeout)]
    public void A_rebuild_starts_the_next_process()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var source = Path.Combine(_probe.Folder, "Program.cs");
        var original = File.ReadAllText(source);
        var process = StartProbe();
        try
        {
            File.WriteAllText(source, original + "\n// rebuilt\n");
            var result = Reload(process.Id);

            Assert.Equal("", result.Error);
            Assert.StartsWith("Reloaded ", result.Output, StringComparison.Ordinal);
            Wait.Until(() => process.HasExited, "the old process to exit");
            Wait.Until(
                () => Listed().Any(row =>
                    row.GetProperty("shellId").GetString() == "probe" &&
                    row.GetProperty("pid").GetInt32() != process.Id),
                "the process that took over");
        }
        finally
        {
            File.WriteAllText(source, original);
        }
    }

    [Fact(Timeout = BuildTimeout)]
    public void A_second_reload_runs_the_second_edit()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var source = Path.Combine(_probe.Folder, "Program.cs");
        var original = File.ReadAllText(source);
        var process = StartProbe();
        try
        {
            // The probe's shell id says which code runs. The first reload builds beside the running program, the second into bin\shell.
            var first = ReloadTo(process, original, "first");
            var second = ReloadTo(first, original, "second");

            Assert.NotEqual(first.Id, second.Id);
        }
        finally
        {
            File.WriteAllText(source, original);
        }
    }

    /// <summary>
    /// The supervisor starts the edit a reload built, beside the program that ran, and owns it: it starts it again after
    /// a crash, with the taskbar that shell hid back.
    /// </summary>
    [Fact(Timeout = BuildTimeout)]
    public void A_reload_under_winshell_run_stays_supervised()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        Assert.False(_taskbarHidden, "Explorer's taskbar was hidden before the test.");
        var source = Path.Combine(_probe.Folder, "Program.cs");
        var original = File.ReadAllText(source);
        var run = Supervise();
        try
        {
            var first = NextPid(0, null);
            // Once the shell's thread answers, its configuration has started and will not see the marker.
            Run("ipc", "--pid", first.ToString(CultureInfo.InvariantCulture), "show").AssertExitCode(0);
            var marker = Path.Combine(_config, ProbeShell.HideTaskbarMarker);
            File.WriteAllText(marker, "");
            File.WriteAllText(source, original.Replace("?? \"probe\"", "?? \"reloaded\"", StringComparison.Ordinal));

            Reload(first);

            var second = NextPid(first, "reloaded");
            Wait.Until(() => !File.Exists(marker), "the reloaded shell to hide the taskbar");
            Assert.True(ExplorerTaskbarRecovery.AnyHidden());
            // Ended without its cleanup, as a crash would end it.
            using (var crashed = Process.GetProcessById(second))
            {
                crashed.Kill();
                crashed.WaitForExit();
            }

            NextPid(second, "reloaded");
            Assert.False(ExplorerTaskbarRecovery.AnyHidden());
            Assert.False(run.HasExited);
        }
        finally
        {
            File.WriteAllText(source, original);
        }
    }

    /// <summary>A reload ends a shell that does not finish quitting, so its supervisor can start the next one.</summary>
    [Fact(Timeout = BuildTimeout)]
    public void A_reload_under_winshell_run_ends_a_shell_that_hangs_on_its_way_out()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var hang = Path.Combine(_config, ProbeShell.HangMarker);
        File.WriteAllText(hang, "");
        var run = Supervise();
        var shell = NextPid(0, null);
        var pidFile = Path.Combine(_config, "child.pid");
        Wait.Until(() => File.Exists(pidFile), "the shell's program to start");
        // Only this shell hangs.
        File.Delete(hang);
        using var program = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture));
        using var hung = Process.GetProcessById(shell);
        try
        {
            Reload(shell);

            Assert.True(hung.HasExited);
            NextPid(shell, null);
            Assert.False(run.HasExited);
        }
        finally
        {
            program.Kill();
            program.WaitForExit(5000);
        }
    }

    /// <summary>Its supervisor starts only a project's last build, so without the project the shell keeps running.</summary>
    [Fact]
    public void A_reload_under_winshell_run_without_its_project_is_refused()
    {
        var run = Supervise();
        var shell = NextPid(0, null);
        var projectFile = Directory.GetFiles(_probe.Folder, "*.csproj").Single();
        File.Move(projectFile, projectFile + ".gone");
        try
        {
            var result = Run("reload", "--pid", shell.ToString(CultureInfo.InvariantCulture));

            var id = Listed().Single(row => row.GetProperty("pid").GetInt32() == shell).GetProperty("id").GetString();
            result.AssertExitCode(1);
            Assert.Equal("", result.Output);
            Assert.Equal(
                $"Could not find the project that builds instance {id}, so it was not reloaded.{Environment.NewLine}",
                result.Error);
            Assert.False(run.HasExited);
        }
        finally
        {
            File.Move(projectFile + ".gone", projectFile);
        }
    }

    /// <summary>Starts <c>winshell run</c> on this class's probe, which runs its last build.</summary>
    private Process Supervise()
    {
        var info = WinshellCli.StartInfo(["run", "--no-build", "-p", _probe.Folder]);
        info.Environment["WINSHELL_CONFIG_DIR"] = _config;
        info.Environment["WINSHELL_LOCAL_DIR"] = _local;
        var run = Process.Start(info)!;
        _started.Add(run);
        // Read the output so the supervisor never blocks on a full pipe.
        run.ErrorDataReceived += (_, _) => { };
        run.OutputDataReceived += (_, _) => { };
        run.BeginErrorReadLine();
        run.BeginOutputReadLine();
        return run;
    }

    /// <summary>The process id of a listed shell other than <paramref name="previous"/>, with <paramref name="shellId"/> when it is given.</summary>
    private int NextPid(int previous, string? shellId)
    {
        var pid = 0;
        Wait.Until(
            () =>
            {
                pid = Listed()
                    .Where(row =>
                        row.GetProperty("pid").GetInt32() != previous &&
                        (shellId is null || row.GetProperty("shellId").GetString() == shellId))
                    .Select(row => row.GetProperty("pid").GetInt32())
                    .FirstOrDefault();
                return pid != 0;
            },
            $"a shell after {previous} in winshell list");
        return pid;
    }

    private Process ReloadTo(Process running, string original, string shellId)
    {
        File.WriteAllText(Path.Combine(_probe.Folder, "Program.cs"),
            original.Replace("?? \"probe\"", $"?? \"{shellId}\"", StringComparison.Ordinal));
        Reload(running.Id);
        Wait.Until(() => running.HasExited, "the old process to exit");
        var pid = 0;
        Wait.Until(
            () =>
            {
                pid = Listed().Where(row => row.GetProperty("shellId").GetString() == shellId)
                    .Select(row => row.GetProperty("pid").GetInt32()).FirstOrDefault();
                return pid != 0;
            },
            $"the process that runs the {shellId} edit");
        var next = Process.GetProcessById(pid);
        _started.Add(next);
        return next;
    }

    /// <summary>Reloads the shell <paramref name="pid"/>, failing with what the reload printed unless it exits 0.</summary>
    private ChildProcess Reload(int pid)
    {
        var result = Run("reload", "--pid", pid.ToString(CultureInfo.InvariantCulture));
        result.AssertExitCode(0);
        return result;
    }

    [Fact]
    public void Reload_with_nothing_running_for_a_pid_is_a_miss()
    {
        var result = Run("reload", "--pid", "0");

        result.AssertExitCode(1);
        Assert.Equal("", result.Output);
        Assert.Equal("No instance with process id 0." + Environment.NewLine, result.Error);
    }

    private Process StartProbe()
    {
        var process = ProbeShell.Start(_config, _local, program: _probe.Program);
        _started.Add(process);
        return process;
    }

    private List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        result.AssertExitCode(0);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private ChildProcess Run(params string[] args)
    {
        return WinshellCli.Run(args, ("WINSHELL_CONFIG_DIR", _config), ("WINSHELL_LOCAL_DIR", _local));
    }

    public void Dispose()
    {
        List<JsonElement> rows = [];
        try
        {
            rows = Listed();
        }
        catch (XunitException)
        {
        }

        foreach (var row in rows)
            try
            {
                Run("kill", "--force", "--pid",
                    row.GetProperty("pid").GetInt32().ToString(CultureInfo.InvariantCulture));
            }
            catch (XunitException)
            {
            }

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

                process.WaitForExit();
            }

            process.Dispose();
        }

        // A test that failed can leave the taskbar its probe hid. One that another program hid stays hidden.
        if (!_taskbarHidden)
            ExplorerTaskbarRecovery.Restore();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }
}
