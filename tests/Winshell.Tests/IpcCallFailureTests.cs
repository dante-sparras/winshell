using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>An <c>IpcHandler</c> that throws is a failed call, not a successful one and not a crash.</summary>
public sealed class IpcCallFailureTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    [Fact]
    public void A_handler_that_throws_exits_1_and_prints_the_message()
    {
        var config = Path.Combine(_dir, "throw");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "shell.qml"), """
            import Winshell
            import Winshell.Io

            ShellRoot {
              IpcHandler {
                target: "probe"
                function boom(): string { throw new Error("x") }
              }
            }
            """);
        var process = Start(config);
        var log = LogPath(process);

        var result = Run("ipc", "--pid", process.Id.ToString(CultureInfo.InvariantCulture), "call", "probe", "boom");

        Assert.Equal(1, result.Code);
        Assert.Equal("", result.Output);
        Assert.Contains("x", result.Error, StringComparison.Ordinal);
        Thread.Sleep(400);
        if (File.Exists(log))
            Assert.DoesNotContain("background task", Read(log), StringComparison.Ordinal);
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
        info.Environment.Remove("WINSHELL_DISABLE_FILE_WATCHER");
        var process = Process.Start(info)!;
        _started.Add(process);
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        WaitFor(() => Listed().Any(i => i.GetProperty("pid").GetInt32() == process.Id), $"instance {process.Id} in winshell list");
        return process;
    }

    private static string LogPath(Process process) =>
        Listed().Single(i => i.GetProperty("pid").GetInt32() == process.Id).GetProperty("log").GetString()!;

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
        process.WaitForExit();
        return (process.ExitCode, output.Result, error.Result);
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
