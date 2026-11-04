using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell run</c> starts the default shell in this process. Each test has config and local folders of its own,
/// so it sees only its own shells and can't touch the user's.
/// </summary>
[Collection(DefaultShellCollection.Name)]
[Trait("Category", "Slow")]
public sealed partial class ShellProgramTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [GeneratedRegex(@"^Log: (?<path>.+), last \d+ lines\r?$", RegexOptions.Multiline)]
    private static partial Regex LogLine();

    [Fact]
    public void List_log_ipc_and_kill_reach_the_program()
    {
        var process = Start();
        var pid = process.Id.ToString(CultureInfo.InvariantCulture);

        var row = Info(process);
        Assert.Equal("winshell-default", row.GetProperty("shellId").GetString());
        Assert.Equal(WinshellCli.Exe, row.GetProperty("configPath").GetString(), ignoreCase: true);

        var log = Run("log", "--pid", pid);
        Assert.Equal(0, log.Code);
        Assert.Contains("Shell ID: \"winshell-default\"", log.Output, StringComparison.Ordinal);

        var show = Run("ipc", "--pid", pid, "show");
        Assert.Equal((0, ""), (show.Code, show.Error));
        var call = Run("ipc", "--pid", pid, "call", "nothing", "open");
        Assert.Equal((1, "Target not found: nothing."), (call.Code, call.Error.Trim()));

        var killed = Run("kill", "--pid", pid);
        Assert.Equal((0, ""), (killed.Code, killed.Error));
        Assert.StartsWith("Killed ", killed.Output, StringComparison.Ordinal);
        Assert.True(process.HasExited);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(Listed());

        // A clean exit keeps the record, marked exited, so the log can still be found.
        var stopped = Run("log");
        Assert.Equal(0, stopped.Code);
        Assert.Contains("Showing the log of stopped instance", stopped.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_with_the_desktop_chords_on_keeps_running()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "startHotkey = \"\"\ndesktopChords = true\n");

        var process = Start();

        // The chords' windows are made after the instance is listed.
        Wait.ForNothingToHappen(TimeSpan.FromSeconds(4));
        Assert.False(process.HasExited, "The shell ended while it started.");
    }

    [Fact]
    public void A_second_start_of_the_same_shell_id_exits_and_leaves_the_first()
    {
        var first = Start("--no-duplicate");

        var second = Launch(["-n"]);

        Wait.Until(() => second.HasExited, "the second start to exit");
        Assert.Equal(0, second.ExitCode);
        Assert.False(first.HasExited);
        Assert.Equal(first.Id, Assert.Single(Listed()).GetProperty("pid").GetInt32());
    }

    [Fact]
    public void A_reload_hands_over_to_a_new_process_of_the_same_program()
    {
        var first = Start("--no-duplicate");

        var reload = Run("reload", "--pid", first.Id.ToString(CultureInfo.InvariantCulture));

        Assert.Equal((0, ""), (reload.Code, reload.Error));
        Assert.StartsWith("Reloaded ", reload.Output, StringComparison.Ordinal);
        Wait.Until(() => first.HasExited, "the old shell to exit");
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() != first.Id), "the new shell in winshell list");
        var next = Assert.Single(Listed());
        Assert.Equal("winshell-default", next.GetProperty("shellId").GetString());

        // --no-duplicate came along, and the old process let go of the shell id.
        var duplicate = Launch(["--no-duplicate"]);
        Wait.Until(() => duplicate.HasExited, "a duplicate of the new shell to exit");
        Assert.Equal(next.GetProperty("id").GetString(), Assert.Single(Listed()).GetProperty("id").GetString());
    }

    /// <summary>
    /// A crash after the config loaded, as an exception escaping the loop would be: one fatal report, written before
    /// the log closes, and reporting it again as the process ends throws nothing. The report goes to a temp folder
    /// through <c>WINSHELL_TEST_CRASH</c>, never to the user's crash folder.
    /// </summary>
    [Fact]
    public void A_crash_as_the_shell_starts_writes_one_report_and_leaves_no_running_instance()
    {
        var crashes = Path.Combine(_dir, "crashes");

        var (code, output, error) = WinshellCli.Run(["run"], [.. Folders, ("WINSHELL_TEST_CRASH", crashes)]);

        Assert.Equal(1, code);
        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(crashes)));
        Assert.Contains("Winshell closed: yes", report, StringComparison.Ordinal);
        Assert.Contains("Where: Winshell", report, StringComparison.Ordinal);
        Assert.Contains("Shell id: winshell-default", report, StringComparison.Ordinal);
        Assert.Contains("asked Winshell to crash as it started", report, StringComparison.Ordinal);
        Assert.Contains("Shell ID: \"winshell-default\"", error + output, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectDisposedException", error + output, StringComparison.Ordinal);
        // The crash and its report are in the log, written before the log file closed.
        var logged = File.ReadAllText(LogLine().Match(report).Groups["path"].Value);
        Assert.Contains("FATAL: Winshell crashed: System.InvalidOperationException", logged, StringComparison.Ordinal);
        Assert.Contains("Wrote a crash report to", logged, StringComparison.Ordinal);
        Assert.Empty(Listed());

        // Its shell id is free again, with no mutex or record left behind.
        var next = Start("--no-duplicate");
        Assert.False(next.HasExited);
    }

    /// <summary><c>winshell run -d</c> leaves the default shell to a child and returns, so the child holds the shell id.</summary>
    [Fact]
    public void A_detached_start_leaves_the_shell_id_to_the_child()
    {
        // WinshellCli reads the output to its end, so the child must not hold it open.
        Assert.Equal(0, WinshellCli.Run(["run", "-n", "-d"], Folders).Code);

        Wait.Until(() => Listed().Count > 0, "the background shell");
        var child = Assert.Single(Listed());
        Assert.Equal("winshell-default", child.GetProperty("shellId").GetString());

        var duplicate = Launch(["-n"]);
        Wait.Until(() => duplicate.HasExited, "a duplicate of the background shell to exit");
        Assert.Equal(child.GetProperty("pid").GetInt32(), Assert.Single(Listed()).GetProperty("pid").GetInt32());
    }

    private Process Start(params string[] args)
    {
        var process = Launch(args);
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private Process Launch(string[] args)
    {
        var info = new ProcessStartInfo(WinshellCli.Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("run");
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        var process = Process.Start(info)!;
        _started.Add(process);
        // Read the output so the shell never blocks on a full pipe.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.OutputDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        return process;
    }

    private (string Name, string Value)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")),
    ];

    private JsonElement Info(Process process) => Listed().Single(i => i.GetProperty("pid").GetInt32() == process.Id);

    private List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args, Folders);

    public void Dispose()
    {
        // A reload starts a process no test holds, so the list decides what is still running.
        try
        {
            foreach (var row in Listed())
                Run("kill", "--force", "--pid", row.GetProperty("pid").GetInt32().ToString(CultureInfo.InvariantCulture));
        }
        catch (Xunit.Sdk.XunitException)
        {
        }
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
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
