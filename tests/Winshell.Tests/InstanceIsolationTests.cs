using System.Diagnostics;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// Runs several winshell instances at once, as separate processes, and checks that they keep apart: each has its own
/// id, pipe and log, each answers for itself, and one that crashes or has a broken config leaves the others running.
/// </summary>
[Trait("Category", "Slow")]
public sealed class InstanceIsolationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    private string Config(string folder)
    {
        var dir = Path.Combine(_dir, folder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Instances_keep_apart_and_outlive_one_that_crashes()
    {
        // Two start with one shell id, and the third is another shell.
        var a = Start(Config("a"));
        var b = Start(Config("b"));
        var c = Start(Config("c"), "other");
        var infos = new[] { a, b, c }.Select(Info).ToList();

        Assert.Equal(3, infos.Select(i => i.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(3, infos.Select(i => i.GetProperty("pipe").GetString()).Distinct().Count());
        Assert.Equal(3, infos.Select(i => i.GetProperty("log").GetString()).Distinct().Count());
        Assert.Equal("probe", infos[0].GetProperty("shellId").GetString());
        Assert.Equal("probe", infos[1].GetProperty("shellId").GetString());
        Assert.Equal("other", infos[2].GetProperty("shellId").GetString());
        Assert.Equal(0, Show(a));
        Assert.Equal(0, Show(b));
        Assert.Equal(0, Show(c));

        // B dies without cleaning up, as a crash would leave it.
        b.Kill();
        b.WaitForExit();

        Assert.Equal(0, Show(a));
        Assert.Equal(0, Show(c));
        Assert.DoesNotContain(Listed(), i => i.GetProperty("pid").GetInt32() == b.Id);
    }

    /// <summary>
    /// A killed instance leaves its file, and Windows can give that process id to the next winshell.
    /// The list then has to name the process that is running, not the file the dead one left.
    /// </summary>
    [Fact]
    public void A_reused_process_id_is_listed_for_the_instance_that_owns_it()
    {
        var process = Start(Config("once"));
        var live = Info(process);
        var liveId = live.GetProperty("id").GetString()!;
        var staleId = Instances.NewId();
        var dir = Instances.DirOf(staleId);
        Directory.CreateDirectory(dir);
        // The shape left on disk before processStart was recorded: same pid, earlier launch, no processStart.
        var legacy = File.ReadAllText(Path.Combine(Instances.DirOf(liveId), "instance.json"));
        legacy = legacy.Replace($"\"id\": \"{liveId}\"", $"\"id\": \"{staleId}\"", StringComparison.Ordinal);
        legacy = legacy.Replace($"\"pipe\": \"winshell-{liveId}\"", $"\"pipe\": \"winshell-{staleId}\"", StringComparison.Ordinal);
        var launched = live.GetProperty("launchTime").GetDateTime().AddHours(-3);
        legacy = System.Text.RegularExpressions.Regex.Replace(
            legacy,
            "\"launchTime\": \"[^\"]+\"",
            "\"launchTime\": \"" + launched.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\"");
        legacy = System.Text.RegularExpressions.Regex.Replace(legacy, ",\\s*\"processStart\": \\d+", "");
        File.WriteAllText(Path.Combine(dir, "instance.json"), legacy);
        var reusedId = Instances.NewId();
        var reusedDir = Instances.DirOf(reusedId);
        try
        {
            // This process id already belongs to a winshell, but the file records a different process start.
            Instances.Write(new InstanceInfo(
                reusedId,
                process.Id,
                "reused",
                live.GetProperty("configPath").GetString()!,
                DateTime.Now,
                "winshell-" + reusedId,
                Path.Combine(reusedDir, "log.txt"),
                ProcessStart: 1,
                ProcessName: process.ProcessName));
            var rows = Listed().Where(i => i.GetProperty("pid").GetInt32() == process.Id).ToList();
            Assert.Single(rows);
            Assert.Equal(liveId, rows[0].GetProperty("id").GetString());
        }
        finally
        {
            Instances.Remove(staleId);
            Instances.Remove(reusedId);
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
            try
            {
                Directory.Delete(reusedDir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private Process Start(string configDir, string? shellId = null)
    {
        var process = ProbeShell.Start(configDir, null, shellId);
        _started.Add(process);
        return process;
    }

    private static JsonElement Info(Process process) => Listed().Single(i => i.GetProperty("pid").GetInt32() == process.Id);

    private static List<JsonElement> Listed()
    {
        using var json = JsonDocument.Parse(Winshell("list", "--json"));
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static int Show(Process process)
    {
        var (code, _, error) = WinshellCli.Run(["ipc", "--pid", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "show"]);
        Assert.True(code == 0, error);
        return code;
    }

    /// <summary>Runs the winshell CLI and returns what it printed.</summary>
    private static string Winshell(params string[] args)
    {
        var (code, output, error) = WinshellCli.Run(args);
        Assert.True(code == 0, $"winshell {string.Join(' ', args)} exited with {code}: {error}");
        return output;
    }

    public void Dispose()
    {
        // Only the instances this test started, by their process ids: asked to quit, then ended if they do not.
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                try
                {
                    Winshell("kill", "--pid", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Xunit.Sdk.XunitException)
                {
                }
                if (!process.WaitForExit(5000))
                    process.Kill();
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
