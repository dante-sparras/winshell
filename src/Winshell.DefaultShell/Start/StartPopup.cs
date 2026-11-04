using Winshell.Apps;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// Start on one taskbar: home, all apps, or search. Opening it does not load icons. The keyboard stays in the search
/// box; <see cref="SelectionName"/> is what Narrator hears.
/// </summary>
public sealed class StartPopup : ObservableObject
{
    private static readonly IReadOnlyList<StartEntry> None = [];
    private readonly Start _start;
    private List<RecentDocument> _documents = [];
    private StartEntry? _menu;

    public StartPopup(Start start)
    {
        _start = start;
        start.Updated += OnStart;
        Rebuild(resetSelection: true);
    }

    public Start Start => _start;

    public bool IsOpen { get; private set => Set(ref field, value); }

    /// <summary>True when Search opened this surface, so the taskbar can show Search pressed.</summary>
    public bool SearchOpen { get; private set => Set(ref field, value); }

    public string Query { get; private set => Set(ref field, value); } = "";

    /// <summary><c>home</c> or <c>all</c>. Search hides both.</summary>
    public string Page { get; private set => Set(ref field, value); } = "home";

    public int Selected { get; private set => Set(ref field, value); }

    /// <summary>The app the arrow keys selected last. A new query does not change it.</summary>
    public string SelectionName { get; private set => Set(ref field, value); } = "";

    public IReadOnlyList<StartEntry> Pinned { get; private set => Set(ref field, value); } = None;

    public IReadOnlyList<StartEntry> Recommended { get; private set => Set(ref field, value); } = None;

    public IReadOnlyList<StartEntry> All { get; private set => Set(ref field, value); } = None;

    public IReadOnlyList<StartEntry> Results { get; private set => Set(ref field, value); } = None;

    /// <summary>What the arrow keys walk: pins then recommended, all apps, or search.</summary>
    public IReadOnlyList<StartEntry> Shown { get; private set => Set(ref field, value); } = None;

    public int PinnedCount => Searching || Page == "all" ? 0 : Pinned.Count;

    public bool Searching => Query.Length > 0;

    public bool ShowingHome => !Searching && Page == "home";

    public bool ShowingAll => !Searching && Page == "all";

    public bool ShowingResults => Searching && Shown.Count > 0;

    public bool EmptySearch => Searching && Shown.Count == 0;

    public bool EmptyPins => Pinned.Count == 0;

    public bool ShowingRecommended => Recommended.Count > 0;

    /// <summary>The line under search. Start does not search the web.</summary>
    public bool WebNote => Searching;

    public string PinLabel => _menu?.App is { } app && _start.IsPinned(app.Id) ? "Unpin from Start" : "Pin to Start";

    public event EventHandler<StartApp>? Launched;

    public event EventHandler<string>? LinkChosen;

    public event EventHandler<StartApp>? LocationChosen;

    /// <summary>Escape did not clear a query or leave All apps. The window closes.</summary>
    public event EventHandler? Dismissed;

    public void Open()
    {
        SearchOpen = false;
        IsOpen = true;
    }

    public void OpenSearch()
    {
        SearchOpen = true;
        IsOpen = true;
    }

    public void Close()
    {
        SearchOpen = false;
        IsOpen = false;
        _documents = [];
        Selected = 0;
        Page = "home";
        Query = "";
        Rebuild(resetSelection: true);
    }

    /// <summary>
    /// The Start button. A press outside dismisses the popup on the way down, and the button's click arrives on the
    /// way up, so the click that closed it must not open it again.
    /// </summary>
    public void Toggle(DateTimeOffset dismissed, DateTimeOffset now)
    {
        if (IsOpen)
            Close();
        else if (!TaskbarClockModel.ClosedBySameClick(dismissed, now))
            Open();
    }

    /// <summary>True when Escape cleared the query or left All apps. False when the window should close.</summary>
    public bool Escape()
    {
        if (Query.Length > 0)
        {
            SetQuery("");
            return true;
        }
        if (Page == "all")
        {
            ShowHome();
            return true;
        }
        Dismissed?.Invoke(this, EventArgs.Empty);
        return false;
    }

    public void ShowAll()
    {
        Page = "all";
        Selected = 0;
        Rebuild(resetSelection: true);
    }

    public void ShowHome()
    {
        Page = "home";
        Selected = 0;
        Rebuild(resetSelection: true);
    }

    public void SetQuery(string query)
    {
        if (query == Query)
            return;
        _documents = [];
        Selected = 0;
        Query = query;
        Rebuild(resetSelection: true);
    }

    /// <summary>Recent documents for the current query. The caller searched off the UI thread. Selection stays.</summary>
    public void SetDocuments(IReadOnlyList<RecentDocument> documents)
    {
        _documents = [.. documents];
        if (!Searching)
            return;
        Rebuild(resetSelection: false);
    }

