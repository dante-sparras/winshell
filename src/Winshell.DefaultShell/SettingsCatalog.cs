using System.Globalization;
using Windows.UI;

namespace Winshell.DefaultShell;

/// <summary>One page of Winshell Settings.</summary>
public sealed record SettingsPage(string Key, string Title, string Glyph);

/// <summary>A page under a page. The header shows it as a breadcrumb.</summary>
public sealed record SettingsSubpage(string Key, string Page, string Title);

/// <summary>One setting: the card, and the words search matches.</summary>
public sealed record SettingsEntry(
    string Key,
    string Page,
    string Glyph,
    string Title,
    string Description,
    string Keywords,
    string Uri,
    string Subpage);

/// <summary>One Windows accent color, with the name a screen reader reads.</summary>
public sealed record SettingsAccent(string Color, string Name);

/// <summary>A value a combo box offers.</summary>
public readonly record struct SettingsOption(string Value, string Label);

/// <summary>One step of the breadcrumb. A link goes back to that page.</summary>
public sealed record SettingsCrumb(string Key, string Title, bool Link);

/// <summary>
/// The pages and settings of Winshell Settings, and the search over them. The text is the card a page shows, so
/// search finds the words on the card.
/// </summary>
public static class SettingsCatalog
{
    public const double PaneWidth = 280;
    public const double ContentMaxWidth = 1000;
    public const double WindowWidth = 1000;
    public const double WindowHeight = 700;
    public const double MinimumWidth = 680;
    public const double MinimumHeight = 480;

    public static IReadOnlyList<SettingsPage> Pages { get; } =
    [
        new("personalization", "Personalization", "\uE771"),
        new("taskbar", "Taskbar", "\uE7C4"),
        new("start", "Start", "\uE80F"),
        new("quickSettings", "Quick settings", "\uE9E9"),
        new("notifications", "Notifications", "\uEA8F"),
        new("hotkeys", "Hotkeys", "\uE765"),
        new("startup", "Startup", "\uE7E8"),
        new("about", "About", "\uE946"),
    ];

    public static IReadOnlyList<SettingsSubpage> Subpages { get; } =
    [
        new("pins", "start", "Pinned apps"),
    ];

    public static IReadOnlyList<SettingsEntry> Settings { get; } =
    [
        Entry("colorScheme", "personalization", "\uE706", "Choose your mode", "Light or dark for the taskbar, Start, flyouts and this window", "dark light theme color colour"),
        Entry("accentColor", "personalization", "\uE790", "Accent color", "The color of buttons, switches and highlights", "accent colour highlight theme"),
        Entry("material", "personalization", "\uE81E", "Material", "What shows through the taskbar and flyouts", "mica acrylic transparency backdrop blur"),
        Entry("windowsColors", "personalization", "\uE771", "Colors in Windows", "Mode, accent color and transparency effects for Windows itself", "transparency windows", "ms-settings:colors"),
        Entry("taskbarAlignment", "taskbar", "\uE8E3", "Taskbar alignment", "Where Start and the apps sit", "center centre left middle"),
        Entry("taskbarAutoHide", "taskbar", "\uE890", "Automatically hide the taskbar", "It comes back when the pointer reaches the bottom of the screen", "auto-hide autohide"),
        Entry("taskbarAllScreens", "taskbar", "\uE7F4", "Show my taskbar on all displays", "Off shows it on the main display only", "monitor screen multiple displays"),
        Entry("taskbarAppsOn", "taskbar", "\uE7F4", "When using multiple displays, show my taskbar apps on", "", "monitor screen multiple displays apps"),
        Entry("trayIcons", "taskbar", "\uE712", "Other system tray icons", "Windows chooses which icons show on the taskbar and which are hidden", "notification area tray hidden icons overflow", "ms-settings:taskbar"),
        Entry("startRecommended", "start", "\uE81C", "Show recommended apps", "Apps you used lately, under your pins", "recent recently used"),
        Entry("startPins", "start", "\uE718", "Pinned apps", "Change their order, or unpin them", "pins pin unpin order", subpage: "pins"),
        Entry("quickSettingsTiles", "quickSettings", "\uE9E9", "Tiles", "Which tiles quick settings shows, in order. A tile this PC has no hardware for stays hidden.", "wifi bluetooth airplane energy saver night light accessibility tiles order"),
        Entry("doNotDisturb", "notifications", "\uE708", "Do not disturb", "Windows turns it on and off", "dnd focus quiet hours silence", "ms-settings:notifications"),
        Entry("notificationApps", "notifications", "\uEA8F", "Notifications from apps", "Turn an app off to leave its notifications out of the notification center. Windows still decides whether it shows banners.", "per app apps senders notification center"),
        Entry("hotkeyStart", "hotkeys", "\uE80F", "Open Start", "", "keyboard shortcut windows key win"),
        Entry("hotkeySettings", "hotkeys", "\uE713", "Open Winshell Settings", "", "keyboard shortcut"),
        Entry("runAtSignIn", "startup", "\uE7E8", "Run Winshell when I sign in", "For your account only", "startup start automatically login logon boot"),
        Entry("startupApps", "startup", "\uE7E8", "Startup apps in Windows", "Windows can turn off an app that runs at sign-in", "startup apps", "ms-settings:startupapps"),
        Entry("version", "about", "\uE946", "Version", "", "about release"),
        Entry("winshellUpdates", "about", "\uE895", "Winshell updates", "", "update upgrade new version download restart check"),
        Entry("autoUpdate", "about", "\uE896", "Get Winshell updates automatically", "Download a new version in the background. It installs when Winshell next starts.", "update upgrade automatic download"),
        Entry("configFolder", "about", "\uE8B7", "Config folder", "", "configuration folder appdata files preferences"),
        Entry("log", "about", "\uE9F9", "Log", "What this instance of Winshell reported", "logs log file errors debug"),
        Entry("diagnostics", "about", "\uE9D9", "Diagnostics", "Check how this PC is set up for Winshell, as winshell doctor does", "doctor troubleshoot problems check setup crash reports"),
    ];

