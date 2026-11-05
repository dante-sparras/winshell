using Winshell.Diagnostics;

namespace Winshell.Commands;

internal static class DoctorCommand
{
    /// <summary>winshell doctor: checks this PC's setup, prints a line each, and fails when a check fails. It never builds.</summary>
    internal static int Run(CliOptions options)
    {
        var root = ConfigPaths.Root();
        var project = ConfigurationSelection.FindProject(options, out var problem);
        string? search = project?.Folder;
        // The folder the Folders check writes to is the -p folder as given, never its parent: that could be a drive, the
        // profile or another project.
        if (search is null && options.Path is { } path)
        {
            search = System.IO.Path.GetFullPath(path);
        }
        else if (search is null && options.Config is { } name)
        {
            search = System.IO.Path.Combine(root, name);
        }
        var checks = Diagnostics.Doctor.Run(new DoctorContext(
            Environment.ProcessPath!, // The CLI runs from its apphost.
            root,
            project?.ProjectFile,
            problem,
            DoctorInstances(),
            ShellLocations.LocalRoot,
            ShellLocations.CrashRoot,
            search));
        if (options.Json)
        {
            Console.WriteLine(Diagnostics.DoctorReport.ToJson(checks, CliMetadata.Version));
        }
        else
        {
            var color = !options.NoColor && !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
            Console.Write(Diagnostics.DoctorReport.Format(checks, CliMetadata.Version, color));
        }
        return checks.Any(c => c.Status == CheckStatus.Fail) ? 1 : 0;
    }

    /// <summary>The running instances, or none when their folder cannot be read: the doctor still runs its checks.</summary>
    private static List<DoctorInstance> DoctorInstances()
    {
        try
        {
            return Instances.List(false).Select(i => new DoctorInstance(i.Info.Id, i.Info.Pid, i.Info.ConfigPath, i.Info.Log)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
