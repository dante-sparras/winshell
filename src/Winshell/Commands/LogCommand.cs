namespace Winshell.Commands;

internal static class LogCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var path = options.Positional.FirstOrDefault();
        if (path is null)
        {
            if (InstanceCall.Select(options, InstanceSelection.Question.Log, out var code) is not { } info)
                return code;
            path = info.Log;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No log at {path}.");
            return 1;
        }

        await using var stream = InstanceLog.OpenToRead(path);
        using var reader = new StreamReader(stream);
        var all = (await reader.ReadToEndAsync()).TrimEnd('\n').Split('\n');
        foreach (var line in options.Tail is { } n ? all.TakeLast(n) : all)
            if (line.Length > 0)
                Console.WriteLine(line.TrimEnd('\r'));
        if (options.Follow)
            await foreach (var line in InstanceLog.Follow(reader))
                Console.WriteLine(line);

        return 0;
    }
}
