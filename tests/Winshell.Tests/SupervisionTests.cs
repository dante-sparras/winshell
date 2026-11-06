using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell run</c> supervises the probe as a configuration's program. The probe hides Explorer's real taskbar when a
/// test asks, so these tests take the taskbar from the desktop they run on, and put it back however they end.
/// </summary>
[Trait("Category", "Slow")]
public sealed partial class SupervisionTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];
    private readonly StringBuilder _printed = new();
    private readonly bool _taskbarHidden = ExplorerTaskbarRecovery.AnyHidden();

    public SupervisionTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        Directory.CreateDirectory(Path.Combine(_dir, "local"));
    }

    private (string, string)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local"))
    ];

    [GeneratedRegex(@"^Log: (?<path>.+), last \d+ lines\r?$", RegexOptions.Multiline)]
    private static partial Regex LogLine();

    // The probe's program is <project>\bin\shell\<name>.exe.
    private static string Project =>
        Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ProbeShell.Program)))!;

    [Fact]
    public void List_log_ipc_and_kill_reach_the_program()
    {
        var run = Supervise();
        using var process = Process.GetProcessById(NextShell(0));
        // Opened while it runs, so its exit code can still be read once it has exited.
        _ = process.SafeHandle;
        var pid = process.Id.ToString(CultureInfo.InvariantCulture);

        var row = Assert.Single(Rows());
        Assert.Equal("probe", row.GetProperty("shellId").GetString());
        Assert.Equal(ProbeShell.Program, row.GetProperty("configPath").GetString(), true);

        var log = Cli("log", "--pid", pid);
        log.AssertExitCode(0);
        Assert.Contains("Shell ID: \"probe\"", log.Output, StringComparison.Ordinal);

        var show = Cli("ipc", "--pid", pid, "show");
        show.AssertExitCode(0);
        Assert.Equal("", show.Error);
        var call = Cli("ipc", "--pid", pid, "call", "nothing", "open");
        call.AssertExitCode(1);
        Assert.Equal("Target not found: nothing.", call.Error.Trim());

        var killed = Cli("kill", "--pid", pid);
        killed.AssertExitCode(0);
        Assert.Equal("", killed.Error);
        Assert.StartsWith("Killed ", killed.Output, StringComparison.Ordinal);
        Assert.True(process.HasExited);
        // The shell writes to the supervisor's output, so what it printed is there.
        ChildProcess.Exited("The probe", process, _printed).AssertExitCode(0);
        Assert.Empty(Listed());
        // A kill was asked for, so winshell run ends with the shell and does not start it again.
        Wait.Until(() => run.HasExited, "winshell run to end with its shell");
        ChildProcess.Exited("winshell run", run, _printed).AssertExitCode(0);

        // A clean exit keeps the record, marked exited, so the log can still be found.
        var stopped = Cli("log");
        stopped.AssertExitCode(0);
        Assert.Contains("Showing the log of stopped instance", stopped.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration that crashes as it starts: one fatal report, written before the log closes, and reporting it again
    /// as the process ends throws nothing. A shell that could not start is not started again. The report goes to the
    /// test's own local folder, never to the user's crash folder.
    /// </summary>
    [Fact]
    public void A_crash_as_the_shell_starts_writes_one_report_and_leaves_no_running_instance()
    {
        File.WriteAllText(Path.Combine(_dir, "config", ProbeShell.CrashMarker), "");

        var result = Cli("run", "--no-build", "-p", Project);

        result.AssertExitCode(1);
        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_dir, "local", "crashes"))));
        Assert.Contains("Winshell closed: yes", report, StringComparison.Ordinal);
        Assert.Contains("Where: Winshell", report, StringComparison.Ordinal);
        Assert.Contains("Shell id: probe", report, StringComparison.Ordinal);
        Assert.Contains("The probe was asked to crash as it started.", report, StringComparison.Ordinal);
        Assert.Contains("Shell ID: \"probe\"", result.Error + result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectDisposedException", result.Error + result.Output, StringComparison.Ordinal);
        // The crash and its report are in the log, written before the log file closed.
        var logged = File.ReadAllText(LogLine().Match(report).Groups["path"].Value);
        Assert.Contains("FATAL: Winshell crashed: System.InvalidOperationException", logged, StringComparison.Ordinal);
        Assert.Contains("Wrote a crash report to", logged, StringComparison.Ordinal);
        Assert.Empty(Listed());

        // Its shell id is free again, with no mutex or record left behind.
        var next = ProbeShell.Launch(Path.Combine(_dir, "config"), Path.Combine(_dir, "local"), ["--no-duplicate"]);
        _started.Add(next);
        Assert.Equal(next.Id, NextShell(0));
        Assert.False(next.HasExited);
    }

    [Fact]
    public void A_crashed_shell_starts_again_with_the_taskbar_back()
    {
        Assert.False(_taskbarHidden, "Explorer's taskbar was hidden before the test.");
        var marker = Path.Combine(_dir, "config", ProbeShell.HideTaskbarMarker);
        File.WriteAllText(marker, "");
        var run = Supervise();
        var first = NextShell(0);
        Wait.Until(() => !File.Exists(marker), "the shell to hide the taskbar");
        Assert.True(ExplorerTaskbarRecovery.AnyHidden());

        // Ended without its cleanup, as a crash would end it.
        using (var crashed = Process.GetProcessById(first))
        {
            crashed.Kill();
            crashed.WaitForExit();
        }

        NextShell(first);

        // The taskbar came back before the second shell started, and that shell found no file telling it to hide it.
        Assert.False(ExplorerTaskbarRecovery.AnyHidden());
        Assert.False(run.HasExited);
    }

    /// <summary>A forced kill ends a shell that did not quit when asked. Its supervisor takes that for a stop, not a crash.</summary>
    [Fact]
    public void A_forced_kill_of_a_hung_shell_ends_supervision()
    {
        File.WriteAllText(Path.Combine(_dir, "config", ProbeShell.HangMarker), "");
        var run = Supervise();
        var shell = NextShell(0);
        var pidFile = Path.Combine(_dir, "config", "child.pid");
        Wait.Until(() => File.Exists(pidFile), "the shell's program to start");
        using var program = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture));
        try
        {
            var killed = Cli("kill", "--force", "--pid", shell.ToString(CultureInfo.InvariantCulture));

            killed.AssertExitCode(0);
            Wait.Until(() => run.HasExited, "winshell run to end with its shell");
            // The exit code of the end --force gave it.
            ChildProcess.Exited("winshell run", run, _printed).AssertExitCode(-1);
            Assert.Empty(Listed());
        }
        finally
        {
            program.Kill();
            program.WaitForExit(5000);
        }
    }

    /// <summary>Starts <c>winshell run</c> on the probe's last build.</summary>
    private Process Supervise()
    {
        var info = WinshellCli.StartInfo(["run", "--no-build", "-p", Project]);
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        var run = Process.Start(info)!;
        _started.Add(run);

        // Read the output so the supervisor never blocks on a full pipe, and keep it for a wrong exit code to show.
        void Print(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
                lock (_printed)
                {
                    _printed.AppendLine(e.Data);
                }
        }

        run.ErrorDataReceived += Print;
        run.OutputDataReceived += Print;
        run.BeginErrorReadLine();
        run.BeginOutputReadLine();
        return run;
    }

    /// <summary>The process id of the shell <c>winshell list</c> shows once it is another than <paramref name="previous"/>.</summary>
    private int NextShell(int previous)
    {
        var pid = 0;
        Wait.Until(() => (pid = Listed().FirstOrDefault(p => p != previous)) != 0, "the shell in winshell list");
        return pid;
    }

    private List<int> Listed()
    {
        return Rows().Select(row => row.GetProperty("pid").GetInt32()).ToList();
    }

    private List<JsonElement> Rows()
    {
        using var listed = JsonDocument.Parse(Cli("list", "--json").Output);
        return listed.RootElement.EnumerateArray().Select(row => row.Clone()).ToList();
    }

    private ChildProcess Cli(params string[] args)
    {
        return WinshellCli.Run(args, Folders);
    }

    public void Dispose()
    {
        foreach (var run in _started)
        {
            if (!run.HasExited)
            {
                run.Kill(true);
                run.WaitForExit();
            }

            run.Dispose();
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
