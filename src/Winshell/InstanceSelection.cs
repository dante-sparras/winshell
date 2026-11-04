namespace Winshell;

internal static class InstanceSelection
{
    internal const string NoneRunning = "No running instances. Start one with winshell run.";

    internal static InstanceChoice Select(CliOptions options, bool includeDead = false)
    {
        var instances = Instances.List(includeDead).Select(i => i.Info).ToList();
        if (options.Pid is { } pid)
        {
            var match = instances.FirstOrDefault(i => i.Pid == pid);
            return match is null ? InstanceChoice.Miss($"No instance with process id {pid}") : InstanceChoice.Found(match);
        }
        if (options.Id is { } id)
        {
            var matches = instances.Where(i => i.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count switch
            {
                1 => InstanceChoice.Found(matches[0]),
                0 => InstanceChoice.Miss($"No instance with id {id}"),
                _ => InstanceChoice.Miss($"Several instances start with {id}"),
            };
        }
        if (options.Path is not null || options.Config is not null)
        {
            var project = ConfigurationSelection.FindProject(options, out var error);
            if (error is not null)
                return InstanceChoice.Miss(error);
            var match = project is null
                ? instances.FirstOrDefault(IsDefaultShell)
                : instances.FirstOrDefault(i => string.Equals(i.ConfigPath, project.Program, StringComparison.OrdinalIgnoreCase));
            var label = project?.Program ?? "the default shell";
            return match is null ? InstanceChoice.Miss($"No running instance of {label}") : InstanceChoice.Found(match);
        }
        return ChooseUnselected(instances);
    }

    /// <summary>
    /// The instance a command with no selector acts on. One instance is that one. Several is a command-line
    /// error: kill, log and ipc must not guess.
    /// </summary>
    internal static InstanceChoice ChooseUnselected(IReadOnlyList<InstanceInfo> instances)
    {
        if (instances.Count > 1)
        {
            var listed = string.Join(", ", instances.Select(i => $"{i.Id} ({i.ConfigPath})"));
            return InstanceChoice.Wrong($"Several instances are running; pick one with -i: {listed}");
        }
        return instances.Count == 1 ? InstanceChoice.Found(instances[0]) : InstanceChoice.Miss(NoneRunning);
    }

    /// <summary><c>winshell.exe</c> and <c>winshellw.exe</c> are the default shell. A project built into <c>bin\shell</c> is not.</summary>
    private static bool IsDefaultShell(InstanceInfo info)
    {
        var name = System.IO.Path.GetFileName(info.ConfigPath);
        return (name.Equals("winshell.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase))
            && ConfigurationProject.ForProgram(info.ConfigPath) is null;
    }

    internal readonly record struct InstanceChoice(InstanceInfo? Info, string? Error, int Code)
    {
        public static InstanceChoice Found(InstanceInfo info) => new(info, null, 0);

        public static InstanceChoice Miss(string? error) => new(null, error, 1);

        public static InstanceChoice Wrong(string error) => new(null, error, 2);
    }
}
