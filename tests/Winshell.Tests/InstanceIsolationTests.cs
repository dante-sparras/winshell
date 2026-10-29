using System.Diagnostics;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// Runs several winshell instances at once, as separate processes, and checks that they keep apart: each has its own
/// id, pipe and log, each answers for itself, and one that crashes or has a broken config leaves the others running.
/// </summary>
public sealed class InstanceIsolationTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    /// <summary>A config whose <c>probe</c> IPC target answers with <paramref name="name"/>.</summary>
    private string Config(string folder, string name, string? shellId = null)
    {
        var dir = Path.Combine(_dir, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), $$"""
            {{(shellId is null ? "" : $"//@ pragma ShellId {shellId}")}}
            import Winshell
            import Winshell.Io

            ShellRoot {
              IpcHandler {
                target: "probe"
                function name(): string { return "{{name}}" }
              }
            }
            """);
        return dir;
    }

    [Fact]
    public void Instances_keep_apart_and_outlive_one_that_crashes()
    {
        // Two configs that are one shell, by their ShellId, and a third that is another.
        var a = Start(Config("a", "a", shellId: "shared"));
        var b = Start(Config("b", "b", shellId: "shared"));
        var c = Start(Config("c", "c"));
        var infos = new[] { a, b, c }.Select(Info).ToList();

        Assert.Equal(3, infos.Select(i => i.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(3, infos.Select(i => i.GetProperty("pipe").GetString()).Distinct().Count());
        Assert.Equal(3, infos.Select(i => i.GetProperty("log").GetString()).Distinct().Count());
        Assert.Equal("shared", infos[0].GetProperty("shellId").GetString());
        Assert.Equal("shared", infos[1].GetProperty("shellId").GetString());
        Assert.NotEqual("shared", infos[2].GetProperty("shellId").GetString());
        Assert.Equal("a", Ask(a));
        Assert.Equal("b", Ask(b));
        Assert.Equal("c", Ask(c));

        // B dies without cleaning up, as a crash would leave it.
        b.Kill();
        b.WaitForExit();

        Assert.Equal("a", Ask(a));
        Assert.Equal("c", Ask(c));
        Assert.DoesNotContain(Listed(), i => i.GetProperty("pid").GetInt32() == b.Id);
    }

    [Fact]
    public void A_broken_config_reports_where_and_keeps_its_instance_running()
    {
        var good = Start(Config("good", "good"));
        var dir = Config("broken", "fixed");
        var shell = Path.Combine(dir, "shell.qml");
        var text = File.ReadAllText(shell);
        File.WriteAllText(shell, text.Replace("IpcHandler {", "IpcHandler {\n    colour: \"red\"", StringComparison.Ordinal));
        var broken = Start(dir);
        var log = Info(broken).GetProperty("log").GetString()!;

        WaitFor(() => File.Exists(log) && Read(log).Contains("Failed to load configuration", StringComparison.Ordinal), "the load error in the log");
        Assert.Contains("shell.qml[7:5]: Cannot assign to non-existent property \"colour\"", Read(log), StringComparison.Ordinal);
        Assert.False(broken.HasExited);
        Assert.Equal("good", Ask(good));

        // Fixed, it loads where it is.
        File.WriteAllText(shell, text);
        WaitFor(() => Read(log).Contains("Configuration Loaded", StringComparison.Ordinal), "the reload in the log");
        Assert.Equal("fixed", Ask(broken));
    }

    /// <summary>
    /// A killed instance leaves its file, and Windows can give that process id to the next winshell.
    /// The list then has to name the process that is running, not the file the dead one left.
    /// </summary>
    [Fact]
    public void A_reused_process_id_is_listed_for_the_instance_that_owns_it()
    {
        var process = Start(Config("once", "once"));
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
                ProcessStart: 1));
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

    /// <summary>Starts an instance and waits until it has written itself into the instance list.</summary>
    private Process Start(string configDir)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(configDir);
        // WINSHELL_ variables the developer set must not change these instances.
        info.Environment.Remove("WINSHELL_DISABLE_FILE_WATCHER");
        var process = Process.Start(info)!;
        _started.Add(process);
        // Read the output so the instance never blocks on a full pipe.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        WaitFor(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private static JsonElement Info(Process process) => Listed().Single(i => i.GetProperty("pid").GetInt32() == process.Id);

    private static List<JsonElement> Listed()
    {
        using var json = JsonDocument.Parse(Winshell("list", "--json"));
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static string Ask(Process process) => Winshell("ipc", "--pid", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "call", "probe", "name").Trim();

    /// <summary>Runs the winshell CLI and returns what it printed.</summary>
    private static string Winshell(params string[] args)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"winshell {string.Join(' ', args)} exited with {process.ExitCode}: {error.Result}");
        return output;
    }

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WaitFor(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < Patience, $"Timed out waiting for {what}");
            Thread.Sleep(100);
        }
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
                catch (Xunit.Sdk.TrueException)
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
