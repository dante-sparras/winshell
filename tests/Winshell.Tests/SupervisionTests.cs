using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell run</c> supervises the probe as a configuration's program. The probe hides Explorer's real taskbar when a
/// test asks, so these tests take the taskbar from the desktop they run on, and put it back however they end.
/// </summary>
[Trait("Category", "Slow")]
public sealed class SupervisionTests : IDisposable, IClassFixture<ProbeBuild>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];
    private readonly bool _taskbarHidden = ExplorerTaskbarRecovery.AnyHidden();

    public SupervisionTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "config"));
        Directory.CreateDirectory(Path.Combine(_dir, "local"));
    }

    private (string, string)[] Folders =>
    [
        ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "config")),
        ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")),
    ];

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

            Assert.Equal(0, killed.Code);
            Wait.Until(() => run.HasExited, "winshell run to end with its shell");
            // The exit code of the end --force gave it.
            Assert.Equal(-1, run.ExitCode);
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
        // The probe's program is <project>\bin\shell\<name>.exe.
        var project = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ProbeShell.Program)))!;
        var info = WinshellCli.StartInfo(["run", "--no-build", "-p", project]);
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        var run = Process.Start(info)!;
        _started.Add(run);
        // Read the output so the supervisor never blocks on a full pipe.
        run.ErrorDataReceived += (_, _) => { };
        run.OutputDataReceived += (_, _) => { };
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
        using var listed = JsonDocument.Parse(Cli("list", "--json").Output);
        return listed.RootElement.EnumerateArray().Select(row => row.GetProperty("pid").GetInt32()).ToList();
    }

    private (int Code, string Output, string Error) Cli(params string[] args) => WinshellCli.Run(args, Folders);

    public void Dispose()
    {
        foreach (var run in _started)
        {
            if (!run.HasExited)
            {
                run.Kill(entireProcessTree: true);
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
