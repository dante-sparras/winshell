namespace Winshell.DefaultShell.Taskbar;

/// <summary>One row or tile in Start. The view binds <see cref="IsSelected"/>, so a move does not rebuild the list.</summary>
public sealed class StartEntry : ObservableObject
{
    internal StartEntry(string kind, string name, string section, string glyph, string icon, string uri, string header, StartApp? app)
    {
        Kind = kind;
        Name = name;
        Section = section;
        Glyph = glyph;
        Icon = icon;
        Uri = uri;
        Header = header;
        App = app;
    }

    public string Kind { get; }

    public string Name { get; }

    /// <summary>Where the row is, for assistive technology: Pinned, Recommended, All apps, Search, Settings or Files.</summary>
    public string Section { get; }

    public string Glyph { get; }

    public string Icon { get; }

    public string Uri { get; }

    /// <summary>The group heading on the first row of a search group, or empty.</summary>
    public string Header { get; }

    public bool Leads => Header.Length > 0;

    public StartApp? App { get; }

    public bool IsSelected { get; internal set => Set(ref field, value); }

    /// <summary>What a list item is called in UI Automation.</summary>
    public override string ToString() => Name;
}
