using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell run</c> starts the default shell in a child process and supervises it. Each test has config and local
/// folders of its own, so it sees only its own shells and can't touch the user's.
/// </summary>
[Collection(DefaultShellCollection.Name)]
[Trait("Category", "Slow")]
public sealed class ShellProgramTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void A_shell_with_the_desktop_chords_on_keeps_running()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "preferences.toml"), "startHotkey = \"\"\ndesktopChords = true\n");

        var process = Start().Shell;

        // The chords' windows are made after the instance is listed.
        Wait.ForNothingToHappen(TimeSpan.FromSeconds(4));
        Assert.False(process.HasExited, "The shell ended while it started.");
    }

    [Fact]
    public void A_second_start_of_the_same_shell_id_exits_and_leaves_the_first()
    {
        var first = Start("--no-duplicate").Shell;

        var second = Launch(["-n"]);

        Wait.Until(() => second.HasExited, "the second start to exit");
        Assert.Equal(0, second.ExitCode);
        Assert.False(first.HasExited);
        Assert.Equal(first.Id, Assert.Single(Listed()).GetProperty("pid").GetInt32());
    }

    [Fact]
    public void A_reload_hands_over_to_a_new_process_of_the_same_program()
    {
        var (run, first) = Start("--no-duplicate");

        var reload = Run("reload", "--pid", first.Id.ToString(CultureInfo.InvariantCulture));

        Assert.Equal((0, ""), (reload.Code, reload.Error));
        Assert.StartsWith("Reloaded ", reload.Output, StringComparison.Ordinal);
        Wait.Until(() => first.HasExited, "the old shell to exit");
        // The winshell run of the old shell ends with it: the new process runs under a winshell run of its own.
        Wait.Until(() => run.HasExited, "the old shell's winshell run to end");
        Assert.Equal(0, run.ExitCode);
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() != first.Id), "the new shell in winshell list");
        var next = Assert.Single(Listed());
        Assert.Equal("winshell-default", next.GetProperty("shellId").GetString());

        // --no-duplicate came along, and the old process let go of the shell id.
        var duplicate = Launch(["--no-duplicate"]);
        Wait.Until(() => duplicate.HasExited, "a duplicate of the new shell to exit");
        Assert.Equal(next.GetProperty("id").GetString(), Assert.Single(Listed()).GetProperty("id").GetString());
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

    /// <summary>Starts <c>winshell run</c>, and returns it with the shell it runs once <c>winshell list</c> shows that.</summary>
    private (Process Run, Process Shell) Start(params string[] args)
    {
        var before = Listed().Select(i => i.GetProperty("pid").GetInt32()).ToHashSet();
        var run = Launch(args);
        var pid = 0;
        Wait.Until(() => (pid = Listed().Select(i => i.GetProperty("pid").GetInt32()).FirstOrDefault(p => !before.Contains(p))) != 0, "the new shell in winshell list");
        var shell = Process.GetProcessById(pid);
        // Opened while it runs, so its exit code can still be read once it has exited.
        _ = shell.SafeHandle;
        _started.Add(shell);
        return (run, shell);
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
        // A winshell run ends once its shell has. One that has not would start the shell again.
        foreach (var process in _started)
        {
            if (!process.WaitForExit(5000))
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
