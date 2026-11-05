namespace Winshell;

/// <summary>
/// Runs a shell in a child process and starts it again when it crashes. Exit code 0 is a stop that was asked for, such as
/// <c>winshell kill</c>, a reload that handed over to a new process, Ctrl+C, or a duplicate that left. 1 is a shell that
/// could not start and said why, which a new start would only repeat. Any other exit is a crash, unless <c>winshell kill</c>
/// had asked the shell to stop. The next start waits 1, 2, 4, 8 and then 10 seconds, and the sixth crash in a row ends
/// supervision. A shell that ran for a minute was running well, so its crash counts as the first. A shell
/// <c>winshell reload</c> asked to quit for its supervisor starts again at once, however it exits, and counts its crashes
/// from none, as the first did.
/// </summary>
/// <remarks>
/// A shell that exits 0 has put Explorer's taskbar back itself. After any other exit, the taskbar comes back if none of
/// its windows was hidden when that child started: a crashed shell cannot put it back, and the next child must find it
/// as Explorer has it, or it would keep a hidden taskbar hidden when it quits. A taskbar that another shell had hidden
/// stays hidden, and so does one a clean exit could only take from the shell that hid it since.
/// </remarks>
internal sealed class ShellSupervisor(Func<IShellChild?> start, Func<bool> taskbarHidden, Action restoreTaskbar, TimeProvider time)
{
    private const int CrashesBeforeGivingUp = 6;

    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RunningWell = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Runs shells until one stops as asked, cannot start, or crashes too often, and returns its exit code.
    /// <paramref name="stop"/> stops the running shell and ends supervision.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken stop)
    {
        var crashes = 0;
        while (true)
        {
            var hidden = taskbarHidden();
            var started = time.GetTimestamp();
            var exit = (Code: 1, Intent: ExitIntent.None);
            try
            {
                exit = await RunOnceAsync(stop);
            }
            finally
            {
                if (!hidden && exit.Code != 0)
                    restoreTaskbar();
            }
            var code = exit.Code;
            if (exit.Intent == ExitIntent.Stop || stop.IsCancellationRequested)
                return code;
            if (exit.Intent == ExitIntent.Reload)
            {
                crashes = 0;
                continue;
            }
            if (code is 0 or 1)
                return code;
            crashes = time.GetElapsedTime(started) >= RunningWell ? 1 : crashes + 1;
            if (crashes == CrashesBeforeGivingUp)
            {
                Console.Error.WriteLine($"The shell crashed {crashes} times in a row, the last with exit code {code}. It is not started again.");
                return code;
            }
            var wait = TimeSpan.FromTicks(Math.Min(FirstWait.Ticks << (crashes - 1), LongestWait.Ticks));
            Console.Error.WriteLine($"The shell exited with code {code}. Starting it again in {wait.TotalSeconds:0} s.");
            try
            {
                await Task.Delay(wait, time, stop);
            }
            catch (OperationCanceledException)
            {
                return code;
            }
        }
    }

    /// <summary>One child, to its exit. 1 when it did not start, and the start has said why.</summary>
    private async Task<(int Code, ExitIntent Intent)> RunOnceAsync(CancellationToken stop)
    {
        using var child = start();
        if (child is null)
            return (1, ExitIntent.None);
        int code;
        try
        {
            code = await child.Exited.WaitAsync(stop);
        }
        catch (OperationCanceledException)
        {
            await child.StopAsync();
            code = await child.Exited;
        }
        return (code, child.Intent);
    }
}
