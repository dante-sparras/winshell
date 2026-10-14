namespace Winshell.Core;

/// <summary>
/// Finds the plugin folders on disk and reads their manifests. A user id in the <c>winoma.</c> prefix, or equal to a built-in id, is skipped,
/// and so is one that <c>winoma plugin new</c> would refuse.
/// </summary>
public static class PluginFolders
{
    /// <summary>
    /// Scans <paramref name="builtInRoot"/> and then <paramref name="userRoot"/>.
    /// Each immediate child directory that contains <c>manifest.json</c> is a plugin.
    /// </summary>
    public static DiscoveredPlugins Discover(string? builtInRoot, string? userRoot)
    {
        var byId = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);
        var warnings = new List<string>();
        ReadRoot(builtInRoot, firstParty: true, byId, warnings);
        ReadRoot(userRoot, firstParty: false, byId, warnings);
        return new DiscoveredPlugins(byId.Values.OrderBy(plugin => plugin.Id, StringComparer.Ordinal).ToList(), warnings);
    }

    /// <summary>
    /// The plugins <paramref name="bar"/> runs, from <paramref name="builtInRoot"/> and then <paramref name="userRoot"/>.
    /// The desktop and the keyboard hook process read chords against this list, so a switched-off plugin's chords pass to Windows.
    /// </summary>
    /// <param name="builtInRoot">The shipped <c>plugins</c> folder.</param>
    /// <param name="userRoot">The config folder's <c>plugins</c>.</param>
    /// <param name="bar">The bar document from <c>settings.json</c>. Null is the default bar.</param>
    /// <returns>Running manifests, in ordinal id order.</returns>
    public static IReadOnlyList<PluginManifest> Running(string? builtInRoot, string? userRoot, BarLayoutDocument? bar) =>
        Running(Discover(builtInRoot, userRoot).Plugins, bar);

    /// <summary>The plugins <paramref name="bar"/> runs out of <paramref name="discovered"/>, for a caller that has already read the folders.</summary>
    /// <param name="discovered">What <see cref="Discover"/> found.</param>
    /// <param name="bar">The bar document from <c>settings.json</c>. Null is the default bar.</param>
    /// <returns>Running manifests, in ordinal id order.</returns>
    public static IReadOnlyList<PluginManifest> Running(IReadOnlyList<PluginManifest> discovered, BarLayoutDocument? bar)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        return BarLayoutResolver.Resolve(bar, discovered).Running(discovered);
    }

    private static void ReadRoot(string? root, bool firstParty, Dictionary<string, PluginManifest> byId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("could not list " + root);
            return;
        }

        foreach (var directory in directories.OrderBy(path => path, StringComparer.Ordinal))
        {
            // A plugin on its way in or out. `winoma plugin add` clones into the first and `remove` renames to the second,
            // and a clone that stopped half way must not load.
            if (directory.EndsWith(".adding", StringComparison.OrdinalIgnoreCase) || directory.EndsWith(".removing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            if (!PluginManifest.TryRead(manifestPath, firstParty, out var manifest, out var error) || manifest is null)
            {
                warnings.Add(error ?? manifestPath);
                continue;
            }

            if (!firstParty && (PluginIds.IsReserved(manifest.Id) || (byId.TryGetValue(manifest.Id, out var prior) && prior.FirstParty)))
            {
                warnings.Add(manifest.Id + " is reserved");
                continue;
            }

            // A folder made by hand is held to the id `winoma plugin new` would take. One with a space can never be bound,
            // and one with a line break would be written raw into the log, so the warning names the file and not the id.
            if (!firstParty && !PluginIds.IsUserId(manifest.Id))
            {
                warnings.Add(manifestPath + " has an unusable id");
                continue;
            }

            if (byId.TryGetValue(manifest.Id, out var existing))
            {
                warnings.Add(manifest.Id + " replaces " + existing.Path);
            }

            byId[manifest.Id] = manifest;
        }
    }
}

/// <summary>Plugins discovered on disk and the manifests that were rejected.</summary>
/// <param name="Plugins">Accepted manifests, in ordinal id order.</param>
/// <param name="Warnings">Rejected manifests.</param>
public sealed record DiscoveredPlugins(IReadOnlyList<PluginManifest> Plugins, IReadOnlyList<string> Warnings);
