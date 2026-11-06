namespace Winshell;

/// <summary>The build-error notice a reload or a dev session asks a running instance to show.</summary>
internal static class BuildNotice
{
    /// <summary>Sends the notice and prints the one sentence when the instance does not show it.</summary>
    internal static async Task Show(InstanceInfo info, string error, string projectFile)
    {
        await foreach (var reply in IpcClient.Request(info.Pipe, IpcRequest.ForBuild(error, projectFile)))
        {
            if (reply.Failure is { } failure)
                Console.Error.WriteLine($"Instance {info.Id} could not show the notice: {failure.Reason}");
            else if (reply.Response is { Ok: false })
                Console.Error.WriteLine($"Instance {info.Id} could not show the notice: {reply.Response.Error}");
            return;
        }

        Console.Error.WriteLine($"Instance {info.Id} could not show the notice.");
    }
}
