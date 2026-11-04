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
        var found = FindProject(configRoot);
        return
        [
            Doctor.Guard("project", "Project", () => Project(found.Project, found.Problem)),
            Doctor.Guard("downloaded", "Downloaded", () => Downloaded(found.Project)),
            Doctor.Guard("built", "Built", () => Built(found.Project)),
            Doctor.Guard("build", "Build", () => BuildNeeded(found.Project)),
        ];
    }

    private readonly record struct FoundProject(ConfigurationProject? Project, string? Problem);

    private static FoundProject FindProject(string root)
    {
        try
        {
            var project = ConfigurationProject.Find(null, null, root, out var problem);
            return new(project, problem);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, $"Winshell cannot read the config folder: {e.Message}");
        }
    }

    internal static DoctorCheck Project(ConfigurationProject? project, string? problem)
    {
        if (problem is not null)
            return new("project", "Project", CheckStatus.Fail, problem, "Pick a config with winshell -c <name>, or give its folder with -p, and make sure your account can read it.");
        if (project is null)
            return new("project", "Project", CheckStatus.Pass, "No configuration project: Winshell runs its default shell.");
        return new("project", "Project", CheckStatus.Pass, project.ProjectFile);
    }

    internal static DoctorCheck Downloaded(ConfigurationProject? project)
    {
        if (project is null)
            return new("downloaded", "Downloaded", CheckStatus.Pass, "No configuration project to check.");
        if (project.Downloaded() is { } file)
            return new("downloaded", "Downloaded", CheckStatus.Fail, $"{file} was downloaded from the internet, so Winshell does not build it. It would run as you.", "If you trust it, unblock it: right-click it > Properties > Unblock, or run Unblock-File. Then save it, or start Winshell again.");
        return new("downloaded", "Downloaded", CheckStatus.Pass, "Nothing the build would run was downloaded.");
    }

    internal static DoctorCheck Built(ConfigurationProject? project)
    {
        if (project is null)
            return new("built", "Built", CheckStatus.Pass, "No configuration project to build.");
        if (!File.Exists(project.Program))
            return new("built", "Built", CheckStatus.Fail, $"{project.Name} has not been built.", "Run winshell run once to build it.");
        return new("built", "Built", CheckStatus.Pass, project.Program);
    }

    internal static DoctorCheck BuildNeeded(ConfigurationProject? project)
    {
        if (project is null)
            return new("build", "Build", CheckStatus.Pass, "No configuration project to build.");
        // A missing program is Built's failure, so it is not reported twice.
        if (!File.Exists(project.Program))
            return new("build", "Build", CheckStatus.Pass, $"Nothing to compare until {project.Name} is built.");
        if (!project.NeedsBuild())
            return new("build", "Build", CheckStatus.Pass, $"{project.Name} is up to date.");
        return new("build", "Build", CheckStatus.Warn, $"A file in {project.Name} is newer than the program.", "Run winshell run to build it. Signing in uses the last build.");
    }
}
