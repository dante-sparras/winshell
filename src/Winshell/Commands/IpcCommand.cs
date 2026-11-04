namespace Winshell.Commands;

internal static class IpcCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var args = options.Positional;
        if (args.Length == 0)
        {
            HelpCommand.Print("ipc");
            return 2;
        }
        IpcRequest request = args[0] switch
        {
            "show" => new IpcRequest("show", Json: options.Json),
            "call" when args.Length >= 3 => new IpcRequest("call", args[1], args[2], args.Skip(3).ToList()),
            "prop" when args.Length >= 4 && args[1] == "get" => new IpcRequest("prop", args[2], args[3]),
            "wait" when args.Length >= 3 => new IpcRequest("wait", args[1], args[2]),
            "listen" when args.Length >= 3 => new IpcRequest("listen", args[1], args[2]),
            _ => new IpcRequest(""),
        };
        if (request.Op.Length == 0)
        {
            Console.Error.WriteLine("Usage: winshell ipc call <target> <function> [args...] | prop get <target> <property> | wait|listen <target> <signal> | show");
            return 2;
        }
        var choice = InstanceSelection.Select(options);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error);
            return choice.Code;
        }
        if (request.Op != "listen")
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
        try
        {
            await foreach (var response in IpcClient.Request(info.Pipe, request))
            {
                if (Print(response) != 0)
                    return 1;
            }
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(InstanceCall.CouldNotReach(info, e));
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
