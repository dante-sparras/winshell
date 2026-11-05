using System.ComponentModel;
using System.Diagnostics;

namespace Winshell;

/// <summary>
/// What <c>winshell kill</c> or <c>winshell reload</c> asked of a shell under <c>winshell run</c>, for its supervisor to
/// read when the shell exits.
/// </summary>
internal enum ExitIntent
{
    /// <summary>Nothing was asked. An exit other than 0 or 1 is a crash.</summary>
    None,

    /// <summary>The supervisor starts the last build at once, however the shell exits.</summary>
    Reload,

    /// <summary>Supervision ends, even after a crash on the way out. It outranks a reload, whichever was asked first.</summary>
    Stop,
}

/// <summary>
/// Ending an instance: asking it to quit, waiting for it to exit, and ending its process when it does not, with the intent
/// its supervisor reads when it exits. The CLI and the supervisor are separate processes, so the intent is a named event
/// for each of Stop and Reload, which the supervisor makes for the shell's process id.
/// </summary>
internal static class ShellExit
{
    /// <summary>How long ending an instance waits for its answer to the quit, and then for it to exit.</summary>
    internal sealed record Patience(TimeSpan Ask, TimeSpan Wait)
    {
        /// <summary><c>winshell kill</c>, <c>winshell reload</c> and <c>winshell dev</c>, while the user waits.</summary>
        public static readonly Patience Interactive = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        /// <summary>Closing the console or signing out, which leaves this process about five seconds for all of it.</summary>
        public static readonly Patience Closing = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    /// <summary>Whether a shell that has not exited after the wait is ended.</summary>
    internal enum Ending
    {
        /// <summary>It is left running.</summary>
        Never,

        /// <summary>
        /// Once it has accepted the quit, or has it and did not answer in time. One the quit did not reach is left running.
        /// </summary>
        IfAccepted,

        /// <summary>Whatever it answered.</summary>
        Always,
    }

    /// <summary>Whether the instance has exited, and its answer to the quit. A shell that did not answer in time has none.</summary>
    internal readonly record struct Outcome(bool Exited, InstanceCall.Answer Answer);

    /// <summary>The supervisor's side of one shell, from its start until the supervisor has read its intent.</summary>
    internal sealed class Supervision(int pid) : IDisposable
    {
        private readonly EventWaitHandle _stop = new(false, EventResetMode.ManualReset, EventName(ExitIntent.Stop, pid));
        private readonly EventWaitHandle _reload = new(false, EventResetMode.ManualReset, EventName(ExitIntent.Reload, pid));

        /// <summary>What was asked of the shell and not taken back.</summary>
        public ExitIntent Intent => _stop.WaitOne(0) ? ExitIntent.Stop : _reload.WaitOne(0) ? ExitIntent.Reload : ExitIntent.None;

        public void Dispose()
        {
            _stop.Dispose();
            _reload.Dispose();
        }
    }

    /// <summary>Whether the shell with this process id runs under <c>winshell run</c>.</summary>
    public static bool IsSupervised(int pid)
    {
        if (!EventWaitHandle.TryOpenExisting(EventName(ExitIntent.Stop, pid), out var handle))
            return false;
        handle.Dispose();
        return true;
    }

    /// <summary>
    /// Records <paramref name="intent"/> for the instance's supervisor, when it has one, asks the instance to quit, and waits
    /// for it to exit. One that has not exited by then is ended as <paramref name="ending"/> says.
    /// </summary>
    public static async Task<Outcome> QuitAsync(InstanceInfo info, ExitIntent intent, Patience patience, Ending ending)
    {
        using var process = Open(info);
        return process is null ? new(true, default) : await QuitAsync(process, info, intent, patience, ending);
    }

    /// <summary>
    /// <see cref="QuitAsync(InstanceInfo, ExitIntent, Patience, Ending)"/> for a shell this process started. Without
    /// <paramref name="info"/>, which a shell that has not started or is on its way out has none of, it is not asked.
    /// </summary>
    public static async Task<Outcome> QuitAsync(Process process, InstanceInfo? info, ExitIntent intent, Patience patience, Ending ending)
    {
        if (intent != ExitIntent.None)
            Tell(process.Id, intent, asked: true);
        var exited = false;
        try
        {
            InstanceCall.Answer answer = default;
            var late = false;
            if (info is not null)
            {
                using var deadline = new CancellationTokenSource(patience.Ask);
                try
                {
                    answer = await InstanceCall.AskAsync(info, new IpcRequest("kill"), deadline.Token);
                }
                catch (OperationCanceledException)
                {
                    // The shell did not answer in time. An interactive ask outlasts the connection's three seconds, so that
                    // shell has the quit, and its thread is busy.
                    late = true;
                }
            }
            // The quit is only posted, and a shell can exit before its answer is written.
            var end = ending == Ending.Always || (ending == Ending.IfAccepted && (late || answer.Response is { Ok: true }));
            exited = await WaitAsync(process, patience.Wait) || (end && await EndAsync(process, info));
            return new(exited, answer);
        }
        finally
        {
            // Its supervisor reads the intent when the shell exits, so only a shell that still runs has it taken back.
            if (intent != ExitIntent.None && !exited && !process.HasExited)
                Tell(process.Id, intent, asked: false);
        }
    }

    /// <summary>The instance's process, or null once it has exited or a different process owns its id.</summary>
    private static Process? Open(InstanceInfo info)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(info.Pid);
            if (!process.HasExited && Instances.IsThisInstance(info, process))
                return process;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // It has exited.
        }
        process?.Dispose();
        return null;
    }

    private static async Task<bool> WaitAsync(Process process, TimeSpan timeout)
    {
        using var patience = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(patience.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // This user can wait on the process but not open it for everything, as with an elevated shell.
            return process.WaitForExit(timeout);
        }
    }

    private static async Task<bool> EndAsync(Process process, InstanceInfo? info)
    {
        try
        {
            // The id was free to take while the instance was asked. A different process that owns it now is not ended.
            process.Refresh();
            if (info is not null && !Instances.IsThisInstance(info, process))
                return true;
            // Not the tree: the apps the user opened from this shell are its children, and they are not hung.
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // It is exiting already, or this user cannot end it.
        }
        return await WaitAsync(process, TimeSpan.FromSeconds(1));
    }

    private static void Tell(int pid, ExitIntent intent, bool asked)
    {
        if (!EventWaitHandle.TryOpenExisting(EventName(intent, pid), out var handle))
            return;
        using (handle)
        {
            if (asked)
                handle.Set();
            else
                handle.Reset();
        }
    }

    private static string EventName(ExitIntent intent, int pid) =>
        intent == ExitIntent.Stop ? $@"Local\winshell-stop-asked-{pid}" : $@"Local\winshell-reload-asked-{pid}";
}
