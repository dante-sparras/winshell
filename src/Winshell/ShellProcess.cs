using System.ComponentModel;
using System.Diagnostics;

namespace Winshell;

/// <summary>A shell <see cref="ShellSupervisor"/> runs: a configuration's program, or <c>winshell.exe</c> running the default shell.</summary>
internal sealed class ShellProcess : IShellChild
{
    private readonly Process _process;
    private readonly EventWaitHandle _stopAsked;

    private ShellProcess(Process process)
    {
        _process = process;
        _stopAsked = new EventWaitHandle(false, EventResetMode.ManualReset, StopAskedEventName(process.Id));
        Exited = ExitAsync();
    }

    public Task<int> Exited { get; }

    public bool StopAsked => _stopAsked.WaitOne(0);

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
    /// Tells the supervisor of the shell with this process id, when it has one, that a stop was asked for, so a crash on
    /// the way out is not started again. False takes that back when the shell did not stop after all.
    /// </summary>
    internal static void TellSupervisor(int pid, bool stopAsked)
    {
        if (!EventWaitHandle.TryOpenExisting(StopAskedEventName(pid), out var asked))
            return;
        using (asked)
        {
            if (stopAsked)
                asked.Set();
            else
                asked.Reset();
        }
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
        _process.Dispose();
    }

    private static string StopAskedEventName(int pid) => $@"Local\winshell-stop-asked-{pid}";

    private async Task<int> ExitAsync()
    {
        await _process.WaitForExitAsync();
        return _process.ExitCode;
    }
}
