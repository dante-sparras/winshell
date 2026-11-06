namespace Winshell.Diagnostics;

/// <summary>The doctor's checks of the configuration project. They read the project file as data and never build.</summary>
internal static class DoctorProject
{
    /// <summary>
    /// The configuration project in <paramref name="configRoot"/>, or why there is not one: whether it was found,
    /// whether a file the build would run was downloaded, whether its program has been built, and whether a build
    /// is needed. Nothing here runs MSBuild.
    /// </summary>
    internal static IReadOnlyList<DoctorCheck> Configuration(string configRoot)
    {
        var found = FindProject(null, configRoot);
        return Configuration(found.Project, found.Problem);
    }

    /// <summary>The same checks for <paramref name="projectFile"/>, the project the CLI picked, read again as it is now.</summary>
    internal static IReadOnlyList<DoctorCheck> Picked(string projectFile, string configRoot)
    {
        var found = FindProject(projectFile, configRoot);
        return Configuration(found.Project, found.Problem);
    }

    /// <summary>The same checks for a project the caller has found already, or for none and <paramref name="problem"/>.</summary>
    internal static IReadOnlyList<DoctorCheck> Configuration(ConfigurationProject? project, string? problem)
    {
        return
        [
            Doctor.Guard("project", "Project", () => Project(project, problem)),
            Doctor.Guard("downloaded", "Downloaded", () => Downloaded(project)),
            Doctor.Guard("built", "Built", () => Built(project)),
            Doctor.Guard("build", "Build", () => BuildNeeded(project))
        ];
    }

    private readonly record struct FoundProject(ConfigurationProject? Project, string? Problem);

    /// <param name="path">A project file the CLI picked, or null to search <paramref name="root"/>.</param>
    /// <param name="root">The config folder.</param>
    private static FoundProject FindProject(string? path, string root)
    {
        try
        {
            var project = ConfigurationProject.Find(path, null, root, out var problem);
            return new FoundProject(project, problem);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FoundProject(null, $"Winshell cannot read {path ?? "the config folder"}: {e.Message}");
        }
    }

    internal static DoctorCheck Project(ConfigurationProject? project, string? problem)
    {
        if (problem is not null)
            return new DoctorCheck("project", "Project", CheckStatus.Fail, problem,
                "Pick a config with winshell -c <name>, or give its folder with -p, and make sure your account can read it.");
        if (project is null)
            return new DoctorCheck("project", "Project", CheckStatus.Warn,
                "No configuration project, so winshell run starts nothing.",
                "Make one with dotnet new winshell, as the configuration guide shows.");
        return new DoctorCheck("project", "Project", CheckStatus.Pass, project.ProjectFile);
    }

    internal static DoctorCheck Downloaded(ConfigurationProject? project)
    {
        if (project is null)
            return new DoctorCheck("downloaded", "Downloaded", CheckStatus.Pass, "No configuration project to check.");
        if (project.Downloaded() is { } file)
            return new DoctorCheck("downloaded", "Downloaded", CheckStatus.Fail,
                $"{file} was downloaded from the internet, so Winshell does not build it. It would run as you.",
                "If you trust it, unblock it: right-click it > Properties > Unblock, or run Unblock-File. Then save it, or start Winshell again.");
        return new DoctorCheck("downloaded", "Downloaded", CheckStatus.Pass,
            "Nothing the build would run was downloaded.");
    }

    internal static DoctorCheck Built(ConfigurationProject? project)
    {
        if (project is null)
            return new DoctorCheck("built", "Built", CheckStatus.Pass, "No configuration project to build.");
        if (!File.Exists(project.Program))
            return new DoctorCheck("built", "Built", CheckStatus.Fail, $"{project.Name} has not been built.",
                "Run winshell run once to build it.");
        return new DoctorCheck("built", "Built", CheckStatus.Pass, project.Program);
    }

    internal static DoctorCheck BuildNeeded(ConfigurationProject? project)
    {
        if (project is null)
            return new DoctorCheck("build", "Build", CheckStatus.Pass, "No configuration project to build.");
        // A missing program is Built's failure, so it is not reported twice.
        if (!File.Exists(project.Program))
            return new DoctorCheck("build", "Build", CheckStatus.Pass,
                $"Nothing to compare until {project.Name} is built.");
        if (!project.NeedsBuild())
            return new DoctorCheck("build", "Build", CheckStatus.Pass, $"{project.Name} is up to date.");
        return new DoctorCheck("build", "Build", CheckStatus.Warn,
            $"A file in {project.Name} is newer than the program.",
            "Run winshell run to build it. Signing in uses the last build.");
    }
}
