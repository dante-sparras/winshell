using System.Diagnostics;

namespace Winshell.Commands;

internal static class IpcCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        // CliParser has checked that args is one of these commands, with the words it needs.
        var args = options.Positional;
        var request = args[0] switch
        {
            IpcOps.Show => IpcRequest.ForShow(options.Json),
            IpcOps.Call => IpcRequest.ForCall(args[1], args[2], args.Skip(3).ToList()),
            IpcOps.Prop => IpcRequest.ForProp(args[2], args[3]),
            IpcOps.Wait => IpcRequest.ForWait(args[1], args[2]),
            IpcOps.Listen => IpcRequest.ForListen(args[1], args[2]),
            _ => throw new UnreachableException($"No ipc command \"{args[0]}\".")
        };
        var choice = InstanceSelection.Select(options, InstanceSelection.Question.Running, Instances.RunDir);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error);
            return choice.Code;
        }

        if (request.Op != IpcOps.Listen)
        {
            var answer = await InstanceCall.AskAsync(info, request);
            if (answer.Response is not { } response)
            {
                Console.Error.WriteLine(answer.Failure);
                return 1;
            }

            return Print(response);
        }

        // A listen prints each signal, and ends without a value when the shell quits.
        await foreach (var reply in IpcClient.Request(info.Pipe, request))
        {
            if (reply.Failure is { } failure)
            {
                Console.Error.WriteLine(failure.Sentence(info.Id));
                return 1;
            }

            if (reply.Response is { } response && Print(response) != 0)
                return 1;
        }

        return 0;
    }

    private static int Print(IpcResponse response)
    {
        if (!response.Ok)
        {
            Console.Error.WriteLine(response.Error);
            return 1;
        }

        if (!string.IsNullOrEmpty(response.Value))
            Console.WriteLine(response.Value.TrimEnd('\n'));
        return 0;
    }
}
