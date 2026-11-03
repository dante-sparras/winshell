using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The quick settings tiles Winshell Settings lists. A key the shell does not know stays in the file and is not a row.
/// A missing list is the default order. An empty list is a choice to show no tiles.
/// </summary>
public static class SettingsTiles
{
    public static IReadOnlyList<string> Keys(IReadOnlyList<object?>? pinned)
    {
        if (pinned is null)
        {
            var defaults = new List<string>(QuickSettingsKind.All.Count);
            foreach (var kind in QuickSettingsKind.All)
                defaults.Add(kind.Key);
            return defaults;
        }
        var keys = new List<string>(pinned.Count);
        foreach (var item in pinned)
        {
            if (item is string key)
                keys.Add(key);
        }
        return keys;
    }

    public static IReadOnlyList<QuickSettingsKind> Shown(IReadOnlyList<string> keys)
    {
        var shown = new List<QuickSettingsKind>();
        foreach (var key in keys)
        {
            foreach (var kind in QuickSettingsKind.All)
            {
                if (kind.Key == key)
                {
                    shown.Add(kind);
                    break;
                }
            }
        }
        return shown;
    }

    public static IReadOnlyList<QuickSettingsKind> Unpinned(IReadOnlyList<string> keys)
    {
        var list = new List<QuickSettingsKind>();
        foreach (var kind in QuickSettingsKind.All)
        {
            if (!Contains(keys, kind.Key))
                list.Add(kind);
        }
        return list;
    }

    public static IReadOnlyList<string>? Move(IReadOnlyList<string> keys, string key, int offset) =>
        SettingsOrder.Move(keys, KeysOf(Shown(keys)), key, offset, StringComparison.Ordinal);

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> keys, string key) =>
        SettingsOrder.Remove(keys, key, StringComparison.Ordinal);

    public static IReadOnlyList<string> Add(IReadOnlyList<string> keys, string key)
    {
        if (Contains(keys, key))
            return keys;
        var list = new List<string>(keys.Count + 1);
        list.AddRange(keys);
        list.Add(key);
        return list;
    }

    private static bool Contains(IReadOnlyList<string> keys, string key)
    {
        foreach (var item in keys)
        {
            if (item == key)
                return true;
        }
        return false;
    }

    private static string[] KeysOf(IReadOnlyList<QuickSettingsKind> kinds)
    {
        var keys = new string[kinds.Count];
        for (var i = 0; i < kinds.Count; i++)
            keys[i] = kinds[i].Key;
        return keys;
    }
}
