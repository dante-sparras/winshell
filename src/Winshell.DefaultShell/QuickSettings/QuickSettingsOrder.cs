namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The rules for the tile order <c>quick-settings.toml</c> saves, over keys alone. The flyout adds which tiles the PC
/// can show; Winshell Settings lists every tile Winshell knows. A missing list is the default order and an empty list
/// is a choice to show no tiles. A key a tile does not have stays in the list.
/// </summary>
internal static class QuickSettingsOrder
{
    /// <summary>The saved keys, or every tile's key in the default order when the file has no list.</summary>
    public static List<string> Keys(IReadOnlyList<object?>? pinned) =>
        pinned is null ? [.. QuickSettingsKind.All.Select(static kind => kind.Key)] : [.. pinned.OfType<string>()];

    /// <summary>
    /// The keys with <paramref name="key"/> put where <paramref name="target"/> is: before it when moving back, after
    /// it when moving on. Null when nothing changes.
    /// </summary>
    public static List<string>? Move(IReadOnlyList<string> keys, string key, string target)
    {
        var at = keys.ToList().IndexOf(target);
        // The index is taken before the key is removed, so a tile moving on lands after the target.
        if (key == target || at < 0)
            return null;
        var moved = keys.Where(item => item != key).ToList();
        moved.Insert(at, key);
        return moved;
    }

    /// <summary>The key <paramref name="offset"/> places from <paramref name="key"/> among the keys on show, kept inside the list. Null when <paramref name="key"/> is not on show.</summary>
    public static string? Step(IReadOnlyList<string> shown, string key, int offset)
    {
        var from = shown.ToList().IndexOf(key);
        return from < 0 ? null : shown[Math.Max(0, Math.Min(shown.Count - 1, from + offset))];
    }

    public static List<string> Remove(IReadOnlyList<string> keys, string key) => [.. keys.Where(item => item != key)];

    /// <summary>The keys with <paramref name="key"/> at the end, or as they are when it is already in them.</summary>
    public static List<string> Add(IReadOnlyList<string> keys, string key) => keys.Contains(key) ? [.. keys] : [.. keys, key];

    /// <summary>Writes the order. A list the file already has is left, so a read does not write it back.</summary>
    public static void Save(QuickSettingsLayout layout, IReadOnlyList<string> keys)
    {
        if (layout.Pinned is { } current && current.Count == keys.Count && current.Zip(keys).All(pair => Equals(pair.First, pair.Second)))
            return;
        layout.Pinned = [.. keys.Cast<object?>()];
    }
}
