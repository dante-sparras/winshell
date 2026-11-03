using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The tile order <c>quick-settings.toml</c> saves, as the tiles the PC can show. A pinned tile the PC cannot show keeps
/// its place in the file.
/// </summary>
public sealed class QuickSettingsPins : ObservableObject
{
    private static readonly string[] DefaultKeys = [.. QuickSettingsKind.All.Select(static kind => kind.Key)];

    private readonly IReadOnlyList<QuickSettingsTile> _tiles;
    private readonly QuickSettingsLayout _layout;

    internal QuickSettingsPins(IReadOnlyList<QuickSettingsTile> tiles, QuickSettingsLayout layout)
    {
        _tiles = tiles;
        _layout = layout;
        layout.PropertyChanged += OnLayout;
        Refresh();
    }

    /// <summary>The pinned tiles this PC can show, in the saved order.</summary>
    public ObservableCollection<QuickSettingsTile> Shown { get; } = [];

    /// <summary>Tiles this PC can show that are not pinned, in the tile list's order.</summary>
    public ObservableCollection<QuickSettingsTile> Unpinned { get; } = [];

    /// <summary>Whether <see cref="Unpinned"/> has a tile the add page can put back.</summary>
    public bool HasUnpinned => Unpinned.Count > 0;

    /// <summary>Puts the tile with this key where the tile with another key is.</summary>
    public void Move(string key, string target)
    {
        var pinned = PinnedKeys();
        var at = pinned.IndexOf(target);
        // Before the target when moving back, after it when moving on: the index is taken before the key is removed.
        if (key == target || at < 0)
            return;
        var keys = pinned.Where(item => item != key).ToList();
        keys.Insert(at, key);
        SetPinned(keys);
    }

    /// <summary>Moves a tile by an offset among the tiles on show, and returns the key the keyboard stays on.</summary>
    public string MoveBy(string key, int offset)
    {
        var shown = Shown.Select(tile => tile.Key).ToList();
        var from = shown.IndexOf(key);
        var to = Math.Max(0, Math.Min(shown.Count - 1, from + offset));
        Move(key, shown[to]);
        return key;
    }

    /// <summary>Unpins a tile. From the keyboard, the result is the key that takes its place.</summary>
    public string? Unpin(string key, bool fromKeyboard)
    {
        var at = Shown.Select(tile => tile.Key).ToList().IndexOf(key);
        SetPinned(PinnedKeys().Where(item => item != key).ToList());
        if (!fromKeyboard || Shown.Count == 0)
            return null;
        return Shown[Math.Min(at, Shown.Count - 1)].Key;
    }

    /// <summary>Pins a tile at the end of the order.</summary>
    public void Add(string key) => SetPinned([.. PinnedKeys(), key]);

    /// <summary>Lists the tiles again, as which of them the PC can show may have changed.</summary>
    internal void Refresh()
    {
        var keys = PinnedKeys();
        var shown = keys
            .Select(key => _tiles.FirstOrDefault(tile => tile.Key == key))
            .OfType<QuickSettingsTile>()
            .Where(tile => tile.Available)
            .ToList();
        Replace(Shown, shown);
        var hadUnpinned = HasUnpinned;
        var unpinned = _tiles.Where(tile => tile.Available && !keys.Contains(tile.Key)).ToList();
        Replace(Unpinned, unpinned);
        if (hadUnpinned != HasUnpinned)
            Changed(nameof(HasUnpinned));
    }

    private void OnLayout(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsLayout.Pinned))
            Refresh();
    }

    private List<string> PinnedKeys() =>
        _layout.Pinned is { } pinned ? pinned.OfType<string>().ToList() : [.. DefaultKeys];

    /// <summary>Writes the order. A list the file already has is left, so a read does not write it back.</summary>
    private void SetPinned(IReadOnlyList<string> keys)
    {
        if (_layout.Pinned is { } current && current.Count == keys.Count && current.Zip(keys).All(pair => Equals(pair.First, pair.Second)))
            return;
        _layout.Pinned = [.. keys.Cast<object?>()];
    }

    private static void Replace<T>(ObservableCollection<T> target, List<T> next)
    {
        if (target.SequenceEqual(next))
            return;
        target.Clear();
        foreach (var item in next)
            target.Add(item);
    }
}
