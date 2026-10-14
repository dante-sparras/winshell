namespace Winshell.Core;

/// <summary>Turns a bar document and a catalog into the widgets the host draws.</summary>
public static class BarLayoutResolver
{
    /// <summary>
    /// Resolves <paramref name="document"/>. Null uses the bar the product installed, or an empty document when none is installed.
    /// A present document is the whole layout. Built-in bar widgets stay off the bar unless listed.
    /// A config-folder plugin is included when its id is in the layout or in <c>plugins</c>.
    /// </summary>
    public static ResolvedLayout Resolve(BarLayoutDocument? document, IReadOnlyList<PluginManifest> catalog)
    {
        var source = document ?? InstalledBar.OrEmpty();
        var layout = source.Layout ?? new BarSections();
        var disabled = new HashSet<string>(source.DisabledPlugins.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        var enabledThirdParty = new HashSet<string>(source.Plugins.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        var byId = catalog.ToDictionary(plugin => plugin.Id, StringComparer.Ordinal);
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var mentioned = new HashSet<string>(enabledThirdParty, StringComparer.Ordinal);
        Mention(layout.Left, mentioned);
        Mention(layout.Center, mentioned);
        Mention(layout.Right, mentioned);
        mentioned.ExceptWith(disabled);
        var routes = PluginIds.Routes(catalog, mentioned, warnings);

        var left = Take(layout.Left, "left", byId, disabled, seen, routes, warnings);
        var center = Take(layout.Center, "center", byId, disabled, seen, routes, warnings);
        var right = Take(layout.Right, "right", byId, disabled, seen, routes, warnings);
        PlaceListed(enabledThirdParty, byId, disabled, seen, left, center, right, warnings);

        var background = new List<PluginManifest>();
        foreach (var plugin in catalog)
        {
            if (seen.Contains(plugin.Id) || disabled.Contains(plugin.Id) || plugin.Kinds.Contains(PluginKind.BarWidget) && plugin.Kinds.Count == 1)
            {
                continue;
            }

            var listed = enabledThirdParty.Contains(plugin.Id) || plugin.FirstParty;
            if (!plugin.FirstParty && !listed)
            {
                continue;
            }

            if (plugin.FirstParty && disabled.Contains(plugin.Id))
            {
                continue;
            }

            // An enabled clone stands in for its built-in, on the bar or off it, so the built-in does not run beside it.
            if (plugin.FirstParty && routes.ContainsKey(plugin.Id))
            {
                continue;
            }

            // A panel opens from its bar item, so a widget with a panel that is off the bar stays stopped.
            var widget = plugin.Kinds.Contains(PluginKind.BarWidget);
            if (!widget || !seen.Contains(plugin.Id))
            {
                if (plugin.Kinds.Any(kind => kind is PluginKind.Service or PluginKind.Overlay or PluginKind.Menu or PluginKind.Bar || kind is PluginKind.Panel && !widget)
                    && (plugin.FirstParty || enabledThirdParty.Contains(plugin.Id)))
                {
                    background.Add(plugin);
                }
            }
        }

        var anchorId = source.CenterAnchor ?? "";
        if (routes.TryGetValue(anchorId, out var anchorClone))
        {
            anchorId = anchorClone;
        }

        if (anchorId.Length == 0 || center.All(slot => !slot.Id.Equals(anchorId, StringComparison.Ordinal)))
        {
            return new ResolvedLayout
            {
                Left = left,
                CenterBefore = center,
                CenterAsGroup = true,
                Right = right,
                Background = background,
                Warnings = warnings,
            };
        }

        var index = center.FindIndex(slot => slot.Id.Equals(anchorId, StringComparison.Ordinal));
        return new ResolvedLayout
        {
            Left = left,
            CenterBefore = center.Take(index).ToList(),
            Anchor = center[index],
            CenterAfter = center.Skip(index + 1).ToList(),
            Right = right,
            Background = background,
            Warnings = warnings,
        };
    }

    private static void PlaceListed(
        HashSet<string> enabledThirdParty,
        Dictionary<string, PluginManifest> byId,
        HashSet<string> disabled,
        HashSet<string> seen,
        List<BarSlot> left,
        List<BarSlot> center,
        List<BarSlot> right,
        List<string> warnings)
    {
        foreach (var id in enabledThirdParty)
        {
            if (seen.Contains(id) || disabled.Contains(id) || !byId.TryGetValue(id, out var plugin))
            {
                if (!seen.Contains(id) && !disabled.Contains(id) && !byId.ContainsKey(id))
                {
                    warnings.Add("skipped unknown plugin " + id);
                }

                continue;
            }

            if (!plugin.Kinds.Contains(PluginKind.BarWidget) || plugin.FirstParty)
            {
                continue;
            }

            var slot = new BarSlot { Id = id };
            switch (plugin.DefaultSection)
            {
                case "left":
                    left.Add(slot);
                    break;
                case "right":
                    right.Add(slot);
                    break;
                default:
                    center.Add(slot);
                    break;
            }

            seen.Add(id);
        }
    }

    private static void Mention(IEnumerable<BarSlot> slots, HashSet<string> mentioned)
    {
        foreach (var slot in slots)
        {
            if (!slot.IsCommand && !string.IsNullOrWhiteSpace(slot.Id))
            {
                mentioned.Add(slot.Id);
            }
        }
    }

    private static BarSlot Rerouted(BarSlot slot, string id) => new()
    {
        Id = id,
        Type = slot.Type,
        Exec = slot.Exec,
        Interval = slot.Interval,
        OnClick = slot.OnClick,
        Formats = [.. slot.Formats],
        VerticalFormat = slot.VerticalFormat,
        Settings = new Dictionary<string, string>(slot.Settings, StringComparer.Ordinal),
    };

    private static List<BarSlot> Take(
        IEnumerable<BarSlot> slots,
        string section,
        Dictionary<string, PluginManifest> byId,
        HashSet<string> disabled,
        HashSet<string> seen,
        IReadOnlyDictionary<string, string> routes,
        List<string> warnings)
    {
        var result = new List<BarSlot>();
        foreach (var slot in slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
            {
                warnings.Add("skipped an empty id in " + section);
                continue;
            }

            if (!seen.Add(slot.Id))
            {
                warnings.Add("skipped duplicate " + slot.Id);
                continue;
            }

            if (disabled.Contains(slot.Id))
            {
                continue;
            }

            if (slot.IsCommand)
            {
                result.Add(slot);
                continue;
            }

            if (routes.TryGetValue(slot.Id, out var clone))
            {
                if (!seen.Add(clone))
                {
                    warnings.Add("skipped duplicate " + slot.Id);
                    continue;
                }

                if (!byId.TryGetValue(clone, out var target) || !target.Kinds.Contains(PluginKind.BarWidget))
                {
                    warnings.Add("skipped " + clone);
                    seen.Remove(slot.Id);
                    seen.Remove(clone);
                    continue;
                }

                result.Add(Rerouted(slot, clone));
                continue;
            }

            if (!byId.TryGetValue(slot.Id, out var plugin))
            {
                warnings.Add("skipped unknown widget " + slot.Id);
                seen.Remove(slot.Id);
                continue;
            }

            if (!plugin.FirstParty && !plugin.Kinds.Contains(PluginKind.BarWidget))
            {
                warnings.Add("skipped " + slot.Id);
                seen.Remove(slot.Id);
                continue;
            }

            result.Add(slot);
        }

        return result;
    }
}
