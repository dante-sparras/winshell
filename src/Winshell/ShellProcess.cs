using System.ComponentModel;
using System.Diagnostics;

namespace Winshell;

/// <summary>A shell <see cref="ShellSupervisor"/> runs: a configuration's program.</summary>
internal sealed class ShellProcess : IShellChild
{
    private readonly Process _process;
    private readonly ShellExit.Supervision _supervision;

    private ShellProcess(Process process)
    {
        _process = process;
        _supervision = new ShellExit.Supervision(process.Id);
        Exited = ExitAsync();
    }

    public Task<int> Exited { get; }

    public ExitIntent Intent => _supervision.Intent;

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
    /// Asks the shell to quit and ends it two seconds later if it has not. Closing the console or signing out leaves this
    /// process about five seconds for all of it.
    /// </summary>
    public async Task StopAsync()
    {
        var project = ConfigurationProject.ForProgram(_process.StartInfo.FileName);
        // Two runs of the same build share that program path, and the newest one is listed first. The child this
        // supervisor started is the one with its process id; asking the other to quit leaves this one running.
        var info = project?.InstancesOf(ConfigurationBuild.Run,
                ShellLocations.Current.Instances.List(false).Select(row => row.Info))
            .FirstOrDefault(candidate => candidate.Pid == _process.Id);
        _ = await ShellExit.QuitAsync(_process, info, ExitIntent.None, ShellExit.Patience.Closing,
            ShellExit.Ending.Always);
    }

    public void Dispose()
    {
        _supervision.Dispose();
        _process.Dispose();
    }

    private async Task<int> ExitAsync()
    {
        await _process.WaitForExitAsync();
        return _process.ExitCode;
    }
}
