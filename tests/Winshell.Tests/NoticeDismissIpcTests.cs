using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell ipc call notice dismiss</c> closes whichever notice is showing, and it takes no arguments.
/// </summary>
[Collection(DefaultShellCollection.Name)]
[Trait("Category", "Slow")]
public sealed class NoticeDismissIpcTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void Notice_dismiss_reaches_a_running_shell_and_takes_no_arguments()
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var process = Start(config);

        var pid = process.Id.ToString(CultureInfo.InvariantCulture);
        var dismissed = Run("ipc", "--pid", pid, "call", "notice", "dismiss");
        Assert.Equal("", dismissed.Error);
        Assert.Equal("", dismissed.Output.Trim());
        Assert.Equal(0, dismissed.Code);
        Assert.False(process.HasExited);

        var extra = Run("ipc", "--pid", pid, "call", "notice", "dismiss", "now");
        Assert.Equal(1, extra.Code);
        Assert.Contains("Function notice.dismiss takes 0 arguments, 1 given.", extra.Error, StringComparison.Ordinal);
        Assert.False(process.HasExited);

        var other = Run("ipc", "--pid", pid, "call", "notice", "open");
        Assert.Equal(1, other.Code);
        Assert.Contains("Target not found: notice.", other.Error, StringComparison.Ordinal);
        Assert.False(process.HasExited);
    }

    private Process Start(string configDir)
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("run");
        info.Environment["WINSHELL_CONFIG_DIR"] = configDir;
        info.Environment["WINSHELL_LOCAL_DIR"] = Path.Combine(_dir, "local");
        var process = Process.Start(info)!;
        _started.Add(process);
        process.ErrorDataReceived += (_, _) => { };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        Wait.Until(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private (int Code, string Output, string Error) Run(params string[] args) =>
        WinshellCli.Run(args, ("WINSHELL_LOCAL_DIR", Path.Combine(_dir, "local")), ("WINSHELL_CONFIG_DIR", Path.Combine(_dir, "leftover")));

    public void Dispose()
    {
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                try
                {
                    Run("kill", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception)
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
