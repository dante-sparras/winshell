namespace Winshell.Core;

/// <summary>Where a plugin assembly is loaded. One that sits in the shell's folder shares the shell's load context.</summary>
public static class PluginAssembly
{
    /// <summary>
    /// True when <paramref name="assemblyPath"/> is inside <paramref name="baseDirectory"/>.
    /// A plugin assembly shipped with the shell uses the shell's WinUI types. A second load context cannot see them, and the type then reads as missing.
    /// An assembly outside that folder keeps its own context.
    /// </summary>
    /// <param name="assemblyPath">The assembly file the manifest names.</param>
    /// <param name="baseDirectory">The shell's own folder.</param>
    /// <returns>True when the file is in that folder or a folder under it.</returns>
    public static bool SharesShellContext(string assemblyPath, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var wanted = Path.GetFullPath(assemblyPath);
        var home = Path.GetFullPath(baseDirectory);
        if (!home.EndsWith(Path.DirectorySeparatorChar) && !home.EndsWith(Path.AltDirectorySeparatorChar))
        {
            home += Path.DirectorySeparatorChar;
        }

        return wanted.StartsWith(home, StringComparison.OrdinalIgnoreCase);
    }
}
