namespace Winshell.Commands;

internal static class ReloadCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var choice = InstanceSelection.Select(options);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error);
            return choice.Code;
        }
        var project = ConfigurationProject.ForProgram(info.ConfigPath);
        if (project is not null)
        {
            string? reason;
            try
            {
                reason = project.Prepare(noBuild: false, ConfigurationProject.Run);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                reason = e.Message;
            }
            if (reason is not null)
            {
                Console.Error.WriteLine(reason);
                await ShowBuildErrorAsync(info, reason, project.ProjectFile);
                return 1;
            }
        }
        // A configuration's program with a supervisor only quits, and its supervisor starts the last build and owns it.
        if (project is not null && await QuitForSupervisorAsync(info) is { } code)
            return code;
        // Any other shell hands over to a process it starts itself, which its supervisor must not start again beside, even
        // after a crash on the way out.
        ShellProcess.TellSupervisor(info.Pid, ShellProcess.Asked.Stop, asked: true);
        // A handover starts the program the build left, which is not always the one this instance runs.
        var request = new IpcRequest("reload", Args: project is null ? null : [project.Program]);
        InstanceCall.Answer answer;
        try
        {
            answer = await InstanceCall.AskAsync(info, request);
        }
        catch
        {
            ShellProcess.TellSupervisor(info.Pid, ShellProcess.Asked.Stop, asked: false);
            throw;
        }
        if (answer.Response is not { } response)
        {
            ShellProcess.TellSupervisor(info.Pid, ShellProcess.Asked.Stop, asked: false);
            Console.Error.WriteLine(answer.Failure);
            return 1;
        }
        if (!response.Ok)
        {
            ShellProcess.TellSupervisor(info.Pid, ShellProcess.Asked.Stop, asked: false);
            Console.Error.WriteLine($"Instance {info.Id} did not reload: {response.Error}");
            return 1;
        }
        Console.WriteLine($"Reloaded {info.Id}");
        return 0;
    }

    /// <summary>
    /// Asks a shell to quit for its supervisor, which starts the last build, and returns the exit code. Null when the shell
    /// has no supervisor.
    /// </summary>
    internal static async Task<int?> QuitForSupervisorAsync(InstanceInfo info)
    {
        if (!ShellProcess.TellSupervisor(info.Pid, ShellProcess.Asked.Reload, asked: true))
            return null;
        var gone = false;
        try
        {
            var answer = await InstanceCall.AskAsync(info, new IpcRequest("kill"));
            // The quit is only posted, and a shell can exit before its answer is written. One that accepted the quit and
            // hangs on its way out is ended, as a reload asked for the new one.
            var accepted = answer.Response is { Ok: true };
            gone = KillCommand.WaitForShellExit(info, TimeSpan.FromSeconds(5)) || (accepted && KillCommand.EndProcess(info));
            if (!gone)
            {
                Console.Error.WriteLine(answer.Response is { Ok: false } refused
                    ? $"Instance {info.Id} did not reload: {refused.Error}"
                    : answer.Failure ?? $"Could not end instance {info.Id}.");
                return 1;
            }
        }
        finally
        {
            // Its supervisor reads the request when the shell exits, so only a shell that still runs has it taken back.
            if (!gone && !KillCommand.WaitForShellExit(info, TimeSpan.Zero))
                ShellProcess.TellSupervisor(info.Pid, ShellProcess.Asked.Reload, asked: false);
        }
        Console.WriteLine($"Reloaded {info.Id}");
        return 0;
    }

    /// <summary>Asks the instance to show the build error. The instance keeps running either way.</summary>
    private static async Task ShowBuildErrorAsync(InstanceInfo info, string error, string projectFile)
    {
        try
        {
            await foreach (var response in IpcClient.Request(info.Pipe, new IpcRequest("build-error", Args: [error, projectFile])))
            {
                if (!response.Ok)
                    Console.Error.WriteLine($"Instance {info.Id} could not show the notice: {response.Error}");
                return;
            }
            Console.Error.WriteLine($"Instance {info.Id} could not show the notice.");
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Instance {info.Id} could not show the notice: {e.Message}");
        }
    }
}
