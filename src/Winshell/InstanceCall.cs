namespace Winshell;

/// <summary>A command's request to a running instance over its control pipe: which instance, its answer, and what is printed.</summary>
internal static class InstanceCall
{
    /// <summary>
    /// The instance <paramref name="options"/> select. Null once why not is printed, and <paramref name="code"/> is then
    /// the command's exit code.
    /// </summary>
    internal static InstanceInfo? Select(CliOptions options, InstanceSelection.Question question, out int code)
    {
        var choice = InstanceSelection.Select(options, question, Instances.RunDir);
        code = choice.Code;
        if (choice.Info is null)
            Console.Error.WriteLine(choice.Error);
        else if (choice.Notice is not null)
            Console.Error.WriteLine(choice.Notice);
        return choice.Info;
    }

    /// <summary>
    /// The instance's first answer, or why there is none. The client names a rejection, a timeout and a pipe that
    /// could not be reached. A pipe that closes with no line is named here.
    /// </summary>
    internal static async Task<Answer> AskAsync(InstanceInfo info, IpcRequest request,
        CancellationToken cancel = default)
    {
        await foreach (var reply in IpcClient.Request(info.Pipe, request, cancel))
            return Answer.Of(reply, info.Id);

        return new Answer(null, $"Instance {info.Id} closed the control pipe without answering.");
    }

    /// <summary>Asks the instance and prints its answer. Returns the exit code.</summary>
    internal static async Task<int> PrintAsync(InstanceInfo info, IpcRequest request)
    {
        return Print(await AskAsync(info, request));
    }

    /// <summary>Prints an answer: its value on standard output, or else why it failed on standard error. Returns the exit code.</summary>
    internal static int Print(Answer answer)
    {
        if (answer.Response is not { Ok: true } response)
        {
            Console.Error.WriteLine(answer.Problem(null));
            return 1;
        }

        if (!string.IsNullOrEmpty(response.Value))
            Console.WriteLine(response.Value.TrimEnd('\n'));
        return 0;
    }

    /// <summary>An instance's answer, or the sentence that says why there is none.</summary>
    internal readonly record struct Answer(IpcResponse? Response, string? Failure)
    {
        public static Answer Of(IpcReply reply, string instanceId)
        {
            return reply.Failure is { } failure
                ? new Answer(null, failure.Sentence(instanceId))
                : new Answer(reply.Response, null);
        }

        /// <summary>
        /// The instance's refusal, after <paramref name="refused"/> when the command names its failure, or else why there
        /// is no answer. Null when it agreed, or when there is neither, as for a shell that did not answer in time.
        /// </summary>
        public string? Problem(string? refused)
        {
            if (Response is { Ok: false } response)
                return refused is null ? response.Error : $"{refused}: {response.Error}";
            return Failure;
        }
    }
}
