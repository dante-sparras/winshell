namespace Winshell;

internal static class ConfigurationSelection
{
    /// <summary>The project <c>run</c> would start, or null when there is none. A reason means it cannot choose.</summary>
    internal static ConfigurationProject? FindProject(CliOptions options, out string? error)
    {
        try
        {
            return ConfigurationProject.Find(options.Path, options.Config, ConfigPaths.Root(), out error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var what = options switch
            {
                { Path: { } path } => path,
                { Config: { } name } => $"config \"{name}\"",
                _ => "the config folder"
            };
            error = $"Winshell cannot read {what}: {e.Message}";
            return null;
        }
    }
}
