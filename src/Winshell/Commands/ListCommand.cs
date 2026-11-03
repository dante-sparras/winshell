namespace Winshell.Commands;

internal static class ListCommand
{
    internal static int Run(CliOptions options)
    {
        var instances = Instances.List(false);
        if (options.Json)
        {
            Console.WriteLine(CliJson.Instances(instances.Select(i => i.Info)));
            return 0;
        }
        if (instances.Count == 0)
        {
            Console.WriteLine(InstanceSelection.NoneRunning);
            return 0;
        }
        foreach (var (info, _) in instances)
        {
            Console.WriteLine($"Instance {info.Id}:");
            Console.WriteLine($"  Process ID: {info.Pid}");
            Console.WriteLine($"  Shell ID: {info.ShellId}");
            Console.WriteLine($"  Config path: {info.ConfigPath}");
            Console.WriteLine($"  Launch time: {info.LaunchTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Log file: {info.Log}");
        }
        return 0;
    }
}
