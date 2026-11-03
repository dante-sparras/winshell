using System.Diagnostics;

namespace Winshell.Commands;

internal static class KillCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var choice = InstanceSelection.Select(options);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error);
            return choice.Code;
        }
        // Null once the instance has accepted the kill. No answer is a failure too.
        string? failure = $"Instance {info.Id} closed the control pipe without answering.";
        try
        {
            await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest("kill")))
            {
                failure = response.Ok ? null : $"Instance {info.Id} refused: {response.Error}";
                break;
            }
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            failure = $"Could not reach instance {info.Id}: {e.Message}";
        }
        // The quit is only posted. Wait so the next start does not overlap the AppBar, hotkeys and tray.
        if (!WaitForShellExit(info, TimeSpan.FromSeconds(5)) && !(options.Force && EndProcess(info)))
        {
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
            process.Kill(entireProcessTree: true);
            return process.WaitForExit(5000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return e is ArgumentException;
        }
    }
}
