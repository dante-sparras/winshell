namespace Winshell.Core;

/// <summary>A bar after enablement, anchor, and catalog rules.</summary>
public sealed class ResolvedLayout
{
    /// <summary>Left section, in order.</summary>
    public IReadOnlyList<BarSlot> Left { get; init; } = [];

    /// <summary>Center widgets before the anchor, or the whole center group when <see cref="CenterAsGroup"/> is true.</summary>
    public IReadOnlyList<BarSlot> CenterBefore { get; init; } = [];

    /// <summary>The pinned center widget.</summary>
    public BarSlot? Anchor { get; init; }

    /// <summary>Center widgets after the anchor.</summary>
    public IReadOnlyList<BarSlot> CenterAfter { get; init; } = [];

    /// <summary>True when the center list is centered as one group.</summary>
    public bool CenterAsGroup { get; init; }

    /// <summary>Right section, in order.</summary>
    public IReadOnlyList<BarSlot> Right { get; init; } = [];

    /// <summary>Enabled plugins that are not already sitting on the bar.</summary>
    public IReadOnlyList<PluginManifest> Background { get; init; } = [];

    /// <summary>Slots and manifests that were skipped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Every id this layout runs: each slot, the anchor, and each background plugin.</summary>
    /// <returns>Plugin and command-slot ids.</returns>
    public HashSet<string> Ids()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Collect(Left);
        Collect(CenterBefore);
        if (Anchor is not null)
        {
            ids.Add(Anchor.Id);
        }

        Collect(CenterAfter);
        Collect(Right);
        foreach (var plugin in Background)
        {
            ids.Add(plugin.Id);
        }

        return ids;

        void Collect(IReadOnlyList<BarSlot> slots)
        {
            foreach (var slot in slots)
            {
                ids.Add(slot.Id);
            }
        }
    }

    /// <summary>The manifests in <paramref name="catalog"/> this layout runs. A switched-off plugin is absent, and so are its chords.</summary>
    /// <param name="catalog">Discovered manifests.</param>
    /// <returns>The running manifests, in catalog order.</returns>
    public IReadOnlyList<PluginManifest> Running(IReadOnlyList<PluginManifest> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var ids = Ids();
        return catalog.Where(plugin => ids.Contains(plugin.Id)).ToList();
    }
}
