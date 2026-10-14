using System.Globalization;

namespace Winshell.Kit;

/// <summary>
/// Segoe Fluent glyphs by name, so a plugin does not spell out code points. A <see cref="Winshell.Core.BarFace"/> names its
/// glyph with one of <see cref="Named"/>'s words, and a panel uses the constants.
/// </summary>
public static class Glyphs
{
    /// <summary>A speaker.</summary>
    public const string Volume = "";

    /// <summary>A muted speaker.</summary>
    public const string Mute = "";

    /// <summary>A microphone.</summary>
    public const string Mic = "";

    /// <summary>A map pin.</summary>
    public const string Pin = "";

    /// <summary>A pushpin, for an item kept in place.</summary>
    public const string Pinned = "";

    /// <summary>An eye struck through, for an item hidden.</summary>
    public const string Hide = "";

    /// <summary>Wi-Fi.</summary>
    public const string Wifi = "";

    /// <summary>A gear.</summary>
    public const string Settings = "";

    /// <summary>An i in a circle.</summary>
    public const string Info = "";

    /// <summary>A chevron pointing up, for an earlier item.</summary>
    public const string Up = "";

    /// <summary>A chevron pointing down, for a later item.</summary>
    public const string Down = "";

    /// <summary>A chevron pointing left, for a step back.</summary>
    public const string Back = "";

    /// <summary>A chevron pointing right, for a step forward.</summary>
    public const string Forward = "";

    /// <summary>A check mark.</summary>
    public const string Check = "";

    /// <summary>A cross.</summary>
    public const string Cross = "";

    /// <summary>A full battery.</summary>
    public const string Battery = "";

    /// <summary>The power button.</summary>
    public const string Power = "";

    /// <summary>A leaf, for power saver.</summary>
    public const string Saver = "";

    /// <summary>A gauge at its middle, for balanced.</summary>
    public const string Balanced = "";

    /// <summary>A gauge at its top, for performance.</summary>
    public const string Performance = "";

    /// <summary>A moon, for sleep.</summary>
    public const string Sleep = "";

    /// <summary>A circular arrow, for restart.</summary>
    public const string Restart = "";

    /// <summary>The Kit gallery's own glyph.</summary>
    public const string Gallery = "";

    /// <summary>
    /// The glyph a face or chip names: <c>volume</c>, <c>wifi</c>, <c>battery-7</c>, <c>battery-charging-3</c>, and the rest.
    /// Empty when the name has no glyph.
    /// </summary>
    /// <param name="name">A glyph name.</param>
    /// <returns>The glyph, or empty.</returns>
    public static string Named(string name) => name switch
    {
        "share" => "",
        "mic" => Mic,
        "mic-off" => "\uEC54",
        "pin" => Pin,
        "pinned" => Pinned,
        "hide" => Hide,
        "keyboard" => "",
        "wifi" => Wifi,
        "wifi-off" => Wifi,
        "ethernet" => "",
        "volume" => Volume,
        "mute" => Mute,
        "brightness" => "",
        "cloud" => "",
        "power" => Power,
        "tray" => "",
        "bell" => "",
        "bell-off" => "",
        "update" => "",
        "agents" => "",
        "cpu" => "",
        "graphics" => "",
        "video-memory" => "",
        "disk" => "",
        "chart" => "",
        "list" => "",
        "wrench" => "",
        "stop" => "",
        "ram" => "",
        "battery" => Battery,
        "profiles" => Performance,
        "settings" => Settings,
        "info" => Info,
        "saver" => Saver,
        "balanced" => Balanced,
        "performance" => Performance,
        "sleep" => Sleep,
        "restart" => Restart,
        "shutdown" => Power,
        "check" => Check,
        "cross" => Cross,
        "forward" => Forward,

        // The names a Menu entry uses (MenuGlyphs in Core). A test fails when one is missing here.
        "search" => "",
        "app" => "",
        "window" => "",
        "command" => "",
        "panel" => "",
        "plugin" => "",
        "file" => "",
        "folder" => "",
        "calculator" => "",
        "clipboard" => "",
        "link" => "",
        "globe" => "",
        "emoji" => "",
        "snippet" => "",
        "terminal" => "",
        "lock" => "",
        "signout" => "",
        "display" => "",
        "play" => "",
        "next" => "",
        "previous" => "",
        "trash" => "",
        "appearance" => "",
        "camera" => "",
        "record" => "",
        "color" => "",
        "text" => "",
        "image" => "",
        "copy" => "",
        "paste" => "",
        "star" => "",
        "clock" => "",
        "timer" => "",
        "workspace" =>"",

        // The Indicators the built-ins keep: the night light mark, and the cup for a Host that will not lock by itself.
        "night-light" => "\uF08C",
        "bluetooth" => "\uE702",
        "coffee" => "\uEC32",
        "shield" => "",
        _ when name.StartsWith("battery-charging-", StringComparison.Ordinal) => Level(name["battery-charging-".Length..], 0xEBAB),
        _ when name.StartsWith("battery-", StringComparison.Ordinal) => Level(name["battery-".Length..], 0xEBA0),
        _ => "",
    };

    /// <summary>A battery glyph for a tenth from 0 to 10. The empty, then charging, ranges run up from <paramref name="first"/>.</summary>
    private static string Level(string tenth, int first) =>
        int.TryParse(tenth, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? char.ConvertFromUtf32(first + Math.Clamp(level, 0, 10))
            : Power;
}
