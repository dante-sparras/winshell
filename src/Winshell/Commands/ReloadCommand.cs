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
        try
        {
            await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest(
                "reload",
                Args: beside is null ? null : [beside])))
            {
                if (response.Ok)
                {
                    Console.WriteLine($"Reloaded {info.Id}");
                    return 0;
                }
                Console.Error.WriteLine($"Instance {info.Id} did not reload: {response.Error}");
                return 1;
            }
            Console.Error.WriteLine($"Instance {info.Id} closed the control pipe without answering.");
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not reach instance {info.Id}: {e.Message}");
        }
        return 1;
    }

    /// <summary>Asks the instance to show the build error. The instance keeps running either way.</summary>
    private static async Task ShowBuildErrorAsync(InstanceInfo info, string error, string projectFile)
    {
        try
        {
            await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest("build-error", Args: [error, projectFile])))
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
