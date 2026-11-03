using Windows.Foundation;
using Winshell.Apps;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>One app Start can show. Hidden apps stay in the list and out of the grid.</summary>
public sealed record StartApp(string Id, string Name, string AppId, string Icon, string Target, bool Packaged, bool Hidden = false);

/// <summary>A settings page or a recent document in search.</summary>
public sealed record StartLink(string Kind, string Name, string Uri, string Glyph);

/// <summary>
/// What Start shows, in Windows 11 logical pixels, and the pin, search and keyboard rules. The view uses the sizes.
/// An AppUserModelID matches before a display name, so a non-English Windows still pins the same apps.
/// </summary>
public static class StartList
{
    public const double MenuWidth = 640;
    public const double MenuHeight = 720;
    public const double Margin = 22;
    public const double SearchX = 22;
    public const double SearchY = 16;
    public const double SearchHeight = 32;
    public const int Columns = 6;
    public const double TileWidth = 96;
    public const double TileHeight = 76;
    public const double Gap = 4;
    public const double PinnedViewport = 3 * TileHeight + 2 * Gap;
    public const double RecommendedRow = 48;
    public const int RecommendedLimit = 6;
    public const double AllRow = 40;
    public const int PinnedIcon = 32;
    public const int RowIcon = 32;
    public const int AllIcon = 24;
    public const string FileGlyph = "\uE8A5";

    private readonly record struct Slot(string[] Ids, string[] Names);

    private static readonly Slot[] Preferred =
    [
        new(["MSEdge", "Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App"], ["Edge", "Microsoft Edge"]),
        new(["Microsoft.Windows.Explorer"], ["File Explorer", "Windows Explorer", "Explorer"]),
        new(["windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"], ["Settings"]),
        new(["Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"], ["Notepad"]),
        new(["Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"], ["Calculator"]),
        new(["Microsoft.Paint_8wekyb3d8bbwe!App"], ["Paint"]),
        new(["Microsoft.ScreenSketch_8wekyb3d8bbwe!App"], ["Snipping Tool"]),
        new(["Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"], ["Terminal", "Windows Terminal"]),
        new(["Microsoft.Windows.Photos_8wekyb3d8bbwe!App"], ["Photos", "Microsoft Photos"]),
        new(["Microsoft.WindowsStore_8wekyb3d8bbwe!App"], ["Microsoft Store"]),
        new(["Microsoft.WindowsAlarms_8wekyb3d8bbwe!App"], ["Clock", "Alarms & Clock"]),
    ];

    private readonly record struct Page(string Name, string Uri, string Keywords, string Glyph);

    /// <summary>Settings pages this shell already opens. Start search adds none of its own, and it does not search the web.</summary>
    private static readonly Page[] SettingsPages =
    [
        new("Wi-Fi", "ms-settings:network-wifi", "wifi wi-fi wireless network", "\uE701"),
        new("Bluetooth", "ms-settings:bluetooth", "bluetooth devices", "\uE702"),
        new("Airplane mode", "ms-settings:network-airplanemode", "airplane flight mode", "\uE709"),
        new("Energy saver", "ms-settings:batterysaver", "battery saver power", "\uE863"),
        new("Night light", "ms-settings:nightlight", "night light blue light", "\uF08C"),
        new("Accessibility", "ms-settings:easeofaccess", "accessibility ease of access", "\uE776"),
        new("More volume settings", "ms-settings:apps-volume", "volume sound mixer apps", "\uE767"),
        new("Colors in Windows", "ms-settings:colors", "colors colour transparency windows personalization", "\uE771"),
        new("Other system tray icons", "ms-settings:taskbar", "tray notification area hidden icons overflow", "\uE712"),
        new("Do not disturb", "ms-settings:notifications", "dnd focus quiet hours silence", "\uE708"),
        new("Startup apps in Windows", "ms-settings:startupapps", "startup apps sign-in", "\uE7E8"),
        new("All settings", "ms-settings:", "settings windows", "\uE713"),
    ];

