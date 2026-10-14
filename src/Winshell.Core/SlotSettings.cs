using System.Globalization;

namespace Winshell.Core;

/// <summary>The values a bar slot may store for a plugin, checked against its <see cref="PluginManifest.Schema"/>.</summary>
internal static class SlotSettings
{
    /// <summary>
    /// The text a slot stores for <paramref name="value"/>. A number is written in invariant round-trip form. A bool is <c>true</c> or <c>false</c>.
    /// </summary>
    /// <param name="manifest">The plugin, or null when it is not installed.</param>
    /// <param name="id">The id named in the warning.</param>
    /// <param name="key">The setting name.</param>
    /// <param name="value">The value as typed.</param>
    /// <param name="stored">The value to store, or empty.</param>
    /// <param name="warning">The sentence when the value is refused, or empty.</param>
    /// <returns>False when the schema has no such key or the value does not fit its type.</returns>
    public static bool TryStore(PluginManifest? manifest, string id, string key, string value, out string stored, out string warning)
    {
        stored = "";
        var setting = manifest?.Schema.FirstOrDefault(item => item.Key.Equals(key, StringComparison.Ordinal));
        if (setting is null)
        {
            warning = id + " has no setting " + key;
            return false;
        }

        if (setting.Type == "string")
        {
            stored = value;
            warning = "";
            return true;
        }

        if (setting.Type == "number")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            {
                warning = id + " setting " + key + " is not a number";
                return false;
            }

            // The shortest text that reads back as the same number. G17 stores 0.1 as 0.10000000000000001.
            stored = number.ToString("R", CultureInfo.InvariantCulture);
            warning = "";
            return true;
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            stored = "true";
            warning = "";
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            stored = "false";
            warning = "";
            return true;
        }

        warning = id + " setting " + key + " is not true or false";
        return false;
    }

    /// <summary>Drops the values on <paramref name="slot"/> the schema refuses and rewrites the rest in stored form.</summary>
    /// <param name="slot">A slot that belongs to <paramref name="manifest"/>.</param>
    /// <param name="manifest">The plugin.</param>
    /// <returns>A line for each dropped value.</returns>
    public static List<string> Clean(BarSlot slot, PluginManifest manifest)
    {
        var warnings = new List<string>();
        if (slot.Settings.Count == 0)
        {
            return warnings;
        }

        var next = new Dictionary<string, string>(slot.Settings.Count, StringComparer.Ordinal);
        var changed = false;
        foreach (var pair in slot.Settings)
        {
            if (!TryStore(manifest, manifest.Id, pair.Key, pair.Value, out var stored, out var warning))
            {
                warnings.Add(warning);
                changed = true;
                continue;
            }

            next[pair.Key] = stored;
            if (!string.Equals(stored, pair.Value, StringComparison.Ordinal))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return warnings;
        }

        slot.Settings.Clear();
        foreach (var pair in next)
        {
            slot.Settings[pair.Key] = pair.Value;
        }

        return warnings;
    }
}