    public void Move(string key)
    {
        var next = StartList.Move(Selected, key, Shown.Count, PinnedCount);
        if (next != Selected && (uint)Selected < (uint)Shown.Count)
            Shown[Selected].IsSelected = false;
        Selected = next;
        if ((uint)Selected < (uint)Shown.Count)
            Shown[Selected].IsSelected = true;
        SelectionName = Current?.Name ?? "";
    }

    public void Activate(StartEntry? entry = null)
    {
        if (entry is not null)
            Select(entry);
        var current = entry ?? Current;
        if (current is null)
            return;
        if (current.Kind is "settings" or "file")
        {
            if (current.Uri.Length == 0)
                return;
            LinkChosen?.Invoke(this, current.Uri);
            return;
        }
        if (current.App is { } app)
            Launched?.Invoke(this, app);
    }

    /// <summary>Right click or the menu key. Settings and files have no menu. The app is selected and not launched.</summary>
    public bool BeginMenu(StartEntry? entry)
    {
        if (entry is null || entry.Kind != "app" || entry.App is null)
            return false;
        Select(entry);
        _menu = entry;
        Changed(nameof(PinLabel));
        return true;
    }

    public bool BeginMenuForSelection() => BeginMenu(Current);

    public void ToggleMenuPin()
    {
        if (_menu?.App is not { } app)
            return;
        _start.TogglePin(app.Id);
        Changed(nameof(PinLabel));
    }

    public void OpenLocation()
    {
        if (_menu?.App is not { } app)
            return;
        LocationChosen?.Invoke(this, app);
    }

    public IReadOnlyList<string> LocationCommand() => StartList.LocationCommand(_menu?.App);

    /// <summary>Stops following the shared catalog. The catalog stays.</summary>
    public void Release() => _start.Updated -= OnStart;

    private StartEntry? Current => (uint)Selected < (uint)Shown.Count ? Shown[Selected] : null;

    private void OnStart(object? sender, EventArgs e)
    {
        Rebuild(resetSelection: false);
        Changed(nameof(PinLabel));
    }

    private void Select(StartEntry entry)
    {
        var index = -1;
        for (var i = 0; i < Shown.Count; i++)
        {
            if (!ReferenceEquals(Shown[i], entry))
                continue;
            index = i;
            break;
        }
        if (index < 0)
            return;
        if ((uint)Selected < (uint)Shown.Count && Selected != index)
            Shown[Selected].IsSelected = false;
        Selected = index;
        entry.IsSelected = true;
    }

    private void Rebuild(bool resetSelection)
    {
        IReadOnlyList<StartEntry> pinned = None;
        IReadOnlyList<StartEntry> recommended = None;
        IReadOnlyList<StartEntry> all = None;
        IReadOnlyList<StartEntry> results = None;
        IReadOnlyList<StartEntry> shown;
        if (Searching)
        {
            var apps = _start.Search(Query).Select(app => AppEntry(app, "Search", "")).ToList();
            var settings = StartList.SearchSettings(Query).Select(link => LinkEntry(link, "Settings")).ToList();
            var files = StartList.SearchFiles(_documents, Query).Select(link => LinkEntry(link, "Files")).ToList();
            Lead(apps, "Apps");
            Lead(settings, "Settings");
            Lead(files, "Files");
            results = [.. apps, .. settings, .. files];
            shown = results;
        }
        else if (Page == "all")
        {
            all = _start.AllApps.Select(app => AppEntry(app, "All apps", "")).ToList();
            shown = all;
        }
        else
        {
            pinned = _start.Pinned.Select(app => AppEntry(app, "Pinned", "")).ToList();
            recommended = _start.Recommended.Select(app => AppEntry(app, "Recommended", "")).ToList();
            shown = [.. pinned, .. recommended];
        }
        if (resetSelection)
            Selected = 0;
        else if (shown.Count == 0)
            Selected = 0;
        else if (Selected >= shown.Count)
            Selected = shown.Count - 1;
        else if (Selected < 0)
            Selected = 0;
        for (var i = 0; i < shown.Count; i++)
            shown[i].IsSelected = i == Selected;
        Pinned = pinned;
        Recommended = recommended;
        All = all;
        Results = results;
        Shown = shown;
        Changed(nameof(PinnedCount));
        Changed(nameof(Searching));
        Changed(nameof(ShowingHome));
        Changed(nameof(ShowingAll));
        Changed(nameof(ShowingResults));
        Changed(nameof(EmptySearch));
        Changed(nameof(EmptyPins));
        Changed(nameof(ShowingRecommended));
        Changed(nameof(WebNote));
    }

    private static void Lead(List<StartEntry> entries, string header)
    {
        if (entries.Count == 0)
            return;
        var first = entries[0];
        entries[0] = new StartEntry(first.Kind, first.Name, first.Section, first.Glyph, first.Icon, first.Uri, header, first.App);
    }

    private static StartEntry AppEntry(StartApp app, string section, string header) =>
        new("app", app.Name, section, "", app.Icon, "", header, app);

    private static StartEntry LinkEntry(StartLink link, string section) =>
        new(link.Kind, link.Name, section, link.Glyph, "", link.Uri, "", null);
}
