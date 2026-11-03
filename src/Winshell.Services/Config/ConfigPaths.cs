namespace Winshell;

/// <summary>Which folder a configuration is loaded from.</summary>
internal static class ConfigPaths
{
    /// <summary>When set, this folder is the config root.</summary>
    public const string OverrideVariable = "WINSHELL_CONFIG_DIR";

    /// <summary><c>%APPDATA%\winshell</c>, or <see cref="OverrideVariable"/> when it is set.</summary>
    public static string Root() => RootFrom(Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>The config root for an override value. A blank value uses <see cref="ShellLocations.ConfigRoot"/>.</summary>
    internal static string RootFrom(string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
            return Path.GetFullPath(overrideValue.Trim());
        return ShellLocations.ConfigRoot;
    }
}
