namespace Winshell.Commands;

internal static class LogCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        string? path = options.Positional.FirstOrDefault();
        if (path is null)
        {
            var choice = InstanceSelection.Select(options);
            if (choice.Info is not { } info)
            {
                // Several running instances is a command-line error, not a reason to show a stopped log.
                if (choice.Code == 2)
                {
                    Console.Error.WriteLine(choice.Error);
                    return 2;
                }
                // A selector can name an instance that stopped, but never gets a different one instead.
                var selected = options.Pid is not null || options.Id is not null || options.Path is not null || options.Config is not null;
                var stopped = selected ? InstanceSelection.Select(options, includeDead: true).Info : Instances.List(true).FirstOrDefault().Info;
                if (stopped is null)
                {
                    Console.Error.WriteLine(choice.Error);
                    return choice.Code;
                }
                info = stopped;
                Console.Error.WriteLine($"Showing the log of stopped instance {info.Id}.");
            }
            path = info.Log;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No log at {path}");
            return 1;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var all = (await reader.ReadToEndAsync()).TrimEnd('\n').Split('\n');
        foreach (var line in options.Tail is { } n ? all.TakeLast(n) : all)
        {
            if (line.Length > 0)
                Console.WriteLine(line.TrimEnd('\r'));
        }
        while (options.Follow)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                await Task.Delay(200);
                continue;
            }
            Console.WriteLine(line);
        }
        return 0;
    }
}
