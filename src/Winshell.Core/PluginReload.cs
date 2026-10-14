namespace Winshell.Core;

/// <summary>Decides which user plugins a rebuild changed, so the desktop reloads them without a restart.</summary>
public static class PluginReload
{
    /// <summary>
    /// User plugins whose assembly was written after the copy this process loaded.
    /// An id that has not been loaded yet is absent: the rescan loads that file.
    /// A built-in is absent: it changes with a new publish (ADR 0042).
    /// </summary>
    /// <param name="catalog">Discovered manifests.</param>
    /// <param name="loadedAt">Manifest id to the write time of the assembly this process loaded.</param>
    /// <returns>Ids to load again.</returns>
    public static IReadOnlyList<string> NewerThan(IReadOnlyList<PluginManifest> catalog, IReadOnlyDictionary<string, DateTimeOffset> loadedAt)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loadedAt);
        var ids = new List<string>();
        foreach (var manifest in catalog)
        {
            if (manifest.FirstParty || !loadedAt.TryGetValue(manifest.Id, out var loaded))
            {
                continue;
            }

            var path = AssemblyPath(manifest);
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            var written = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            if (written > loaded)
            {
                ids.Add(manifest.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// True when a change to <paramref name="path"/> may be a rebuild of a user plugin: an assembly or its symbols,
    /// anywhere under <paramref name="plugins"/>, the user plugin folder. A build writes intermediate files under <c>obj</c>,
    /// which are not the plugin.
    /// </summary>
    /// <param name="plugins">The user plugin folder.</param>
    /// <param name="path">The file that changed.</param>
    /// <returns>True when the desktop should look for a newer plugin.</returns>
    public static bool Rebuilt(string plugins, string path)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(path);
        var relative = Path.GetRelativePath(plugins, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return false;
        }

        var parts = relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The assembly file next to <paramref name="manifest"/>, or null when the path leaves that folder or cannot be a path.</summary>
    /// <param name="manifest">A discovered manifest.</param>
    /// <returns>A full path, or null.</returns>
    public static string? AssemblyPath(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Assembly) || Path.IsPathRooted(manifest.Assembly))
        {
            return null;
        }

        // Windows has no path with a null character in it, and Path.GetFullPath throws on one.
        if (manifest.Assembly.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        if (manifest.Assembly.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(manifest.Path);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(folder, manifest.Assembly));
    }
}
