namespace Winshell.DefaultShell.Taskbar;

/// <summary>One tile. A tile the PC cannot show stays out of <see cref="QuickSettingsPins.Shown"/> and keeps its place.</summary>
public sealed class QuickSettingsTile : ObservableObject
{
    internal QuickSettingsTile(string key, string title, string glyph, string description, bool toggle, string settingsUri, string page)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        Label = title;
        Description = description;
        Toggle = toggle;
        SettingsUri = settingsUri;
        Page = page;
    }

    public string Key { get; }

    /// <summary>The tile's name. The label under it can show more, such as the network it is on.</summary>
    public string Title { get; }

    public string Glyph { get; }

    public string SettingsUri { get; }

    /// <summary>The page this tile opens, or empty when it opens Settings instead.</summary>
    public string Page { get; }

    public bool Toggle { get; }

    public bool HasPage => Page.Length > 0;

    public string Label { get; internal set => Set(ref field, value); }

    public string Description { get; internal set => Set(ref field, value); }

    public bool On { get; internal set => Set(ref field, value); }

    public bool Available { get; internal set => Set(ref field, value); } = true;
}
