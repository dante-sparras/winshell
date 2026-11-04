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
        var answer = await InstanceCall.AskAsync(info, new IpcRequest("inspect", args[0], Args: rest));
        if (answer.Response is not { } response)
        {
            Console.Error.WriteLine(answer.Failure);
            return 1;
        }
        if (!response.Ok)
        {
            Console.Error.WriteLine(response.Error);
            return 1;
        }
        Console.WriteLine(response.Value);
        return 0;
    }
}