    public static bool SameId(string? a, string? b) =>
        a is not null && b is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    public static bool IsPinned(IReadOnlyList<string> ids, string id)
    {
        foreach (var key in ids)
        {
            if (SameId(key, id))
                return true;
        }
        return false;
    }

    public static IReadOnlyList<StartApp> Visible(IReadOnlyList<StartApp> apps)
    {
        var list = new List<StartApp>(apps.Count);
        foreach (var app in apps)
        {
            if (app is { Hidden: false })
                list.Add(app);
        }
        return list;
    }

    /// <summary>Pinned ids in order. An id with no visible app stays in the file and is not shown.</summary>
    public static IReadOnlyList<StartApp> Pinned(IReadOnlyList<StartApp> apps, IReadOnlyList<string> ids)
    {
        var list = Visible(apps);
        var pinned = new List<StartApp>(ids.Count);
        foreach (var id in ids)
        {
            foreach (var app in list)
            {
                if (!SameId(app.Id, id))
                    continue;
                pinned.Add(app);
                break;
            }
        }
        return pinned;
    }

    public static IReadOnlyList<StartApp> Recommended(IReadOnlyList<StartApp> recent, IReadOnlyList<string> ids, int limit = RecommendedLimit)
    {
        var cap = limit > 0 ? limit : RecommendedLimit;
        var list = new List<StartApp>();
        foreach (var app in Visible(recent))
        {
            if (IsPinned(ids, app.Id))
                continue;
            list.Add(app);
            if (list.Count == cap)
                break;
        }
        return list;
    }

    /// <summary>Preferred apps that are installed, in that order. When none of them are, the first eight visible apps.</summary>
    public static IReadOnlyList<string> DefaultPins(IReadOnlyList<StartApp> apps)
    {
        var list = Visible(apps);
        var ids = new List<string>();
        foreach (var slot in Preferred)
        {
            StartApp? match = null;
            foreach (var app in list)
            {
                if (!slot.Ids.Any(id => SameId(app.Id, id) || SameId(app.AppId, id)))
                    continue;
                match = app;
                break;
            }
            if (match is null)
            {
                foreach (var app in list)
                {
                    if (!slot.Names.Any(name => SameId(app.Name, name)))
                        continue;
                    match = app;
                    break;
                }
            }
            if (match is not null && !IsPinned(ids, match.Id))
                ids.Add(match.Id);
        }
        return ids.Count > 0 ? ids : list.Take(8).Select(app => app.Id).ToList();
    }

    public static IReadOnlyList<string> TogglePin(IReadOnlyList<string> ids, string id) =>
        IsPinned(ids, id) ? ids.Where(key => !SameId(key, id)).ToList() : [.. ids, id];

    /// <summary>A packaged app has no file to show, so its location is the Apps folder. The icon path wins over the target.</summary>
    public static IReadOnlyList<string> LocationCommand(StartApp? app)
    {
        string[] folder = ["explorer.exe", "shell:AppsFolder"];
        if (app is null)
            return folder;
        var path = app.Icon.Length > 0 ? app.Icon : app.Target;
        if (path.Length == 0 || app.Packaged || InFolder(app.Icon) || InFolder(app.Target))
            return folder;
        return ["explorer.exe", "/select," + path];
    }

