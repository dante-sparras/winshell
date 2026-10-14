namespace Winshell.Core;

/// <summary>What a plugin can be. One plugin may declare several.</summary>
public enum PluginKind
{
    /// <summary>A component the bar places in a section.</summary>
    BarWidget,

    /// <summary>A popup anchored to a widget.</summary>
    Panel,

    /// <summary>A fullscreen surface.</summary>
    Overlay,

    /// <summary>A summoned menu.</summary>
    Menu,

    /// <summary>A headless singleton.</summary>
    Service,

    /// <summary>A full bar that replaces the built-in one.</summary>
    Bar,
}

/// <summary>The words <c>manifest.json</c> and <c>winoma plugin list</c> use for each <see cref="PluginKind"/>.</summary>
internal static class PluginKindNames
{
    /// <summary>Reads a manifest word.</summary>
    /// <param name="text">A <c>kinds</c> entry.</param>
    /// <param name="kind">The kind, or the default when the word is unknown.</param>
    /// <returns>False for an unknown word.</returns>
    public static bool TryParse(string? text, out PluginKind kind)
    {
        kind = text switch
        {
            "bar-widget" => PluginKind.BarWidget,
            "panel" => PluginKind.Panel,
            "overlay" => PluginKind.Overlay,
            "menu" => PluginKind.Menu,
            "service" => PluginKind.Service,
            "bar" => PluginKind.Bar,
            _ => default,
        };
        return text is "bar-widget" or "panel" or "overlay" or "menu" or "service" or "bar";
    }

    /// <summary>The manifest word for <paramref name="kind"/>.</summary>
    /// <param name="kind">A kind.</param>
    /// <returns>The word. An undefined value reads as <c>bar-widget</c>.</returns>
    public static string Name(PluginKind kind) => kind switch
    {
        PluginKind.BarWidget => "bar-widget",
        PluginKind.Panel => "panel",
        PluginKind.Overlay => "overlay",
        PluginKind.Menu => "menu",
        PluginKind.Service => "service",
        PluginKind.Bar => "bar",
        _ => "bar-widget",
    };
}
