namespace Winshell.DefaultShell;

/// <summary>One row of an order list: a pin or a tile.</summary>
public sealed record SettingsOrderItem(string Key, string Title, string Glyph = "", string Icon = "");

/// <summary>A move in an order list. <see cref="Button"/> is <c>up</c> or <c>down</c>, which keeps the keyboard.</summary>
public readonly record struct SettingsOrderMove(string Key, int Offset, string Button);

/// <summary>
/// The order list Winshell Settings uses for pins and tiles. Moving steps through the rows on show. Keys those rows
/// do not contain keep their place. Comparison is case-insensitive for pins and exact for tiles.
/// </summary>
public static class SettingsOrder
{
    public static IReadOnlyList<string>? Move(IReadOnlyList<string> keys, IReadOnlyList<string> visible, string id, int offset, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var at = IndexOf(visible, id, comparison);
        if (at < 0 || offset == 0 || at + offset < 0 || at + offset >= visible.Count)
            return null;
        var neighbour = visible[at + offset];
        var list = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!key.Equals(id, comparison))
                list.Add(key);
        }
        var to = IndexOf(list, neighbour, comparison);
        if (to < 0)
            return null;
        list.Insert(offset > 0 ? to + 1 : to, id);
        return list;
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> keys, string id, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var list = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!key.Equals(id, comparison))
                list.Add(key);
        }
        return list;
    }

    /// <summary>The row that takes the place of the one at <paramref name="index"/>, or null when none remain.</summary>
    public static string? FocusIndex(IReadOnlyList<string> keys, int index) =>
        keys.Count == 0 ? null : keys[Math.Clamp(index, 0, keys.Count - 1)];

    private static int IndexOf(IReadOnlyList<string> keys, string id, StringComparison comparison)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].Equals(id, comparison))
                return i;
        }
        return -1;
    }
}
