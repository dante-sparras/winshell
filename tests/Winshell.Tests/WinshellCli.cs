using System.Diagnostics;
using System.Text;

namespace Winshell.Tests;

/// <summary>Runs winshell.exe to its end.</summary>
internal static class WinshellCli
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    internal static string Exe { get; } = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    internal static ProcessStartInfo StartInfo(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(Exe)
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
        return info;
    }

    /// <summary>The exit code and what it printed. Fails the test if it does not exit.</summary>
    internal static (int Code, string Output, string Error) Run(IEnumerable<string> args, params (string Name, string Value)[] environment)
    {
        var info = StartInfo(args);
        foreach (var (name, value) in environment)
            info.Environment[name] = value;
        using var process = Process.Start(info)!;
        // Threads of their own: a full pipe stops the program, and a busy pool would leave the pipe full.
        var output = "";
        var error = "";
        var readOutput = new Thread(() => output = process.StandardOutput.ReadToEnd()) { IsBackground = true };
        var readError = new Thread(() => error = process.StandardError.ReadToEnd()) { IsBackground = true };
        readOutput.Start();
        readError.Start();
        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"winshell {string.Join(' ', args)} did not exit");
        }
        Assert.True(readOutput.Join(Patience) && readError.Join(Patience), $"winshell {string.Join(' ', args)} left its output open");
        return (process.ExitCode, output, error);
    }
}
