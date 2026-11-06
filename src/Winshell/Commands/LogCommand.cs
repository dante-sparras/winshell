namespace Winshell.Commands;

internal static class LogCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var path = options.Positional.FirstOrDefault();
        if (path is null)
        {
            var choice = InstanceSelection.Select(options, InstanceSelection.Question.Log, Instances.RunDir);
            if (choice.Info is not { } info)
            {
                Console.Error.WriteLine(choice.Error);
                return choice.Code;
            }

            if (choice.Notice is not null)
                Console.Error.WriteLine(choice.Notice);
            path = info.Log;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No log at {path}.");
            return 1;
        }

        await using var stream =
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var all = (await reader.ReadToEndAsync()).TrimEnd('\n').Split('\n');
        foreach (var line in options.Tail is { } n ? all.TakeLast(n) : all)
            if (line.Length > 0)
                Console.WriteLine(line.TrimEnd('\r'));
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
