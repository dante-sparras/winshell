namespace Winshell.DefaultShell;

/// <summary>
/// Which page Winshell Settings shows, and what search found. Empty opens nothing. A setting opens its page and
/// remembers that setting for the keyboard. A page opens that page, and a subpage opens under its page. Anything else
/// stays where it was.
/// </summary>
public sealed class SettingsNavigation
{
    public string Page { get; private set; } = "personalization";

    public string Subpage { get; private set; } = "";

    public string Query { get; private set; } = "";

    public string? FocusKey { get; private set; }

    public IReadOnlyList<SettingsEntry> Results { get; private set; } = [];

    public bool Searching => Query.Trim().Length > 0;

    /// <summary>The page the body shows, or its subpage in place of it. Null while the search results show.</summary>
    public SettingsBody? Shown => Searching ? null : new SettingsBody(Page, Subpage);

    public IReadOnlyList<SettingsCrumb> Crumbs
    {
        get
        {
            if (Searching)
                return [new SettingsCrumb("", "Search results", false)];
            var page = SettingsCatalog.FindPage(Page) ?? SettingsCatalog.Pages[0];
            if (SettingsCatalog.FindSubpage(Subpage) is { } sub && sub.Page == page.Key)
                return [new SettingsCrumb(page.Key, page.Title, true), new SettingsCrumb(sub.Key, sub.Title, false)];
            return [new SettingsCrumb(page.Key, page.Title, false)];
        }
    }

    /// <summary>Opens a page, a subpage or the page of a setting. Returns false for an empty name, without warning.</summary>
    public bool Open(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        if (SettingsCatalog.FindSetting(key) is { } setting)
        {
            Show(setting.Page);
            FocusKey = setting.Key;
            return true;
        }
        if (SettingsCatalog.FindPage(key) is not null)
        {
            Show(key);
            return true;
        }
        if (SettingsCatalog.FindSubpage(key) is { } subpage)
        {
            Show(subpage.Page, subpage.Key);
            return true;
        }
        return false;
    }

    public void Show(string page, string? subpage = null)
    {
        if (SettingsCatalog.FindPage(page) is null)
            return;
        Page = page;
        var sub = SettingsCatalog.FindSubpage(subpage);
        Subpage = sub is not null && sub.Page == page ? sub.Key : "";
        Query = "";
        Results = [];
        FocusKey = null;
    }

    public void Search(string? query)
    {
        Query = query ?? "";
        Results = Searching ? SettingsCatalog.Search(Query) : [];
        FocusKey = null;
    }

    /// <summary>
    /// Where Up (-1) or Down (1) takes the keyboard from a search result, or from the search box at -1. Down from the box
    /// reaches the first result and Up from the first returns to the box. Null keeps the keyboard where it is: Up in the
    /// box, Down on the last result, and anything while no results show.
    /// </summary>
    public int? MoveInResults(int from, int direction)
    {
        if (!Searching || from < -1 || from >= Results.Count)
            return null;
        var next = from + direction;
        return next >= -1 && next < Results.Count ? next : null;
    }
}

/// <summary>A page of Winshell Settings, and its subpage when one is open. An empty subpage is the page itself.</summary>
public sealed record SettingsBody(string Page, string Subpage);
