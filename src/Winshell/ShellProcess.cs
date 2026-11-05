using System.ComponentModel;
using System.Diagnostics;

namespace Winshell;

/// <summary>A shell <see cref="ShellSupervisor"/> runs: a configuration's program.</summary>
internal sealed class ShellProcess : IShellChild
{
    private readonly Process _process;
    private readonly EventWaitHandle _stopAsked;
    private readonly EventWaitHandle _reloadAsked;

    internal ShellProcess(Process process)
    {
        _process = process;
        _stopAsked = new EventWaitHandle(false, EventResetMode.ManualReset, AskedEventName(Asked.Stop, process.Id));
        _reloadAsked = new EventWaitHandle(false, EventResetMode.ManualReset, AskedEventName(Asked.Reload, process.Id));
        Exited = ExitAsync();
    }

    /// <summary>What <c>winshell kill</c> or <c>winshell reload</c> asked of a shell, for its supervisor to read when it exits.</summary>
    internal enum Asked
    {
        Stop,
        Reload,
    }

    public Task<int> Exited { get; }

    public bool StopAsked => _stopAsked.WaitOne(0);

    public bool ReloadAsked => _reloadAsked.WaitOne(0);

    /// <summary>Starts the shell, or says why it did not start and returns null. A dialog can offer <paramref name="folder"/>.</summary>
    public static ShellProcess? Start(ProcessStartInfo info, string? folder)
    {
        try
        {
            // UseShellExecute=false starts a process, so Start cannot return null for an opened document.
            return new ShellProcess(Process.Start(info)!);
        }
        catch (Win32Exception e)
        {
            StartupError.Report($"Could not start \"{info.FileName}\": {e.Message}", folder);
            return null;
        }
    }

    /// <summary>
    /// Tells the supervisor of the shell with this process id, when it has one, what was asked of the shell before it is
    /// asked to quit: a stop, so a crash on the way out is not started again, or a reload, so the shell starts again at
    /// once. False for <paramref name="asked"/> takes that back when the shell did not quit after all. Returns whether the
    /// shell has a supervisor.
    /// </summary>
    internal static bool TellSupervisor(int pid, Asked what, bool asked)
    {
        if (!EventWaitHandle.TryOpenExisting(AskedEventName(what, pid), out var handle))
            return false;
        using (handle)
        {
            if (asked)
                handle.Set();
            else
                handle.Reset();
        }
        return true;
    }

    /// <summary>
    /// Asks the shell to quit and ends it two seconds later if it has not. Closing the console or signing out leaves this
    /// process about five seconds for all of it.
    /// </summary>
    public async Task StopAsync()
    {
        if (Instances.List(false).Select(i => i.Info).FirstOrDefault(i => i.Pid == _process.Id) is { } info)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                _ = await InstanceCall.AskAsync(info, new IpcRequest("kill"), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                // The shell did not answer in time. It is ended below.
            }
        }
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await _process.WaitForExitAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                // Not the tree: the apps the user opened from this shell are its children.
                _process.Kill();
            }
            catch (Win32Exception)
            {
                // It is exiting already.
            }
        }
    }

    public void Dispose()
    {
        _stopAsked.Dispose();
        _reloadAsked.Dispose();
        _process.Dispose();
    }

    private static string AskedEventName(Asked what, int pid) =>
        what == Asked.Stop ? $@"Local\winshell-stop-asked-{pid}" : $@"Local\winshell-reload-asked-{pid}";

    private async Task<int> ExitAsync()
    {
        await _process.WaitForExitAsync();
        return _process.ExitCode;
    }
}
