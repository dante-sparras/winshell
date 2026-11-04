namespace Winshell;

/// <summary>One request to a running instance over its control pipe, and its first answer.</summary>
internal static class InstanceCall
{
    /// <summary>
    /// The instance's first answer, or why there is none: it closed the pipe without one, or could not be reached.
    /// </summary>
    internal static async Task<InstanceAnswer> AskAsync(InstanceInfo info, IpcRequest request)
    {
        try
        {
            await foreach (var response in IpcServer.Request(info.Pipe, request))
                return new(response, null);
            return new(null, $"Instance {info.Id} closed the control pipe without answering.");
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new(null, CouldNotReach(info, e));
        }
    }

    internal static string CouldNotReach(InstanceInfo info, Exception e) => $"Could not reach instance {info.Id}: {e.Message}";
}

/// <summary>An instance's answer, or the sentence that says why there is none.</summary>
internal readonly record struct InstanceAnswer(IpcResponse? Response, string? Failure);
