using System.Diagnostics;
using System.Text;

namespace Winshell.TestSupport;

/// <summary>
/// How a process the test started ended, and what it printed. A test checks the exit code with
/// <see cref="AssertExitCode"/>, so a wrong one fails with the output that says why.
/// </summary>
internal sealed record ChildProcess(int Code, string Output, string Error)
{
    /// <summary>What ran, as a failure names it: the program's file name and its arguments.</summary>
    internal string Command { get; init; } = "The process";

    /// <summary>Runs <paramref name="info"/> to its end. Fails the test if it does not exit within <paramref name="patience"/>.</summary>
    internal static ChildProcess Run(ProcessStartInfo info, TimeSpan patience)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        var what = string.Join(' ', [Path.GetFileName(info.FileName), .. info.ArgumentList]);
        using var process = Process.Start(info)!;
        // Threads of their own: a full pipe stops the program, and a busy pool would leave the pipe full.
        var output = "";
        var error = "";
        var readOutput = new Thread(() => output = process.StandardOutput.ReadToEnd()) { IsBackground = true };
        var readError = new Thread(() => error = process.StandardError.ReadToEnd()) { IsBackground = true };
        readOutput.Start();
        readError.Start();
        if (!process.WaitForExit(patience))
        {
            process.Kill(true);
            process.WaitForExit();
            readOutput.Join(patience);
            readError.Join(patience);
            Assert.Fail($"{what} did not exit.\nOutput:\n{output}\nError:\n{error}");
        }

        Assert.True(readOutput.Join(patience) && readError.Join(patience), $"{what} left its output open");
        return new ChildProcess(process.ExitCode, output, error) { Command = what };
    }

    /// <summary>Runs <paramref name="file"/> with <paramref name="args"/> to its end, within a minute.</summary>
    internal static ChildProcess Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return Run(info, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// How <paramref name="process"/> ended, for a process the test read as it ran: <paramref name="printed"/> holds both
    /// its streams, written under its own lock, as far as they have been read.
    /// </summary>
    internal static ChildProcess Exited(string command, Process process, StringBuilder printed)
    {
        lock (printed)
        {
            return new ChildProcess(process.ExitCode, printed.ToString(), "") { Command = command };
        }
    }

    /// <summary>Fails the test, showing what the process printed, unless it exited with <paramref name="expected"/>.</summary>
    internal void AssertExitCode(int expected)
    {
        if (Code != expected)
            Assert.Fail($"{Command} exited with {Code}, not {expected}.\nOutput:\n{Output}\nError:\n{Error}");
    }
}
