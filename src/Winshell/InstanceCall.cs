namespace Winshell;

/// <summary>One request to a running instance over its control pipe, and its first answer.</summary>
internal static class InstanceCall
{
    /// <summary>
    /// The instance's first answer, or why there is none. The client names a rejection, a timeout and a pipe that
    /// could not be reached. A pipe that closes with no line is named here.
    /// </summary>
    internal static async Task<Answer> AskAsync(InstanceInfo info, IpcRequest request,
        CancellationToken cancel = default)
    {
        await foreach (var reply in IpcClient.Request(info.Pipe, request, cancel))
            return reply.Failure is { } failure
                ? new Answer(null, failure.Sentence(info.Id))
                : new Answer(reply.Response, null);

        return new Answer(null, $"Instance {info.Id} closed the control pipe without answering.");
    }

    /// <summary>An instance's answer, or the sentence that says why there is none.</summary>
    internal readonly record struct Answer(IpcResponse? Response, string? Failure);
}
