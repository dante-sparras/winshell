namespace Winshell.Commands;

internal static class InspectCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        // CliParser has checked that args is one of tree, screenshot, click and hover, with the words it needs.
        var args = options.Positional;
        var choice = InstanceSelection.Select(options, InstanceSelection.Question.Running, Instances.RunDir);
        if (choice.Info is not { } info)
        {
            Console.Error.WriteLine(choice.Error);
            return choice.Code;
        }

        // The shell writes the file, so a relative path must mean what it means here.
        var rest = args.Skip(1).Select((arg, i) => args[0] == "screenshot" && i == 0 ? Path.GetFullPath(arg) : arg)
            .ToList();
        var answer = await InstanceCall.AskAsync(info, IpcRequest.ForInspect(args[0], rest));
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
