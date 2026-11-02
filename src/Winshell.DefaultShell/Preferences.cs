using System.Text.RegularExpressions;

namespace Winshell.DefaultShell;

/// <summary>
/// The choices made in Winshell Settings, kept in <c>preferences.toml</c>. Every surface reads them here and follows a
/// change at once, including one made to the file by hand. A key the file does not have keeps its default. A value
/// Winshell Settings would not offer is reported and left in the file, and the one in use stays.
/// </summary>
public sealed partial class Preferences(string path, IEventLoop loop, Log? log = null) : TomlFile(path, loop, log, Check)
{
    [TomlComment("\"system\" follows Windows; or \"light\" or \"dark\".")]
    public string ColorScheme { get; set => Set(ref field, value); } = "system";

    [TomlComment("\"\" for the accent color picked in Windows, or \"#rrggbb\".")]
    public string AccentColor { get; set => Set(ref field, value); } = "";

    [TomlComment("What shows through the taskbar and the flyouts: \"acrylic\" or \"mica\".")]
    public string Backdrop { get; set => Set(ref field, value); } = "acrylic";

    [TomlComment("true centers the taskbar's apps; false puts them on the left.")]
    public bool TaskbarCentered { get; set => Set(ref field, value); } = true;

    [TomlComment("true hides the taskbar until the pointer reaches it.")]
    public bool TaskbarAutoHide { get; set => Set(ref field, value); }

    [TomlComment("true shows a taskbar on every display; false on the main display only.")]
    public bool TaskbarAllScreens { get; set => Set(ref field, value); } = true;

    [TomlComment("With several displays: \"all\" shows every app on every taskbar, \"main\" every app on the main\ntaskbar and each display's own windows on the others, \"where\" each window on its display's taskbar only.")]
    public string TaskbarAppsOn { get; set => Set(ref field, value); } = "all";

    [TomlComment("true shows recommended apps in Start under the pins.")]
    public bool StartRecommended { get; set => Set(ref field, value); } = true;

    [TomlComment("true shows week numbers beside the calendar.")]
    public bool CalendarWeekNumbers { get; set => Set(ref field, value); }

    [TomlComment("true shows seconds on the taskbar clock.")]
    public bool ClockSeconds { get; set => Set(ref field, value); }

    [TomlComment("true lets Winshell take Alt+Tab, Win+Tab, Win+D and Win+X. false leaves them with Windows.")]
    public bool DesktopChords { get; set => Set(ref field, value); }

    [TomlComment("true hides Explorer's taskbar and releases the screen edge it reserved, until Winshell exits.\nfalse leaves it alone. There is no switch for this in Winshell Settings.")]
    public bool HideExplorerTaskbar { get; set => Set(ref field, value); }

    [TomlComment("The apps the notification center leaves out, as { id = \"<AppUserModelID>\", name = \"<app name>\" }.")]
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> HiddenNotificationApps { get; set => Set(ref field, value); } = [];

    [TomlComment("The hotkey that opens Start, such as \"Win\" or \"Ctrl+Alt+S\"; \"\" turns it off.")]
    public string StartHotkey { get; set => Set(ref field, value); } = "Win";

    [TomlComment("The hotkey that opens Winshell Settings; \"\" turns it off.")]
    public string SettingsHotkey { get; set => Set(ref field, value); } = "";

    [TomlComment("true checks for, downloads and installs Winshell updates. Only a portable Winshell updates itself.")]
    public bool AutoUpdate { get; set => Set(ref field, value); } = true;

    /// <summary>The material the taskbar and the flyouts use.</summary>
    public WindowBackdrop Material => Backdrop == "mica" ? WindowBackdrop.Mica : WindowBackdrop.Acrylic;

    /// <summary>Whether <see cref="AccentColor"/> is one Winshell can use.</summary>
    public bool ValidAccent => AccentPattern().IsMatch(AccentColor);

    /// <summary><see cref="TaskbarAppsOn"/>, or <c>all</c> when the file has one Winshell cannot use.</summary>
    public string AppsOn => TaskbarAppsOn is "all" or "main" or "where" ? TaskbarAppsOn : "all";

    /// <summary><see cref="ColorScheme"/>, or <c>system</c> when the file has one Winshell cannot use.</summary>
    public string ValidColorScheme => ColorScheme is "system" or "light" or "dark" ? ColorScheme : "system";

    /// <summary>The hidden apps that have an id.</summary>
    public IEnumerable<IReadOnlyDictionary<string, object?>> HiddenApps => HiddenNotificationApps.Where(app => app.TryGetValue("id", out var id) && id is string);

    /// <summary>
    /// Why a hotkey sequence is not taken, or <c>""</c>. A first chord without Win, Ctrl or Alt would take that key from
    /// every app on the PC: Shift alone is still typing.
    /// </summary>
    public static string HotkeyRefusal(string text)
    {
        if (text.Length == 0)
            return "";
        var first = text.Split(',')[0].Split('+').Select(p => p.Trim().ToLowerInvariant());
        return first.Any(p => p is "win" or "left win" or "right win" or "ctrl" or "alt")
            ? ""
            : $"Start it with Win, Ctrl or Alt, so {text} still types in other apps";
    }

    public bool ShowsNotificationsFrom(string appId) => !HiddenApps.Any(app => Equals(app["id"], appId));

    public void SetNotificationsFrom(string appId, string appName, bool shown)
    {
        var rest = HiddenApps.Where(app => !Equals(app["id"], appId)).ToList();
        if (!shown)
            rest.Add(new Dictionary<string, object?> { ["id"] = appId, ["name"] = appName });
        HiddenNotificationApps = rest;
    }

    private static string Check(string key, object value)
    {
        switch (key)
        {
            case "backdrop":
                return value is "acrylic" or "mica" ? "" : "is not \"acrylic\" or \"mica\"";
            case "startHotkey" or "settingsHotkey":
                return value is string text && HotkeyRefusal(text) is { Length: > 0 } why ? $"is refused: {why}" : "";
            default:
                return "";
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentPattern();
}
