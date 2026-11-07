namespace Winshell.Commands;

internal static class InspectCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        // CliParser has checked that args is one of tree, screenshot, click and hover, with the words it needs.
        var args = options.Positional;
        if (InstanceCall.Select(options, InstanceSelection.Question.Running, out var code) is not { } info)
            return code;

        // The shell writes the file, so a relative path must mean what it means here.
        var rest = args.Skip(1).Select((arg, i) => args[0] == "screenshot" && i == 0 ? Path.GetFullPath(arg) : arg)
            .ToList();
        return await InstanceCall.PrintAsync(info, IpcRequest.ForInspect(args[0], rest));
    }
}
