using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell reload</c> builds a configuration project and then asks the instance to start the next process.
/// A build that fails leaves that instance running and tells it to show the notice.
/// </summary>
[Trait("Category", "Slow")]
public sealed class ReloadCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _config;
    private readonly List<Process> _started = [];

    public ReloadCommandTests()
    {
        _local = Path.Combine(_dir, "local");
        _config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(_local);
        Directory.CreateDirectory(_config);
    }

    [Fact]
    public void A_build_that_fails_leaves_the_instance_running()
    {
        lock (ProbeShell.Gate)
        {
            var source = Path.Combine(ProbeShell.Folder, "Program.cs");
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
    }

    [Fact]
    public void A_rebuild_starts_the_next_process()
    {
        lock (ProbeShell.Gate)
        {
            var source = Path.Combine(ProbeShell.Folder, "Program.cs");
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
    }

    [Fact]
    public void Reload_with_nothing_running_for_a_pid_is_a_miss()
    {
        var result = Run("reload", "--pid", "0");

        Assert.Equal(1, result.Code);
        Assert.Equal("", result.Output);
        Assert.Equal("No instance with process id 0" + Environment.NewLine, result.Error);
    }

    private Process StartProbe()
    {
        var process = ProbeShell.Start(_config, _local);
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
