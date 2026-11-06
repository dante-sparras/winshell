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
                reason = project.Prepare(false, ConfigurationProject.Run);
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

        if (ShellExit.IsSupervised(info.Pid))
        {
            // Its supervisor starts the last build and owns it, so the shell only quits. Without a project there is none.
            if (project is null)
            {
                Console.Error.WriteLine(
                    $"Could not find the project that builds instance {info.Id}, so it was not reloaded.");
                return 1;
            }

            // One that accepted the quit and hangs on its way out is ended, as a reload asked for the new one.
            var quit = await ShellExit.QuitAsync(info, ExitIntent.Reload, ShellExit.Patience.Interactive,
                ShellExit.Ending.IfAccepted);
            if (!quit.Exited)
            {
                Console.Error.WriteLine(Failure(info, quit.Answer) ?? $"Could not end instance {info.Id}.");
                return 1;
            }

            Console.WriteLine($"Reloaded {info.Id}");
            return 0;
        }

        // Any other shell hands over to a process it starts itself. A handover starts the program the build left, which is
        // not always the one this instance runs.
        var answer = await InstanceCall.AskAsync(info, IpcRequest.ForReload(project?.Program));
        if (Failure(info, answer) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 1;
        }

        Console.WriteLine($"Reloaded {info.Id}");
        return 0;
    }

    private static string? Failure(InstanceInfo info, InstanceCall.Answer answer)
    {
        return answer.Response is { Ok: false } refused
            ? $"Instance {info.Id} did not reload: {refused.Error}"
            : answer.Failure;
    }

    /// <summary>Asks the instance to show the build error. The instance keeps running either way.</summary>
    private static async Task ShowBuildErrorAsync(InstanceInfo info, string error, string projectFile)
    {
        try
        {
            await foreach (var response in IpcClient.Request(info.Pipe, IpcRequest.ForBuild(error, projectFile)))
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