    public static IReadOnlyList<SettingsAccent> Accents { get; } =
    [
        new("#FFB900", "Yellow gold"), new("#FF8C00", "Gold"), new("#F7630C", "Orange bright"), new("#CA5010", "Orange dark"), new("#DA3B01", "Rust"), new("#EF6950", "Pale rust"), new("#D13438", "Brick red"), new("#FF4343", "Mod red"),
        new("#E74856", "Pale red"), new("#E81123", "Red"), new("#EA005E", "Rose bright"), new("#C30052", "Rose"), new("#E3008C", "Plum light"), new("#BF0077", "Plum"), new("#C239B3", "Orchid light"), new("#9A0089", "Orchid"),
        new("#0078D4", "Default blue"), new("#0063B1", "Navy blue"), new("#8E8CD8", "Purple shadow"), new("#6B69D6", "Purple shadow dark"), new("#8764B8", "Iris pastel"), new("#744DA9", "Iris spring"), new("#B146C2", "Violet red light"), new("#881798", "Violet red"),
        new("#0099BC", "Cool blue bright"), new("#2D7D9A", "Cool blue"), new("#00B7C3", "Seafoam"), new("#038387", "Seafoam teal"), new("#00B294", "Mint light"), new("#018574", "Mint dark"), new("#00CC6A", "Turf green"), new("#10893E", "Sport green"),
        new("#7A7574", "Gray"), new("#5D5A58", "Gray brown"), new("#68768A", "Steel blue"), new("#515C6B", "Metal blue"), new("#567C73", "Pale moss"), new("#486860", "Moss"), new("#498205", "Meadow green"), new("#107C10", "Green"),
        new("#767676", "Overcast"), new("#4C4A48", "Storm"), new("#69797E", "Blue gray"), new("#4A5459", "Gray dark"), new("#647C64", "Liddy green"), new("#525E54", "Sage"), new("#847545", "Camouflage desert"), new("#7E735F", "Camouflage"),
    ];

    public static IReadOnlyList<SettingsOption> ColorSchemes { get; } =
    [
        new("system", "Same as Windows"),
        new("light", "Light"),
        new("dark", "Dark"),
    ];

    public static IReadOnlyList<SettingsOption> AccentChoices { get; } =
    [
        new("windows", "Same as Windows"),
        new("manual", "Manual"),
    ];

    public static IReadOnlyList<SettingsOption> Materials { get; } =
    [
        new("acrylic", "Acrylic"),
        new("mica", "Mica"),
    ];

    public static IReadOnlyList<SettingsOption> Alignments { get; } =
    [
        new("center", "Center"),
        new("left", "Left"),
    ];

    public static IReadOnlyList<SettingsOption> TaskbarApps { get; } =
    [
        new("all", "All taskbars"),
        new("main", "Main taskbar and taskbar where window is open"),
        new("where", "Taskbar where window is open"),
    ];

    public static SettingsPage? FindPage(string? key) => Pages.FirstOrDefault(page => page.Key == key);

    public static SettingsEntry? FindSetting(string? key) => Settings.FirstOrDefault(setting => setting.Key == key);

