using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// winshell-next.exe is a configuration's own program: it calls <c>Shell.Run</c>, and the CLI reaches it as it reaches a
/// QML instance. Each test has config and local folders of its own, so it sees only its own shells and can't touch the
/// user's.
/// </summary>
public sealed class ShellProgramTests : IDisposable
{
    private static readonly string Next = Path.GetFullPath(typeof(ShellProgramTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "WinshellNext").Value!);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void List_log_ipc_and_kill_reach_the_program()
    {
        var process = Start();
        var pid = process.Id.ToString(CultureInfo.InvariantCulture);

        var row = Info(process);
        Assert.Equal("winshell-default", row.GetProperty("shellId").GetString());
        Assert.Equal(Next, row.GetProperty("configPath").GetString(), ignoreCase: true);

        var log = Run("log", "--pid", pid);
        Assert.Equal(0, log.Code);
        Assert.Contains("Shell ID: \"winshell-default\"", log.Output, StringComparison.Ordinal);

        var show = Run("ipc", "--pid", pid, "show");
        Assert.Equal((0, ""), (show.Code, show.Error));
        var call = Run("ipc", "--pid", pid, "call", "settings", "open");
        Assert.Equal((1, "Target not found: settings"), (call.Code, call.Error.Trim()));

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
    public void A_second_start_of_the_same_shell_id_exits_and_leaves_the_first()
    {
        var first = Start("--no-duplicate");

        var second = Launch(["--no-duplicate"]);

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

    [Fact]
    public void A_start_that_fails_reports_it_and_leaves_no_running_instance()
    {
        var crashes = Path.Combine(_dir, "crashes");

        var process = Launch([], crashes);

        Wait.Until(() => process.HasExited, "the failed start to exit");
        Assert.Equal(1, process.ExitCode);
        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(crashes)));
        Assert.Contains("Winshell closed: yes", report, StringComparison.Ordinal);
        Assert.Contains("Shell id: winshell-default", report, StringComparison.Ordinal);
        Assert.Contains("asked Winshell to crash as it started", report, StringComparison.Ordinal);
        Assert.Empty(Listed());

        // Its shell id is free again, with no mutex or record left behind.
        var next = Start("--no-duplicate");
        Assert.False(next.HasExited);
    }

    private Process Start(params string[] args)
    {
        var process = Launch(args);
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private Process Launch(string[] args, string? crashFolder = null)
    {
        var info = new ProcessStartInfo(Next)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        foreach (var (name, value) in Folders)
            info.Environment[name] = value;
        if (crashFolder is not null)
            info.Environment["WINSHELL_TEST_CRASH"] = crashFolder;
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
