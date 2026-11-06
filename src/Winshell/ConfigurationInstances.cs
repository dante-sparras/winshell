namespace Winshell;

/// <summary>
/// Which instances belong to a configuration. Each one is the Run build, <c>bin\shell</c>, or the Dev build,
/// <c>bin\shell.next</c>. A program whose folder only shares a prefix with those is not one of them.
/// </summary>
internal static class ConfigurationInstances
{
    internal enum Build
    {
        Run,
        Dev
    }

    internal readonly record struct Member(InstanceInfo Info, Build Build);

    /// <summary>The instances in <paramref name="instances"/> whose program is one of <paramref name="project"/>'s builds.</summary>
    internal static List<Member> Of(IEnumerable<InstanceInfo> instances, ConfigurationProject project)
    {
        var members = new List<Member>();
        foreach (var info in instances)
            if (BuildOf(info.ConfigPath, project) is { } build)
                members.Add(new Member(info, build));
        return members;
    }

    /// <summary>
    /// Run or Dev when <paramref name="program"/> is that build of <paramref name="project"/>. Null when it is a
    /// different project, or a path that only begins the way <c>bin\shell</c> does.
    /// </summary>
    internal static Build? BuildOf(string program, ConfigurationProject project)
    {
        if (ConfigurationProject.ForProgram(program) is not { } owned
            || !string.Equals(owned.ProjectFile, project.ProjectFile, StringComparison.OrdinalIgnoreCase))
            return null;

        string directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(program)) ?? "";
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException
                                      or IOException)
        {
            return null;
        }

        if (directory.Equals(Path.GetFullPath(project.OutputFolder), StringComparison.OrdinalIgnoreCase))
            return Build.Run;
        if (directory.Equals(Path.GetFullPath(project.OutputFolder + ".next"), StringComparison.OrdinalIgnoreCase))
            return Build.Dev;
        return null;
    }
}
