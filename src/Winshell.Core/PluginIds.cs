namespace Winshell.Core;

/// <summary>Reserved ids and the clone that stands in for a built-in id.</summary>
public static class PluginIds
{
    /// <summary>True when <paramref name="id"/> is in the <c>winoma.</c> prefix a user folder cannot claim.</summary>
    /// <param name="id">A manifest id.</param>
    /// <returns>True for <c>winoma.</c> and anything that starts with it, ordinal and case-sensitive.</returns>
    public static bool IsReserved(string? id) =>
        !string.IsNullOrEmpty(id) && id.StartsWith("winoma.", StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="id"/> is one a user folder may take: up to 80 characters of lowercase ASCII letters and digits,
    /// with single dots or hyphens between them, outside the reserved prefix.
    /// </summary>
    /// <param name="id">A proposed manifest id.</param>
    /// <returns>True when <c>winoma plugin new</c> or <c>clone</c> may write it.</returns>
    internal static bool IsUserId(string id)
    {
        if (id.Length == 0 || id.Length > 80 || IsReserved(id))
        {
            return false;
        }

        var separator = false;
        for (var i = 0; i < id.Length; i++)
        {
            var ch = id[i];
            var word = ch is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (i == 0)
            {
                if (!word)
                {
                    return false;
                }

                continue;
            }

            if (ch is '.' or '-')
            {
                if (separator)
                {
                    return false;
                }

                separator = true;
                continue;
            }

            if (!word)
            {
                return false;
            }

            separator = false;
        }

        return !separator;
    }

    /// <summary>
    /// The plugin id that implements <paramref name="id"/>.
    /// An enabled clone whose <see cref="PluginManifest.ClonedFrom"/> matches wins. Otherwise the id itself.
    /// </summary>
    /// <param name="id">The id a bar slot or a caller asked for.</param>
    /// <param name="catalog">Discovered manifests.</param>
    /// <param name="enabled">Clone ids that are on the bar or in <c>plugins</c>, and are not disabled.</param>
    /// <returns>The clone id, or <paramref name="id"/> when no enabled clone claims it.</returns>
    public static string Resolve(string id, IReadOnlyList<PluginManifest> catalog, IReadOnlySet<string> enabled)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(enabled);
        return Routes(catalog, enabled, []).TryGetValue(id, out var clone) ? clone : id;
    }

    /// <summary>
    /// Maps each built-in id to the enabled clone that stands in for it.
    /// Two enabled clones of one id: the first id in ordinal order wins, and <paramref name="warnings"/> names the rest.
    /// </summary>
    /// <param name="catalog">Discovered manifests.</param>
    /// <param name="enabled">Clone ids that may take over a built-in.</param>
    /// <param name="warnings">Receives a line for each extra clone.</param>
    /// <returns>Source id to clone id.</returns>
    public static Dictionary<string, string> Routes(
        IReadOnlyList<PluginManifest> catalog,
        IReadOnlySet<string> enabled,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(warnings);
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var manifest in catalog.OrderBy(plugin => plugin.Id, StringComparer.Ordinal))
        {
            if (manifest.FirstParty || manifest.ClonedFrom.Length == 0 || !enabled.Contains(manifest.Id))
            {
                continue;
            }

            if (routes.TryGetValue(manifest.ClonedFrom, out var prior))
            {
                warnings.Add(manifest.Id + " loses to " + prior + " for " + manifest.ClonedFrom);
                continue;
            }

            routes[manifest.ClonedFrom] = manifest.Id;
        }

        return routes;
    }
}
