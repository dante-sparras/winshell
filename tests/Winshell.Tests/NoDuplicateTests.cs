using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>--no-duplicate</c> holds a per-shell-id mutex for the life of the shell, so a second start exits
/// even when the first has not written instance.json yet. Two programs with different shell ids both stay.
/// </summary>
[Trait("Category", "Slow")]
public sealed class NoDuplicateTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly string _local;
    private readonly string _config;
    private readonly List<Process> _started = [];
    private readonly Dictionary<int, StringBuilder> _printed = [];

    public NoDuplicateTests()
    {
        _local = Path.Combine(_dir, "local");
        _config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(_local);
        Directory.CreateDirectory(_config);
    }

    [Fact]
    public void A_second_start_exits_and_another_shell_stays()
    {
        var first = Start(duplicate: true);
        AssertHeld("probe");

        var same = Launch(duplicate: true);
        AssertExitedQuietly(same);
        Assert.False(first.HasExited);

        var second = Start(duplicate: true, "other");
        Assert.False(first.HasExited);
        Assert.False(second.HasExited);
        AssertHeld("other");
    }

    [Fact]
    public void A_shell_started_without_the_flag_still_blocks_a_duplicate()
    {
        var first = Start(duplicate: false);
        Assert.Throws<WaitHandleCannotBeOpenedException>(() => Mutex.OpenExisting(MutexName("probe")));

        AssertExitedQuietly(Launch(duplicate: true));
        Assert.False(first.HasExited);
    }

    [Fact]
    public void Two_overlapping_starts_leave_one_shell()
    {
        var first = Launch(duplicate: true);
        var second = Launch(duplicate: true);

        Wait.Until(() => first.HasExited || second.HasExited, "one of the two starts to exit");
        var loser = first.HasExited ? first : second;
        var winner = ReferenceEquals(loser, first) ? second : first;
        AssertExitedQuietly(loser);

        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == winner.Id), $"instance {winner.Id}");
        Assert.False(winner.HasExited);
        Assert.DoesNotContain(Listed(), i => i.GetProperty("pid").GetInt32() == loser.Id);
    }

    [Fact]
    public void Daemonize_leaves_the_mutex_to_the_child()
    {
        ProbeShell.Detach(_config, _local, ["--no-duplicate", "-d"]).AssertExitCode(0);

        Wait.Until(() => Listed().Any(i => i.GetProperty("shellId").GetString() == "probe"), "the background shell");
        var pid = Listed().Single(i => i.GetProperty("shellId").GetString() == "probe").GetProperty("pid").GetInt32();
        var child = Process.GetProcessById(pid);
        _started.Add(child);
        AssertHeld("probe");

        AssertExitedQuietly(Launch(duplicate: true));
        Assert.False(child.HasExited);
    }

    private Process Start(bool duplicate, string? shellId = null)
    {
        var process = Launch(duplicate, shellId);
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private Process Launch(bool duplicate, string? shellId = null)
    {
        var printed = new StringBuilder();
        var process = ProbeShell.Launch(_config, _local, duplicate ? ["--no-duplicate"] : [], shellId, printed);
        _started.Add(process);
        _printed[process.Id] = printed;
        return process;
    }

    private void AssertExitedQuietly(Process process)
    {
        Assert.True(process.WaitForExit(20_000));
        // Without a timeout this waits for the output to be read to its end.
        process.WaitForExit();
        ChildProcess.Exited("The probe", process, _printed[process.Id]).AssertExitCode(0);
        Assert.Equal("", _printed[process.Id].ToString());
    }

    private void AssertHeld(string shellId)
    {
        using var mutex = Mutex.OpenExisting(MutexName(shellId));
        var taken = false;
        try
        {
            taken = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            taken = true;
        }
        finally
        {
            if (taken)
                mutex.ReleaseMutex();
        }
        Assert.False(taken);
    }

    private string MutexName(string shellId) => DuplicateHold.ShellMutexName(shellId, _local);

    private List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        result.AssertExitCode(0);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private ChildProcess Run(params string[] args) =>
        WinshellCli.Run(args, ("WINSHELL_CONFIG_DIR", _config), ("WINSHELL_LOCAL_DIR", _local));

    public void Dispose()
    {
        foreach (var process in _started.ToList())
        {
            try
            {
                if (!process.HasExited)
                {
                    Run("kill", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));
                    if (!process.WaitForExit(8_000))
                        process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }
            }
            process.Dispose();
        }
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (Exception)
        {
        }
    }
}
