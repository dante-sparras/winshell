using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>--no-duplicate</c> holds a per-config mutex for the life of the shell, so a second start exits
/// even when the first has not written instance.json yet.
/// </summary>
public sealed class NoDuplicateTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];
    private readonly Dictionary<int, StringBuilder> _errors = [];

    [Fact]
    public void A_second_start_exits_and_another_config_stays()
    {
        var config = Config("one");
        var other = Config("two");
        var first = Start(config, duplicate: true);

        AssertHeld(ShellFile(config));

        var same = Run("run", "-n", "-p", OtherCase(config));
        Assert.Equal(0, same.Code);
        Assert.Equal("", same.Output);
        Assert.Equal("", same.Error);
        Assert.False(first.HasExited);

        var second = Start(other, duplicate: true);
        Assert.False(first.HasExited);
        Assert.False(second.HasExited);
        AssertHeld(ShellFile(other));
    }

    [Fact]
    public void A_shell_started_without_the_flag_still_blocks_a_duplicate()
    {
        var config = Config("open");
        var first = Start(config, duplicate: false);
        Assert.Throws<WaitHandleCannotBeOpenedException>(() => Mutex.OpenExisting(Cli.DuplicateMutexName(ShellFile(config))));

        var same = Run("run", "-n", "-p", config);
        Assert.Equal(0, same.Code);
        Assert.Equal("", same.Output);
        Assert.Equal("", same.Error);
        Assert.False(first.HasExited);
    }

    [Fact]
    public void Two_overlapping_starts_leave_one_shell()
    {
        var config = Config("race");
        var first = Launch(config, duplicate: true);
        var second = Launch(config, duplicate: true);

        WaitFor(() => first.HasExited || second.HasExited, "one of the two starts to exit");
        var loser = first.HasExited ? first : second;
        var winner = ReferenceEquals(loser, first) ? second : first;
        Assert.True(loser.WaitForExit(15_000));
        loser.WaitForExit();
        Assert.Equal(0, loser.ExitCode);
        Assert.Equal("", Errors(loser));

        WaitFor(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == winner.Id), $"instance {winner.Id}");
        Assert.False(winner.HasExited);
        Assert.DoesNotContain(Listed(), i => i.GetProperty("pid").GetInt32() == loser.Id);
    }

    [Fact]
    public void Daemonize_leaves_the_mutex_to_the_child()
    {
        var config = Config("daemon");
        // Do not redirect the parent. The child inherits those pipes, and reading them to the end waits until the shell exits.
        var parent = StartReturning("run", "-n", "-d", "-p", config);
        Assert.Equal(0, parent);

        var shell = ShellFile(config);
        WaitFor(() => Listed().Any(i => SameConfig(i, shell)), "the background shell");
        var pid = Listed().Single(i => SameConfig(i, shell)).GetProperty("pid").GetInt32();
        _started.Add(Process.GetProcessById(pid));
        AssertHeld(shell);

        var again = Run("run", "-n", "-p", config);
        Assert.Equal(0, again.Code);
        Assert.Equal("", again.Output);
        Assert.Equal("", again.Error);
        Assert.False(_started[^1].HasExited);
    }

    private string Config(string name)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), """
            import Winshell
            ShellRoot {}
            """);
        return dir;
    }

    private static string ShellFile(string configDir) => Path.GetFullPath(Path.Combine(configDir, "shell.qml"));

    private static string OtherCase(string path) =>
        path.Any(char.IsLower) ? path.ToUpperInvariant() : path.ToLowerInvariant();

    private Process Start(string configDir, bool duplicate)
    {
        var process = Launch(configDir, duplicate);
        WaitFor(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private Process Launch(string configDir, bool duplicate)
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("run");
        if (duplicate)
            info.ArgumentList.Add("-n");
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(configDir);
        info.Environment["WINSHELL_CONFIG_DIR"] = _dir;
        var process = Process.Start(info)!;
        _started.Add(process);
        _errors[process.Id] = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _errors[process.Id].AppendLine(e.Data);
        };
        process.BeginErrorReadLine();
        process.OutputDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        return process;
    }

    private static void AssertHeld(string shellFile)
    {
        using var mutex = Mutex.OpenExisting(Cli.DuplicateMutexName(shellFile));
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

    private string Errors(Process process) => _errors.TryGetValue(process.Id, out var text) ? text.ToString() : "";

    private static bool SameConfig(JsonElement row, string shellFile) =>
        string.Equals(row.GetProperty("configPath").GetString(), shellFile, StringComparison.OrdinalIgnoreCase);

    private static List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    /// <summary>Starts winshell and waits for that process only. A daemonized child is not this process, and it must not inherit a pipe.</summary>
    private static int StartReturning(params string[] args)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("winshell did not exit");
        }
        return process.ExitCode;
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("winshell did not exit");
        }
        return (process.ExitCode, output.Result, error.Result);
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
