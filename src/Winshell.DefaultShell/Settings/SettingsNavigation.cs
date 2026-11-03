namespace Winshell.DefaultShell;

/// <summary>
/// Which page Winshell Settings shows, and what search found. Empty opens nothing. A setting opens its page and
/// remembers that setting for the keyboard. A page opens that page. Anything else stays where it was.
/// </summary>
public sealed class SettingsNavigation
{
    public string Page { get; private set; } = "personalization";

    public string Subpage { get; private set; } = "";

    public string Query { get; private set; } = "";

    public string? FocusKey { get; private set; }

    public IReadOnlyList<SettingsEntry> Results { get; private set; } = [];

    public bool Searching => Query.Trim().Length > 0;

    /// <summary>
    /// What the body shows: "search" while searching, otherwise the page and its subpage as "page/subpage". A subpage
    /// takes the place of its page.
    /// </summary>
    public string Shown => Searching ? "search" : $"{Page}/{Subpage}";

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

    /// <summary>Opens a page or the page of a setting. Returns false for an empty name, without warning.</summary>
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
}
