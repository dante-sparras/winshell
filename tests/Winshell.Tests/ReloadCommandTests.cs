using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell reload</c> builds a configuration project and then asks the instance to start the next process.
/// A build that fails leaves that instance running and tells it to show the notice. These tests edit and rebuild the
/// probe, so they build one of their own that no other test starts.
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

            Assert.Equal(1, result.Code);
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
            var result = Run("reload", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));

            Assert.Equal(0, result.Code);
            Assert.Equal("", result.Error);
            Assert.StartsWith("Reloaded ", result.Output, StringComparison.Ordinal);
            Wait.Until(() => process.HasExited, "the old process to exit");
            Wait.Until(
                () => Listed().Any(row => row.GetProperty("shellId").GetString() == "probe" && row.GetProperty("pid").GetInt32() != process.Id),
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

    private Process ReloadTo(Process running, string original, string shellId)
    {
        File.WriteAllText(Path.Combine(_probe.Folder, "Program.cs"), original.Replace("?? \"probe\"", $"?? \"{shellId}\"", StringComparison.Ordinal));
        var result = Run("reload", "--pid", running.Id.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(0, result.Code);
        Wait.Until(() => running.HasExited, "the old process to exit");
        var pid = 0;
        Wait.Until(
            () =>
            {
                pid = Listed().Where(row => row.GetProperty("shellId").GetString() == shellId).Select(row => row.GetProperty("pid").GetInt32()).FirstOrDefault();
                return pid != 0;
            },
            $"the process that runs the {shellId} edit");
        var next = Process.GetProcessById(pid);
        _started.Add(next);
        return next;
    }

    [Fact]
    public void Reload_with_nothing_running_for_a_pid_is_a_miss()
    {
        var result = Run("reload", "--pid", "0");

        Assert.Equal(1, result.Code);
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
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args, ("WINSHELL_CONFIG_DIR", _config), ("WINSHELL_LOCAL_DIR", _local));

    public void Dispose()
    {
        List<JsonElement> rows = [];
        try
        {
            rows = Listed();
        }
        catch (Xunit.Sdk.XunitException)
        {
        }
        foreach (var row in rows)
        {
            try
            {
                Run("kill", "--force", "--pid", row.GetProperty("pid").GetInt32().ToString(CultureInfo.InvariantCulture));
            }
            catch (Xunit.Sdk.XunitException)
            {
            }
        }
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
                process.WaitForExit();
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
