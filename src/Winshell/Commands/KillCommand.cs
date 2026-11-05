using System.Diagnostics;

namespace Winshell.Commands;

internal static class KillCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var choice = InstanceSelection.Select(options);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error == InstanceSelection.NoneRunning ? InstanceSelection.NoneToKill : choice.Error);
            return choice.Code;
        }
        // Its supervisor must not start it again, even after a crash on the way out or the end --force gives it.
        ShellProcess.TellSupervisor(info.Pid, stopAsked: true);
        // Null once the instance has accepted the kill. No answer is a failure too.
        var answer = await InstanceCall.AskAsync(info, new IpcRequest("kill"));
        var failure = answer.Response is { Ok: false } refused ? $"Instance {info.Id} refused: {refused.Error}" : answer.Failure;
        // The quit is only posted. Wait so the next start does not overlap the AppBar, hotkeys and tray.
        if (!WaitForShellExit(info, TimeSpan.FromSeconds(5)) && !(options.Force && EndProcess(info)))
        {
            ShellProcess.TellSupervisor(info.Pid, stopAsked: false);
            if (failure is not null)
                Console.Error.WriteLine(failure);
            Console.Error.WriteLine(options.Force
                ? $"Could not end instance {info.Id}."
                : $"Instance {info.Id} is still running. Use --force to end it.");
            return 1;
        }
        Console.WriteLine($"Killed {info.Id}");
        return 0;
    }

    /// <summary>True when this instance's process has exited, or the process id now belongs to something else.</summary>
    private static bool WaitForShellExit(InstanceInfo info, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(info.Pid);
            if (process.HasExited || !Instances.IsThisInstance(info, process))
                return true;
            return process.WaitForExit(timeout);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static bool EndProcess(InstanceInfo info)
    {
        try
        {
            using var process = Process.GetProcessById(info.Pid);
            // A different process now owns this id. The instance we asked is already gone.
            if (process.HasExited || !Instances.IsThisInstance(info, process))
                return true;
            // Not the tree: the apps the user opened from this shell are its children, and they are not hung.
            process.Kill();
            return process.WaitForExit(5000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return e is ArgumentException;
        }
    }
}
