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
        var beside = project?.RebuiltBeside;
        var answer = await InstanceCall.AskAsync(info, new IpcRequest("reload", Args: beside is null ? null : [beside]));
        if (answer.Response is not { } response)
        {
            Console.Error.WriteLine(answer.Failure);
            return 1;
        }
        if (!response.Ok)
        {
            Console.Error.WriteLine($"Instance {info.Id} did not reload: {response.Error}");
            return 1;
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