    public static SettingsSubpage? FindSubpage(string? key) => Subpages.FirstOrDefault(page => page.Key == key);

    public static SettingsPage RequirePage(string key) => FindPage(key) ?? throw new InvalidOperationException($"Winshell Settings has no page \"{key}\".");

    public static SettingsEntry RequireSetting(string key) => FindSetting(key) ?? throw new InvalidOperationException($"Winshell Settings has no setting \"{key}\".");

    /// <summary>Why <paramref name="key"/> is neither a page nor a setting, or null when it is one, or when it is empty.</summary>
    public static string? Unknown(string? key)
    {
        if (string.IsNullOrEmpty(key) || FindPage(key) is not null || FindSetting(key) is not null)
            return null;
        return $"Winshell Settings has no page or setting named \"{key}\"";
    }

    /// <summary>The index of <paramref name="value"/>, or 0 when the list has no such value.</summary>
    public static int IndexOf(IReadOnlyList<SettingsOption> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Value == value)
                return i;
        }
        return 0;
    }

    /// <summary>
    /// The accent a manual choice starts from: the accent in use, uppercased, or the default blue when that accent is
    /// not one Winshell can store. Anything other than <c>manual</c> follows Windows.
    /// </summary>
    public static string ChooseAccent(string choice, string? systemAccent) =>
        choice == "manual" ? IsAccent(systemAccent) ? systemAccent!.ToUpperInvariant() : "#0078D4" : "";

    public static bool IsAccent(string? text)
    {
        if (text is not { Length: 7 } || text[0] != '#')
            return false;
        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
                return false;
        }
        return true;
    }

    /// <summary>Whether a check drawn on <paramref name="hex"/> is black.</summary>
    public static bool LightGlyph(string hex)
    {
        if (!IsAccent(hex))
            return false;
        var r = byte.Parse(hex.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255d;
        var g = byte.Parse(hex.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255d;
        var b = byte.Parse(hex.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255d;
        return (Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b))) / 2 > 0.6;
    }

    public static Color AccentColor(string hex)
    {
        var r = byte.Parse(hex.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return Color.FromArgb(255, r, g, b);
    }

    /// <summary>How many accent swatches fit the width of the swatch card. Eight is the wide page.</summary>
    public static int SwatchColumns(double width)
    {
        var room = Math.Max(44, width - 68);
        return Math.Max(1, Math.Min(8, (int)Math.Floor((room + 4) / 44)));
    }

    /// <summary>What a search result says under its title: the page, then the setting's own description.</summary>
    public static string ResultText(SettingsEntry setting)
    {
        var page = FindPage(setting.Page)?.Title ?? "";
        return setting.Description.Length == 0 ? page : $"{page} \u2022 {setting.Description}";
    }

    public static string NoResults(string? query) => $"No results for \u201c{(query ?? "").Trim()}\u201d";

    /// <summary>
    /// Every word of <paramref name="query"/> must be in the title, description, keywords or page title. A title that
    /// starts with the query comes first, then a title that contains it, then the catalog order.
    /// </summary>
    public static IReadOnlyList<SettingsEntry> Search(string? query)
    {
        var words = Words(query);
        if (words.Count == 0)
            return [];
        var whole = string.Join(' ', words);
        var hits = new List<(SettingsEntry Setting, int Rank, int Order)>();
        for (var i = 0; i < Settings.Count; i++)
        {
            var setting = Settings[i];
            var page = FindPage(setting.Page)?.Title ?? Pages[0].Title;
            var text = $"{setting.Title} {setting.Description} {setting.Keywords} {page}".ToLowerInvariant();
            if (!words.All(word => text.Contains(word, StringComparison.Ordinal)))
                continue;
            var title = setting.Title.ToLowerInvariant();
            var rank = title.StartsWith(whole, StringComparison.Ordinal) ? 0 : title.Contains(whole, StringComparison.Ordinal) ? 1 : 2;
            hits.Add((setting, rank, i));
        }
        return hits.OrderBy(hit => hit.Rank).ThenBy(hit => hit.Order).Select(hit => hit.Setting).ToList();
    }

    private static List<string> Words(string? query)
    {
        var words = new List<string>();
        var text = (query ?? "").ToLowerInvariant();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var boundary = i == text.Length || char.IsWhiteSpace(text[i]);
            if (!boundary && start < 0)
                start = i;
            if (boundary && start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }
        return words;
    }

    private static SettingsEntry Entry(string key, string page, string glyph, string title, string description, string keywords, string uri = "", string subpage = "") =>
        new(key, page, glyph, title, description, keywords, uri, subpage);
}
