namespace Winshell;

/// <summary>
/// Which instance a command means. The command says whether a stopped instance can answer, and how "none" is
/// phrased. The result is that instance, or a finished failure with its sentence and exit code.
/// </summary>
internal static class InstanceSelection
{
    /// <summary>What <c>list</c> and a command that needs an instance say when nothing runs.</summary>
    internal const string NoneRunning = "No running instances. Start one with winshell run.";

    /// <summary>What <c>kill</c> says when nothing runs. It wanted nothing running, so it gets no hint to start one.</summary>
    internal const string NoneToKill = "No running instances.";

    /// <summary>What the command accepts: a stopped instance as a fallback, and the sentence when nothing qualifies.</summary>
    internal readonly record struct Question(string None, bool StoppedFallback)
    {
        /// <summary>A running instance. Nothing running names the start command.</summary>
        internal static Question Running => new(NoneRunning, false);

        /// <summary>A running instance. Nothing running is a finished kill.</summary>
        internal static Question Kill => new(NoneToKill, false);

        /// <summary>A running instance, or else the stopped one a log may show. Several running instances stay an error.</summary>
        internal static Question Log => new(NoneRunning, true);
    }

    /// <summary>
    /// An instance, or a finished failure. <see cref="Notice"/> is set when the instance is the stopped one a log
    /// fell back to.
    /// </summary>
    internal readonly record struct Result(InstanceInfo? Info, string? Notice, string? Error, int Code)
    {
        public static Result Found(InstanceInfo info, string? notice = null)
        {
            return new Result(info, notice, null, 0);
        }

        public static Result Miss(string? error)
        {
            return new Result(null, null, error, 1);
        }

        public static Result Wrong(string error)
        {
            return new Result(null, null, error, 2);
        }
    }

    internal static Result Select(CliOptions options, Question question, string runDir)
    {
        var choice = From(options, Listed(runDir, false), question.None);
        // Several running instances is a choice the user still has to make. A stopped log must not stand in for it.
        if (!question.StoppedFallback || choice.Info is not null || choice.Code == 2)
            return choice;

        var asked = options.Pid is not null || options.Id is not null || options.Path is not null ||
                    options.Config is not null;
        // A selector can name an instance that stopped, but never gets a different one instead. With no selector the
        // newest instance left on disk is the log, running or not.
        var stopped = asked
            ? From(options, Listed(runDir, true), question.None).Info
            : Instances.ListIn(runDir, true).FirstOrDefault().Info;
        return stopped is null
            ? choice
            : Result.Found(stopped, $"Showing the log of stopped instance {stopped.Id}.");
    }

    private static List<InstanceInfo> Listed(string runDir, bool includeDead)
    {
        return Instances.ListIn(runDir, includeDead).Select(row => row.Info).ToList();
    }

    private static Result From(CliOptions options, List<InstanceInfo> instances, string none)
    {
        if (options.Pid is { } pid)
        {
            var match = instances.FirstOrDefault(i => i.Pid == pid);
            return match is null
                ? Result.Miss($"No instance with process id {pid}.")
                : Result.Found(match);
        }

        if (options.Id is { } id)
        {
            var matches = instances.Where(i => i.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count switch
            {
                1 => Result.Found(matches[0]),
                0 => Result.Miss($"No instance with id {id}."),
                _ => Result.Miss($"Several instances start with {id}.")
            };
        }

        if (options.Path is not null || options.Config is not null)
        {
            var project = ConfigurationSelection.FindProject(options, out var error);
            if (error is not null)
                return Result.Miss(error);
            // With -c or -p a project is always found, or the error above says why not.
            var match = instances.FirstOrDefault(i => RunsProject(i, project!));
            return match is null
                ? Result.Miss($"No running instance of {project!.Program}.")
                : Result.Found(match);
        }

        return ChooseUnselected(instances, none);
    }

    /// <summary>
    /// The instance a command with no selector acts on. One instance is that one. Several is a command-line
    /// error: kill, log and ipc must not guess.
    /// </summary>
    internal static Result ChooseUnselected(IReadOnlyList<InstanceInfo> instances, string none = NoneRunning)
    {
        if (instances.Count > 1)
        {
            var listed = string.Join(", ", instances.Select(i => $"{i.Id} ({i.ConfigPath})"));
            return Result.Wrong($"Several instances are running; pick one with -i: {listed}.");
        }

        return instances.Count == 1 ? Result.Found(instances[0]) : Result.Miss(none);
    }

    /// <summary>Whether <paramref name="info"/> runs <paramref name="project"/>, from <c>bin\shell</c> or from <c>bin\shell.next</c>.</summary>
    internal static bool RunsProject(InstanceInfo info, ConfigurationProject project)
    {
        return string.Equals(ConfigurationProject.ForProgram(info.ConfigPath)?.ProjectFile, project.ProjectFile,
            StringComparison.OrdinalIgnoreCase);
    }
}
