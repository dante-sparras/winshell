namespace Winshell;

/// <summary>
/// Runs a shell in a child process and starts it again when it crashes. Exit code 0 is a stop that was asked for:
/// <c>winshell kill</c>, a reload that handed over to a new process, Ctrl+C, or a duplicate that left. 1 is a shell that
/// could not start and said why, which a new start would only repeat. Any other exit is a crash. The next start waits
/// 1, 2, 4, 8 and then 10 seconds, and the sixth crash in a row ends supervision. A shell that ran for a minute was
/// running well, so its crash counts as the first.
/// </summary>
/// <remarks>
/// After every exit, Explorer's taskbar comes back if it showed when that child started: a crashed shell cannot put it
/// back, and the next child must find it as Explorer has it, or it would keep a hidden taskbar hidden when it quits. A
/// taskbar that another shell had hidden stays hidden.
/// </remarks>
internal sealed class ShellSupervisor(Func<IShellChild?> start, Func<bool> taskbarShowing, Action restoreTaskbar, TimeProvider time)
{
    internal const int CrashesBeforeGivingUp = 6;

    internal static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);

    internal static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan RunningWell = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Runs shells until one stops as asked, cannot start, or crashes too often, and returns its exit code.
    /// <paramref name="stop"/> stops the running shell and ends supervision.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken stop)
    {
        var crashes = 0;
        while (true)
        {
            var showing = taskbarShowing();
            var started = time.GetTimestamp();
            int code;
            try
            {
                code = await RunOnceAsync(stop);
            }
            finally
            {
                if (showing)
                    restoreTaskbar();
            }
            if (code is 0 or 1 || stop.IsCancellationRequested)
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
    private async Task<int> RunOnceAsync(CancellationToken stop)
    {
        using var child = start();
        if (child is null)
            return 1;
        try
        {
            return await child.Exited.WaitAsync(stop);
        }
        catch (OperationCanceledException)
        {
            await child.StopAsync();
            return await child.Exited;
        }
    }
}
