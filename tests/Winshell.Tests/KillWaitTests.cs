using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary><c>winshell kill</c> waits until the instance's process has exited.</summary>
public sealed class KillWaitTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void Kill_returns_after_the_process_has_exited()
    {
        var config = Path.Combine(_dir, "shell");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "shell.qml"), """
            import Winshell
            ShellRoot {}
            """);
        var process = Start(config);

        var result = Run("kill", "--pid", process.Id.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(0, result.Code);
        Assert.Equal("", result.Error);
        Assert.StartsWith("Killed ", result.Output, StringComparison.Ordinal);
        Assert.True(process.HasExited);
    }

    [Fact]
    public void Force_on_a_missing_pid_is_still_a_miss()
    {
        var result = Run("kill", "--force", "--pid", "0");

        Assert.Equal(1, result.Code);
        Assert.Equal("", result.Output);
        Assert.Equal("No instance with process id 0" + Environment.NewLine, result.Error);
    }

    private Process Start(string configDir)
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(configDir);
        info.Environment["WINSHELL_CONFIG_DIR"] = _dir;
        var process = Process.Start(info)!;
        _started.Add(process);
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        WaitFor(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private static List<JsonElement> Listed()
    {
        var result = Run("list", "--json");
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
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
        foreach (var process in _started)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }
                process.WaitForExit(5000);
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