    /// <summary>
    /// Home is a grid of pins, then one column of recommended rows. Search and All apps are one column, so
    /// <paramref name="pinnedCount"/> is 0 there and left and right do nothing.
    /// </summary>
    public static int Move(int index, string key, int count, int pinnedCount, int columns = Columns)
    {
        if (count <= 0)
            return 0;
        index = Math.Clamp(index, 0, count - 1);
        var cols = columns > 0 ? columns : Columns;
        var inPins = pinnedCount > 0 && index < pinnedCount;
        if (key == "left")
            return inPins && index % cols != 0 ? index - 1 : index;
        if (key == "right")
            return inPins && index % cols != cols - 1 && index + 1 < pinnedCount ? index + 1 : index;
        if (key == "down")
        {
            if (!inPins)
                return Math.Min(count - 1, index + 1);
            if (index + cols < pinnedCount)
                return index + cols;
            // The cell below is empty, but the next row still has pins: land on its first one.
            var nextRow = index / cols * cols + cols;
            if (nextRow < pinnedCount)
                return nextRow;
            return pinnedCount < count ? pinnedCount : index;
        }
        if (key == "up")
        {
            if (pinnedCount > 0 && index == pinnedCount)
                return (pinnedCount - 1) / cols * cols;
            if (inPins)
                return index - cols >= 0 ? index - cols : index;
            return Math.Max(0, index - 1);
        }
        return index;
    }

    /// <summary>Every word must be in the page's name or keywords. A name that starts with the query comes first.</summary>
    public static IReadOnlyList<StartLink> SearchSettings(string query)
    {
        var words = Words(query);
        if (words.Count == 0)
            return [];
        var text = string.Join(' ', words);
        var hits = new List<(int Rank, int Order, Page Page)>();
        for (var i = 0; i < SettingsPages.Length; i++)
        {
            var page = SettingsPages[i];
            if (!ContainsAll(page.Name + " " + page.Keywords, words))
                continue;
            var title = page.Name.ToLowerInvariant();
            var at = title.IndexOf(text, StringComparison.Ordinal);
            var rank = at == 0 ? 0 : at > 0 ? 1 : 2;
            hits.Add((rank, i, page));
        }
        hits.Sort(static (a, b) =>
        {
            var rank = a.Rank.CompareTo(b.Rank);
            return rank != 0 ? rank : a.Order.CompareTo(b.Order);
        });
        var result = new List<StartLink>(hits.Count);
        foreach (var hit in hits)
            result.Add(new StartLink("settings", hit.Page.Name, hit.Page.Uri, hit.Page.Glyph));
        return result;
    }

    /// <summary>The name is matched. The path is not, so a folder name is not a hit. A blank query matches nothing.</summary>
    public static IReadOnlyList<StartLink> SearchFiles(IReadOnlyList<RecentDocument> files, string query)
    {
        var words = Words(query);
        if (words.Count == 0)
            return [];
        var result = new List<StartLink>();
        foreach (var file in files)
        {
            if (!ContainsAll(file.Name, words))
                continue;
            result.Add(new StartLink("file", file.Name, file.Path, FileGlyph));
        }
        return result;
    }

    private static bool InFolder(string path) => path.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase);

    private static List<string> Words(string query)
    {
        var words = new List<string>();
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 0)
                words.Add(word.ToLowerInvariant());
        }
        return words;
    }

    private static bool ContainsAll(string text, List<string> words)
    {
        var hay = text.ToLowerInvariant();
        foreach (var word in words)
        {
            if (!hay.Contains(word, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

/// <summary>Where Start sits: above the taskbar, centered on it or lined up with the Start button, then slid into the work area.</summary>
public static class StartPlacement
{
    /// <summary>The gap between the menu and the top of the taskbar.</summary>
    public const double Gap = 12;

    public static Rect Above(Rect taskbar, Rect workArea, bool centered, double startButtonX)
    {
        var width = Math.Min(StartList.MenuWidth, Math.Max(1, workArea.Width));
        var height = Math.Min(StartList.MenuHeight, Math.Max(1, workArea.Height - Gap));
        var anchorX = centered ? taskbar.X + taskbar.Width / 2 : startButtonX;
        var x = centered ? anchorX - width / 2 : anchorX;
        var y = taskbar.Y - Gap - height;
        if (x < workArea.X)
            x = workArea.X;
        else if (x + width > workArea.Right)
            x = workArea.Right - width;
        if (y < workArea.Y)
            y = workArea.Y;
        else if (y + height > workArea.Bottom)
            y = workArea.Bottom - height;
        return new Rect(Round(x), Round(y), Round(width), Round(height));
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
