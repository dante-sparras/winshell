namespace Winshell;

/// <summary>Folders the shell reads that depend on which extensions loaded.</summary>
internal static class ShellPaths
{
    /// <summary>The built-in plugins: beside the first extension that ships them, else beside the shell (<see cref="ProductFolders.BuiltInPlugins"/>).</summary>
    internal static string PluginRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>The folder that holds the built-in plugins' assemblies: the one above <see cref="PluginRoot"/>.</summary>
    internal static string PluginAssemblies => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(PluginRoot)) ?? AppContext.BaseDirectory;
}
