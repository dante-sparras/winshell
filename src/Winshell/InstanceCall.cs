namespace Winshell;

/// <summary>One request to a running instance over its control pipe, and its first answer.</summary>
internal static class InstanceCall
{
    /// <summary>
    /// The instance's first answer, or why there is none: it closed the pipe without one, or could not be reached.
    /// </summary>
    internal static async Task<Answer> AskAsync(InstanceInfo info, IpcRequest request,
        CancellationToken cancel = default)
    {
        try
        {
            await foreach (var response in IpcClient.Request(info.Pipe, request, cancel))
                return new Answer(response, null);
            return new Answer(null, $"Instance {info.Id} closed the control pipe without answering.");
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new Answer(null, CouldNotReach(info, e));
        }
    }

    internal static string CouldNotReach(InstanceInfo info, Exception e)
    {
        return $"Could not reach instance {info.Id}: {e.Message}";
    }

    /// <summary>An instance's answer, or the sentence that says why there is none.</summary>
    internal readonly record struct Answer(IpcResponse? Response, string? Failure);
}
