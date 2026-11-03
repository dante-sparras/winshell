namespace Winshell.Commands;

internal static class InspectCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var args = options.Positional;
        if (args is not (["tree"] or ["screenshot", _, ..] or ["click", _, ..] or ["hover", _, ..]))
        {
            HelpCommand.Print("inspect");
            return 2;
        }
        var choice = InstanceSelection.Select(options);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error);
            return choice.Code;
        }
        // The shell writes the file, so a relative path must mean what it means here.
        var rest = args.Skip(1).Select((arg, i) => args[0] == "screenshot" && i == 0 ? Path.GetFullPath(arg) : arg).ToList();
        try
        {
            await foreach (var response in IpcServer.Request(info.Pipe, new IpcRequest("inspect", args[0], Args: rest)))
            {
                if (!response.Ok)
                {
                    Console.Error.WriteLine(response.Error);
                    return 1;
                }
                Console.WriteLine(response.Value);
                return 0;
            }
            Console.Error.WriteLine($"Instance {info.Id} closed the control pipe without answering.");
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not reach instance {info.Id}: {e.Message}");
        }
        return 1;
    }
}
