using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary><c>winshell reload</c> reloads a running instance by hand, and says whether the configuration loaded.</summary>
public sealed class ReloadCommandTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    private static string Shell(string version) => $$"""
        import Winshell
        import Winshell.Io

        ShellRoot {
          IpcHandler {
            target: "probe"
            function version(): string { return "{{version}}" }
          }
        }
        """;

    private void Write(string text) => File.WriteAllText(Path.Combine(_dir, "shell.qml"), text);

    private string Version(string pid) => Run("ipc", "--pid", pid, "call", "probe", "version").Output.TrimEnd();

    // An instance is listed before its first load ends, and the file must not change until that load has read it.
    private void Loaded(string pid)
    {
        Wait.Until(() => Version(pid) == "one", "the first load");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reload_loads_a_change_the_watcher_did_not_see(bool hard)
    {
        Directory.CreateDirectory(_dir);
        Write(Shell("one"));
        var pid = Start().Id.ToString(CultureInfo.InvariantCulture);
        Loaded(pid);
        Write(Shell("two"));

        var result = hard ? Run("reload", "--hard", "--pid", pid) : Run("reload", "--pid", pid);

        Assert.Equal(0, result.Code);
        Assert.Equal("", result.Error);
        Assert.StartsWith("Reloaded ", result.Output, StringComparison.Ordinal);
        Assert.Equal("two", Version(pid));
    }

    [Fact]
    public void A_configuration_that_does_not_load_exits_1_and_the_old_one_keeps_running()
    {
        Directory.CreateDirectory(_dir);
        Write(Shell("one"));
        var pid = Start().Id.ToString(CultureInfo.InvariantCulture);
        Loaded(pid);
        Write("ShellRoot {");

        var result = Run("reload", "--pid", pid);

        Assert.Equal(1, result.Code);
        Assert.Equal("", result.Output);
        Assert.Contains("did not reload", result.Error, StringComparison.Ordinal);
        Assert.Equal("one", Version(pid));
    }

    [Fact]
    public void Reload_with_nothing_running_for_a_pid_is_a_miss()
    {
        var result = Run("reload", "--pid", "0");

        Assert.Equal(1, result.Code);
        Assert.Equal("", result.Output);
        Assert.Equal("No instance with process id 0" + Environment.NewLine, result.Error);
    }

    private Process Start()
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(_dir);
        // The test's edit must not reload the shell by itself.
        info.Environment["WINSHELL_DISABLE_FILE_WATCHER"] = "1";
        var process = Process.Start(info)!;
        _started.Add(process);
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private static List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args);

    public void Dispose()
    {
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                Run("kill", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));
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
