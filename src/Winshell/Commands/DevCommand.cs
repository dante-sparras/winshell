namespace Winshell.Commands;

internal static class DevCommand
{
    internal static async Task<int> RunAsync(CliOptions options)
    {
        var project = ConfigurationSelection.FindProject(options, out var error);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        if (project is null)
        {
            Console.Error.WriteLine("winshell dev needs a configuration project. Run dotnet new winshell to make one.");
            return 1;
        }
        if (project.Downloaded() is { } file)
        {
            Console.Error.WriteLine($"{file}: {ZoneIdentifier.Blocked(file)}");
            return 1;
        }
        return await DevSession.Run(project);
    }
}
