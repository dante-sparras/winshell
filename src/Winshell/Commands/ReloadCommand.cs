namespace Winshell.Commands;

internal static class ReloadCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        if (InstanceCall.Select(options, InstanceSelection.Question.Running, out var code) is not { } info)
            return code;

        var project = ConfigurationProject.ForProgram(info.ConfigPath);
        if (project is not null)
            if (project.Prepare(false, ConfigurationProject.Run) is { } reason)
            {
                Console.Error.WriteLine(reason);
                await BuildNotice.Show(info, reason, project.ProjectFile);
                return 1;
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
        return answer.Problem($"Instance {info.Id} did not reload");
    }
}
