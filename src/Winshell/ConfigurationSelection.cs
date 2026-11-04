namespace Winshell;

internal static class ConfigurationSelection
{
    /// <summary>The project <c>run</c> would start, or null when the default shell runs. A reason means it cannot choose.</summary>
    internal static ConfigurationProject? FindProject(CliOptions options, out string? error)
    {
        try
        {
            return ConfigurationProject.Find(options.Path, options.Config, ConfigPaths.Root(), out error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = options.Path is { } path ? $"Winshell cannot read {path}: {e.Message}" : $"Winshell cannot read the config folder: {e.Message}";
            return null;
        }
    }
}
