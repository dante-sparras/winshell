namespace Winshell.Core;

/// <summary>What plugins have published for <c>winoma desktop</c>, safe to change from any thread.</summary>
public sealed class PluginFacts
{
    private readonly object _gate = new();
    // Kept as the plugin's own values in one array. A plugin can publish every second and `winoma desktop` reads rarely,
    // so the sorted copy is made when it is read.
    private readonly Dictionary<string, KeyValuePair<string, object>[]> _byPlugin = new(StringComparer.Ordinal);

    /// <summary>
    /// Keeps the strings, booleans, and numbers in <paramref name="facts"/>. Other values are dropped.
    /// Null or empty clears what <paramref name="plugin"/> published.
    /// </summary>
    /// <param name="plugin">Plugin id.</param>
    /// <param name="facts">Names and values, such as <c>phase</c> and <c>listening</c>.</param>
    public void Set(string plugin, IReadOnlyDictionary<string, object>? facts)
    {
        ArgumentException.ThrowIfNullOrEmpty(plugin);
        var kept = Kept(facts);
        lock (_gate)
        {
            if (kept.Length == 0)
            {
                _byPlugin.Remove(plugin);
                return;
            }

            _byPlugin[plugin] = kept;
        }
    }

    /// <summary>Forgets <paramref name="plugin"/>.</summary>
    /// <param name="plugin">Plugin id.</param>
    public void Remove(string plugin)
    {
        lock (_gate)
        {
            _byPlugin.Remove(plugin);
        }
    }

    /// <summary>A copy, keyed by plugin id in ordinal order.</summary>
    /// <returns>Each plugin's facts.</returns>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> Snapshot()
    {
        var snapshot = new SortedDictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var (plugin, kept) in _byPlugin)
            {
                var clean = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var (name, value) in kept)
                {
                    clean[name] = value;
                }

                snapshot[plugin] = clean;
            }
        }

        return snapshot;
    }

    /// <summary>The strings, booleans, and whole or decimal numbers in <paramref name="facts"/>, by name in ordinal order.</summary>
    /// <param name="facts">What a plugin passed.</param>
    /// <returns>A copy the plugin cannot change.</returns>
    public static IReadOnlyDictionary<string, object> Clean(IReadOnlyDictionary<string, object>? facts)
    {
        var clean = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in Kept(facts))
        {
            clean[name] = value;
        }

        return clean;
    }

    // A number that is not finite has no JSON form, and one of them would fail every reply that carries the facts.
    private static bool Keeps(string name, object value) =>
        !string.IsNullOrWhiteSpace(name) && value switch
        {
            string or bool or int or long => true,
            double number => double.IsFinite(number),
            _ => false,
        };

    /// <summary>The facts worth keeping, copied so the plugin cannot change them afterwards.</summary>
    private static KeyValuePair<string, object>[] Kept(IReadOnlyDictionary<string, object>? facts)
    {
        if (facts is null || facts.Count == 0)
        {
            return [];
        }

        var count = 0;
        foreach (var (name, value) in facts)
        {
            if (Keeps(name, value))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        var kept = new KeyValuePair<string, object>[count];
        var next = 0;
        foreach (var fact in facts)
        {
            // A dictionary changed between the two walks is the plugin's fault. Whatever fits is kept.
            if (next < kept.Length && Keeps(fact.Key, fact.Value))
            {
                kept[next++] = fact;
            }
        }

        return next == kept.Length ? kept : kept[..next];
    }
}
