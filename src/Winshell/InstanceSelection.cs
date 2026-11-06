namespace Winshell;

internal static class InstanceSelection
{
    /// <summary>What <c>list</c> and a command that needs an instance say when nothing runs.</summary>
    internal const string NoneRunning = "No running instances. Start one with winshell run.";

    /// <summary>What <c>kill</c> says when nothing runs. It wanted nothing running, so it gets no hint to start one.</summary>
    internal const string NoneToKill = "No running instances.";

    internal static InstanceChoice Select(CliOptions options, bool includeDead = false)
    {
        var instances = Instances.List(includeDead).Select(i => i.Info).ToList();
        if (options.Pid is { } pid)
        {
            var match = instances.FirstOrDefault(i => i.Pid == pid);
            return match is null
                ? InstanceChoice.Miss($"No instance with process id {pid}.")
                : InstanceChoice.Found(match);
        }

        if (options.Id is { } id)
        {
            var matches = instances.Where(i => i.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count switch
            {
                1 => InstanceChoice.Found(matches[0]),
                0 => InstanceChoice.Miss($"No instance with id {id}."),
                _ => InstanceChoice.Miss($"Several instances start with {id}.")
            };
        }

        if (options.Path is not null || options.Config is not null)
        {
            var project = ConfigurationSelection.FindProject(options, out var error);
            if (error is not null)
                return InstanceChoice.Miss(error);
            // With -c or -p a project is always found, or the error above says why not.
            var match = instances.FirstOrDefault(i => RunsProject(i, project!));
            return match is null
                ? InstanceChoice.Miss($"No running instance of {project!.Program}.")
                : InstanceChoice.Found(match);
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
            return InstanceChoice.Wrong($"Several instances are running; pick one with -i: {listed}.");
        }

        return instances.Count == 1 ? InstanceChoice.Found(instances[0]) : InstanceChoice.Miss(NoneRunning);
    }

    /// <summary>Whether <paramref name="info"/> runs <paramref name="project"/>, from <c>bin\shell</c> or from <c>bin\shell.next</c>.</summary>
    internal static bool RunsProject(InstanceInfo info, ConfigurationProject project)
    {
        return string.Equals(ConfigurationProject.ForProgram(info.ConfigPath)?.ProjectFile, project.ProjectFile,
            StringComparison.OrdinalIgnoreCase);
    }

    internal readonly record struct InstanceChoice(InstanceInfo? Info, string? Error, int Code)
    {
        public static InstanceChoice Found(InstanceInfo info)
        {
            return new InstanceChoice(info, null, 0);
        }

        public static InstanceChoice Miss(string? error)
        {
            return new InstanceChoice(null, error, 1);
        }

        public static InstanceChoice Wrong(string error)
        {
            return new InstanceChoice(null, error, 2);
        }
    }
}
