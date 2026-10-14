namespace Winshell.Core;

/// <summary>One entry in a bar section.</summary>
public sealed class BarSlot
{
    /// <summary>Plugin id, or a command-widget id.</summary>
    public string Id { get; set; } = "";

    /// <summary><c>command</c> for a script widget. Empty for a plugin.</summary>
    public string Type { get; set; } = "";

    /// <summary>Program and arguments for a script widget.</summary>
    public string Exec { get; set; } = "";

    /// <summary>Seconds between script runs. Zero means five.</summary>
    public int Interval { get; set; }

    /// <summary>Program run on left click of a script widget.</summary>
    public string OnClick { get; set; } = "";

    /// <summary>The clock's formats, in the order a right click steps through them. The first is shown at start. Empty means the clock's shipped formats.</summary>
    public List<string> Formats { get; set; } = [];

    /// <summary>Clock format used on a vertical bar.</summary>
    public string VerticalFormat { get; set; } = "";

    /// <summary>Values named by the plugin's <c>barWidget.schema</c>.</summary>
    public Dictionary<string, string> Settings
    {
        get => _settings ??= [];
        set => _settings = value ?? [];
    }

    private Dictionary<string, string>? _settings = [];

    /// <summary>True when this slot is a script widget.</summary>
    public bool IsCommand => Type.Equals("command", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The three bar sections.</summary>
public sealed class BarSections
{
    /// <summary>Widgets on the left.</summary>
    public List<BarSlot> Left { get; set; } = [];

    /// <summary>Widgets in the center.</summary>
    public List<BarSlot> Center { get; set; } = [];

    /// <summary>Widgets on the right.</summary>
    public List<BarSlot> Right { get; set; } = [];
}

/// <summary>The <c>bar</c> object in <c>settings.json</c>. A missing object means the default layout.</summary>
public sealed class BarLayoutDocument
{
    /// <summary>The center widget pinned to the middle of the screen. Empty centers the center group.</summary>
    public string CenterAnchor { get; set; } = "";

    /// <summary>True when the Bar is drawn on acrylic, so the wallpaper shows through it. Off when the key is missing.</summary>
    public bool Transparent { get; set; }

    /// <summary>Built-in plugins that stay off.</summary>
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>Plugins from the config folder that should run.</summary>
    public List<string> Plugins { get; set; } = [];

    /// <summary>Section lists. Null is an empty bar when this document itself is present.</summary>
    public BarSections? Layout { get; set; }
}
