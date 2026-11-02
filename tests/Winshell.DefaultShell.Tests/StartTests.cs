using Windows.Foundation;
using Winshell.Apps;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class StartTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-start-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Start_sits_above_the_taskbar_and_slides_into_the_work_area()
    {
        var wide = new Rect(0, 0, 1920, 1000);
        var bar = new Rect(0, 1000, 1920, 48);
        Assert.Equal(new Rect(640, 268, 640, 720), StartPlacement.Above(bar, wide, centered: true, startButtonX: 12));
        Assert.Equal(new Rect(12, 268, 640, 720), StartPlacement.Above(bar, wide, centered: false, startButtonX: 12));
        Assert.Equal(new Rect(740, 268, 640, 720), StartPlacement.Above(new Rect(100, 1000, 1920, 48), wide, centered: true, 0));
        Assert.Equal(new Rect(0, 268, 500, 720), StartPlacement.Above(bar, new Rect(0, 0, 500, 1000), centered: false, startButtonX: 800));
        Assert.Equal(new Rect(640, 0, 640, 388), StartPlacement.Above(new Rect(0, 400, 1920, 48), new Rect(0, 0, 1920, 400), centered: true, 0));
    }

    [Fact]
    public void Arrows_move_through_the_grid_then_recommended_and_enter_launches()
    {
        using var start = Open("grid");
        start.SetApps(GridApps(), GridRecent());
        var popup = new StartPopup(start);
        Assert.Equal(8, popup.PinnedCount);
        Assert.Equal(11, popup.Shown.Count);
        Assert.Equal(0, popup.Selected);
        Assert.Equal("", popup.SelectionName);

        popup.Move("right");
        Assert.Equal(1, popup.Selected);
        Assert.Equal("Pin 1", popup.SelectionName);
        Assert.True(popup.Shown[1].IsSelected);
        Assert.False(popup.Shown[0].IsSelected);
        popup.Move("left");
        popup.Move("left");
        Assert.Equal(0, popup.Selected);
        Assert.Equal("Pin 0", popup.SelectionName);
        popup.Move("right");
        for (var i = 0; i < 4; i++)
            popup.Move("right");
        Assert.Equal(5, popup.Selected);
        popup.Move("down");
        Assert.Equal(6, popup.Selected);
        popup.Move("down");
        Assert.Equal(8, popup.Selected);
        popup.Move("up");
        Assert.Equal(6, popup.Selected);
        popup.Move("down");
        Assert.Equal(8, popup.Selected);

        StartApp? launched = null;
        popup.Launched += (_, app) => launched = app;
        popup.Activate();
        Assert.Equal("Recent One", launched!.Name);

        var dismissed = 0;
        popup.Dismissed += (_, _) => dismissed++;
        Assert.False(popup.Escape());
        Assert.Equal(1, dismissed);
        Assert.False(popup.IsOpen);
    }

    [Fact]
    public void Down_from_a_short_pin_row_lands_on_its_first_pin_or_on_recommended()
    {
        Assert.Equal(6, StartList.Move(5, "down", 11, 8));
        Assert.Equal(8, StartList.Move(7, "down", 11, 8));
        Assert.Equal(7, StartList.Move(7, "down", 8, 8));
        Assert.Equal(6, StartList.Move(8, "up", 11, 8));
    }

    [Fact]
    public void Search_lists_the_given_hits_and_escape_clears_before_it_dismisses()
    {
        var apps = GridApps();
        using var start = Open("search-hits", Prefix(apps));
        start.SetApps(apps, GridRecent());
        var popup = new StartPopup(start);
        popup.SetQuery("R");
        Assert.Equal(["Recent One", "Recent Two", "Recent Three"], popup.Shown.Where(entry => entry.Kind == "app").Select(entry => entry.Name));
        Assert.False(popup.ShowingHome);
        Assert.True(popup.WebNote);
        Assert.Equal(0, popup.PinnedCount);
        popup.Move("down");
        Assert.Equal(1, popup.Selected);
        popup.Move("left");
        Assert.Equal(1, popup.Selected);
        StartApp? launched = null;
        popup.Launched += (_, app) => launched = app;
        popup.Activate();
        Assert.Equal("Recent Two", launched!.Name);
        Assert.Equal("R", popup.Query);

        popup.SetQuery("Rz");
        Assert.Empty(popup.Shown);
        Assert.True(popup.EmptySearch);
        Assert.True(popup.WebNote);

        var dismissed = 0;
        popup.Dismissed += (_, _) => dismissed++;
        Assert.True(popup.Escape());
        Assert.Equal("", popup.Query);
        Assert.Equal(0, dismissed);
        Assert.True(popup.ShowingHome);
        Assert.False(popup.WebNote);
        Assert.False(popup.Escape());
        Assert.Equal(1, dismissed);
    }

    [Fact]
    public void All_apps_is_one_column_and_escape_returns_home()
    {
        var apps = ManyApps();
        using var start = Open("all-apps");
        start.SetApps(apps, []);
        var popup = new StartPopup(start);
        popup.ShowAll();
        Assert.Equal("all", popup.Page);
        Assert.True(popup.ShowingAll);
        Assert.False(popup.ShowingHome);
        Assert.Equal(33, popup.Shown.Count);
        Assert.Equal("Pin 0", popup.Shown[0].Name);
        Assert.Equal("Recent Three", popup.Shown[^1].Name);
        Assert.DoesNotContain(popup.Shown, entry => entry.Name == "Hidden Tool");
        Assert.Equal(0, popup.PinnedCount);
        popup.Move("down");
        Assert.Equal(1, popup.Selected);
        popup.Move("right");
        Assert.Equal(1, popup.Selected);
        for (var i = 0; i < 20; i++)
            popup.Move("down");
        Assert.Equal(21, popup.Selected);
        var dismissed = 0;
        popup.Dismissed += (_, _) => dismissed++;
        Assert.True(popup.Escape());
        Assert.Equal("home", popup.Page);
        Assert.Equal(0, dismissed);
        Assert.Equal(0, popup.Selected);
    }

    [Fact]
    public void Narrator_hears_the_app_the_arrow_keys_select_and_a_query_does_not_rename_it()
    {
        using var start = Open("narrator");
        start.SetApps(GridApps(), GridRecent());
        var popup = new StartPopup(start);
        popup.Move("right");
        Assert.Equal("Pin 1", popup.SelectionName);
        popup.Move("down");
        popup.Move("down");
        Assert.Equal("Recent One", popup.SelectionName);
        popup.SetQuery("R");
        Assert.Equal(0, popup.Selected);
        Assert.Equal("Recent One", popup.SelectionName);
    }

    [Fact]
    public void A_missing_pins_file_seeds_preferred_names_then_the_first_eight()
    {
        var loop = new ManualEventLoop();
        using var seeded = Open("preferred", loop: loop);
        Assert.Empty(seeded.PinIds);
        Assert.False(File.Exists(PathOf("preferred")));
        seeded.SetApps(
            [
                App("clock", "Clock"),
                App("edge", "Microsoft Edge"),
                App("calc", "Calculator"),
                App("note", "Notepad"),
                App("other", "Something Else"),
                App("hid", "Hidden Tool", hidden: true),
            ],
            []);
        loop.Process();
        Assert.Equal(["edge", "note", "calc", "clock"], seeded.PinIds);
        Assert.Equal(["Microsoft Edge", "Notepad", "Calculator", "Clock"], seeded.Pinned.Select(app => app.Name));
        var text = File.ReadAllText(PathOf("preferred"));
        Assert.Contains("edge", text, StringComparison.Ordinal);
        Assert.Contains("[] pins nothing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hid", text, StringComparison.Ordinal);

        using var fallback = Open("fallback");
        fallback.SetApps(Enumerable.Range(0, 10).Select(i => App("a" + i, "App " + i)).ToList(), []);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => "a" + i), fallback.PinIds);
    }

    [Fact]
    public void Default_pins_match_an_aumid_before_an_english_display_name()
    {
        using var seeded = Open("aumid");
        seeded.SetApps(
            [
                App("de-calc", "Rechner", appId: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", packaged: true),
                App("en-calc", "Calculator", appId: "other"),
                App("de-edge", "Microsoft Edge", appId: "MSEdge"),
            ],
            []);
        Assert.Equal(["de-edge", "de-calc"], seeded.PinIds);
    }

    [Fact]
    public void An_empty_pins_file_stays_empty_and_unknown_ids_are_kept()
    {
        var loop = new ManualEventLoop();
        var empty = PathOf("empty");
        File.WriteAllText(empty, "pins = []");
        var emptyBytes = File.ReadAllBytes(empty);
        using var blank = Open("empty", loop: loop);
        blank.SetApps([App("note", "Notepad"), App("edge", "Microsoft Edge")], []);
        loop.Process();
        Assert.Empty(blank.PinIds);
        Assert.Empty(blank.Pinned);
        Assert.Equal(emptyBytes, File.ReadAllBytes(empty));

        var kept = PathOf("kept");
        File.WriteAllText(kept, """pins = ["ghost", "note"]""");
        using var known = Open("kept", loop: loop);
        known.SetApps([App("note", "Notepad"), App("edge", "Edge")], []);
        Assert.Equal(["ghost", "note"], known.PinIds);
        Assert.Equal(["Notepad"], known.Pinned.Select(app => app.Name));
        known.TogglePin("note");
        loop.Process();
        Assert.Equal(["ghost"], known.PinIds);
        var written = File.ReadAllText(kept);
        Assert.Contains("ghost", written, StringComparison.Ordinal);
        Assert.DoesNotContain("note", written, StringComparison.Ordinal);

        using var again = Open("kept");
        again.SetApps([App("note", "Notepad")], []);
        Assert.Equal(["ghost"], again.PinIds);
        Assert.Empty(again.Pinned);
    }

    [Fact]
    public void A_pins_file_that_does_not_parse_shows_the_defaults_and_is_not_written()
    {
        var broken = PathOf("broken");
        File.WriteAllText(broken, "pins = [");
        var before = File.ReadAllBytes(broken);
        var loop = new ManualEventLoop();
        using var start = Open("broken", loop: loop);
        var apps = new List<StartApp>
        {
            App("clock", "Clock"),
            App("edge", "Microsoft Edge"),
            App("calc", "Calculator"),
            App("note", "Notepad"),
        };
        start.SetApps(apps, []);
        loop.Process();
        Assert.Equal(["edge", "note", "calc", "clock"], start.PinIds);
        Assert.NotEmpty(start.Error);
        Assert.Equal(before, File.ReadAllBytes(broken));

        apps.Add(App("paint", "Paint"));
        start.SetApps(apps, []);
        loop.Process();
        Assert.Equal(["edge", "note", "calc", "clock"], start.PinIds);
        Assert.Equal(before, File.ReadAllBytes(broken));
    }

    [Fact]
    public void A_file_with_no_pins_array_is_not_a_choice()
    {
        var loop = new ManualEventLoop();
        var path = PathOf("no-key");
        File.WriteAllText(path, "other = 1\n");
        using var start = Open("no-key", loop: loop);
        start.SetApps([App("edge", "Microsoft Edge"), App("note", "Notepad")], []);
        loop.Process();
        var text = File.ReadAllText(path);
        Assert.Contains("edge", text, StringComparison.Ordinal);
        Assert.Contains("other", text, StringComparison.Ordinal);
        Assert.Equal(["edge", "note"], start.PinIds);
    }

    [Fact]
    public void The_context_menu_pins_and_open_file_location_builds_a_command()
    {
        var note = App("note", "Notepad", icon: "C:/Apps/Notepad.lnk", target: "C:/Windows/notepad.exe");
        File.WriteAllText(PathOf("menu"), """pins = ["note"]""");
        using var start = Open("menu");
        start.SetApps([note], []);
        var popup = new StartPopup(start);
        Assert.True(popup.BeginMenuForSelection());
        Assert.Equal("Unpin from Start", popup.PinLabel);
        Assert.Equal(["explorer.exe", "/select,C:/Apps/Notepad.lnk"], popup.LocationCommand());
        StartApp? located = null;
        popup.LocationChosen += (_, app) => located = app;
        popup.OpenLocation();
        Assert.Equal(note, located);
        Assert.Equal(["explorer.exe", "shell:AppsFolder"], StartList.LocationCommand(App("store", "Store", icon: "shell:AppsFolder", target: "shell:AppsFolder", packaged: true)));
        Assert.Equal(["explorer.exe", "shell:AppsFolder"], StartList.LocationCommand(App("x", "X")));
        Assert.Equal(["explorer.exe", "shell:AppsFolder"], StartList.LocationCommand(App("packed", "Packed", icon: "C:/Apps/Packed.lnk", target: "C:/Apps/Packed.exe", packaged: true)));
        Assert.Equal(["explorer.exe", "shell:AppsFolder"], StartList.LocationCommand(App("folder", "Folder", icon: "shell:appsfolder\\Folder", target: "C:/Apps/Folder.exe")));
        Assert.Equal(
            ["explorer.exe", "/select,C:/Apps/Notepad.lnk"],
            StartList.LocationCommand(App("note", "Notepad", icon: "C:/Apps/Notepad.lnk", target: "C:/Windows/notepad.exe")));

        var launched = 0;
        popup.Launched += (_, _) => launched++;
        Assert.False(popup.BeginMenu(null));
        popup.ToggleMenuPin();
        Assert.Empty(start.PinIds);
        Assert.Equal("Pin to Start", popup.PinLabel);
        Assert.Equal(0, launched);
        Assert.False(popup.BeginMenu(popup.Results.FirstOrDefault()));
    }

    [Fact]
    public void Recommended_apps_can_be_turned_off_and_a_shrinking_list_clamps_the_selection()
    {
        File.WriteAllText(PathOf("recommended"), """pins = ["p0", "p1"]""");
        using var start = Open("recommended");
        start.SetApps(GridApps(), GridRecent());
        var popup = new StartPopup(start);
        Assert.Equal(["Recent One", "Recent Two", "Recent Three"], start.Recommended.Select(app => app.Name));
        start.ShowRecommended = false;
        Assert.Empty(start.Recommended);
        Assert.Equal(["p0", "p1"], popup.Shown.Select(entry => entry.App!.Id));
        Assert.False(popup.ShowingRecommended);

        popup.Move("right");
        Assert.Equal(1, popup.Selected);
        start.TogglePin("p1");
        Assert.Equal(["p0"], start.PinIds);
        Assert.Equal(0, popup.Selected);
        Assert.Equal("Pin 0", popup.Shown[0].Name);
    }

    [Fact]
    public void Start_follows_its_pins_file_as_it_changes_and_a_deleted_file_does_not_reseed()
    {
        var loop = new ManualEventLoop();
        var path = PathOf("follow");
        File.WriteAllText(path, """pins = ["p0", "p1"]""");
        using var start = Open("follow", loop: loop);
        start.SetApps(GridApps().Take(2).ToList(), []);
        Assert.Equal(["p0", "p1"], start.PinIds);

        File.WriteAllText(path, """pins = ["p1"]""");
        Wait.Until(() => start.PinIds.Count == 1, "Start did not follow the file.", loop.Process);
        Assert.Equal(["Pin 1"], start.Pinned.Select(app => app.Name));

        var updates = 0;
        start.Updated += (_, _) => updates++;
        File.Delete(path);
        Wait.Until(() => updates > 0, "Start did not notice the deleted file.", loop.Process);
        Assert.Equal(["p1"], start.PinIds);
    }

    [Fact]
    public void Search_lists_settings_and_recent_documents_after_apps()
    {
        var apps = new List<StartApp> { App("alarms", "Alarms"), App("r0", "Recent One") };
        File.WriteAllText(PathOf("settings"), "pins = []");
        using var start = Open("settings", Prefix(apps));
        start.SetApps(apps, [App("r0", "Recent One")]);
        var popup = new StartPopup(start);
        Assert.False(popup.WebNote);
        Assert.True(popup.ShowingHome);
        Assert.Equal(["Recent One"], popup.Recommended.Select(entry => entry.Name));

        var files = new List<RecentDocument>
        {
            new("Agenda.docx", "C:/Docs/Agenda.docx.lnk"),
            new("Notes", "C:/Agenda/Notes.lnk"),
        };
        popup.SetQuery("a");
        popup.SetDocuments(files);
        Assert.Equal(
            [
                "Alarms",
                "Airplane mode", "Accessibility", "All settings", "Energy saver", "Other system tray icons",
                "Startup apps in Windows", "More volume settings", "Colors in Windows",
                "Agenda.docx",
            ],
            popup.Shown.Select(entry => entry.Name));
        Assert.Equal("Apps", popup.Shown[0].Header);
        Assert.Equal("Settings", popup.Shown[1].Header);
        Assert.Equal("Files", popup.Shown[^1].Header);
        Assert.Equal("Search", popup.Shown[0].Section);
        Assert.Equal("Settings", popup.Shown[1].Section);
        Assert.Equal("Files", popup.Shown[^1].Section);
        Assert.True(popup.WebNote);
        Assert.False(popup.EmptySearch);

        string? link = null;
        StartApp? launched = null;
        popup.LinkChosen += (_, uri) => link = uri;
        popup.Launched += (_, app) => launched = app;
        popup.Move("down");
        Assert.Equal("Airplane mode", popup.SelectionName);
        popup.Activate();
        Assert.Equal("ms-settings:network-airplanemode", link);
        Assert.Null(launched);
        Assert.False(popup.BeginMenuForSelection());

        Assert.True(popup.Escape());
        Assert.Equal("", popup.Query);
        Assert.False(popup.WebNote);
        Assert.True(popup.ShowingHome);

        popup.SetQuery("night");
        popup.SetDocuments(files);
        Assert.Equal(["Night light"], popup.Shown.Select(entry => entry.Name));
        Assert.False(popup.EmptySearch);
        Assert.True(popup.WebNote);
        popup.Activate();
        Assert.Equal("ms-settings:nightlight", link);
        Assert.Null(launched);

        Assert.True(popup.Escape());
        popup.SetQuery("agenda");
        popup.SetDocuments(files);
        Assert.Equal(["Agenda.docx"], popup.Shown.Select(entry => entry.Name));
        popup.Activate();
        Assert.Equal("C:/Docs/Agenda.docx.lnk", link);
        Assert.Null(launched);

        Assert.True(popup.Escape());
        popup.SetQuery("zzzzzz");
        popup.SetDocuments(files);
        Assert.Empty(popup.Shown);
        Assert.True(popup.EmptySearch);
        Assert.True(popup.WebNote);
        popup.SetQuery("");
        Assert.True(popup.ShowingHome);
        Assert.Empty(popup.Results);
    }

    [Fact]
    public void One_hotkey_closes_any_open_start_and_otherwise_opens_the_main_one()
    {
        using var start = Open("hotkey");
        var main = new StartPopup(start);
        var other = new StartPopup(start);
        other.OpenSearch();
        StartHotkey.Toggle([main, other], 0);
        Assert.False(main.IsOpen);
        Assert.False(other.IsOpen);
        Assert.False(other.SearchOpen);

        StartHotkey.Toggle([main, other], 1);
        Assert.False(main.IsOpen);
        Assert.True(other.IsOpen);
        Assert.False(other.SearchOpen);

        other.Close();
        StartHotkey.Toggle([main, other], 0);
        Assert.True(main.IsOpen);
        Assert.False(main.SearchOpen);
    }

    [Fact]
    public void The_start_button_does_not_reopen_what_its_click_just_closed_and_search_does()
    {
        using var start = Open("toggle");
        var popup = new StartPopup(start);
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var openedAt = t0.AddSeconds(5);
        popup.Toggle(t0, openedAt);
        Assert.True(popup.IsOpen);
        Assert.False(popup.SearchOpen);
        popup.Toggle(t0, openedAt);
        Assert.False(popup.IsOpen);

        popup.Toggle(openedAt, openedAt.AddMilliseconds(499));
        Assert.False(popup.IsOpen);
        popup.Toggle(openedAt, openedAt.AddMilliseconds(500));
        Assert.True(popup.IsOpen);
        popup.Close();

        popup.SetQuery("notes");
        popup.ShowAll();
        popup.OpenSearch();
        Assert.True(popup.SearchOpen);
        Assert.Equal("notes", popup.Query);
        popup.Open();
        Assert.False(popup.SearchOpen);
        Assert.True(popup.IsOpen);
        popup.Close();
        Assert.False(popup.IsOpen);
        Assert.Equal("", popup.Query);
        Assert.Equal("home", popup.Page);
        Assert.Equal(0, popup.Selected);

        popup.OpenSearch();
        Assert.True(popup.IsOpen);
        Assert.True(popup.SearchOpen);
    }

    private string PathOf(string name) => Path.Combine(_folder, name + ".toml");

    private Start Open(string name, Func<string, IReadOnlyList<StartApp>>? search = null, ManualEventLoop? loop = null) =>
        new(PathOf(name), loop ?? new ManualEventLoop(), Quiet(), search);

    private static Log Quiet() => new() { MinimumLevel = LogLevel.Fatal };

    private static StartApp App(string id, string name, string appId = "", string icon = "", string target = "", bool packaged = false, bool hidden = false) =>
        new(id, name, appId, icon, target, packaged, hidden);

    private static List<StartApp> GridApps()
    {
        var apps = Enumerable.Range(0, 8).Select(i => App("p" + i, "Pin " + i)).ToList();
        apps.Add(App("r0", "Recent One"));
        apps.Add(App("r1", "Recent Two"));
        apps.Add(App("r2", "Recent Three"));
        apps.Add(App("hid", "Hidden Tool", hidden: true));
        return apps;
    }

    private static List<StartApp> GridRecent() =>
    [
        App("r0", "Recent One"),
        App("r1", "Recent Two"),
        App("hid", "Hidden Tool", hidden: true),
        App("p0", "Pin 0"),
        App("r2", "Recent Three"),
    ];

    private static List<StartApp> ManyApps()
    {
        var apps = Enumerable.Range(0, 30).Select(i => App("p" + i, "Pin " + i)).ToList();
        apps.Add(App("r0", "Recent One"));
        apps.Add(App("r1", "Recent Two"));
        apps.Add(App("r2", "Recent Three"));
        apps.Add(App("hid", "Hidden Tool", hidden: true));
        return apps;
    }

    private static Func<string, IReadOnlyList<StartApp>> Prefix(IReadOnlyList<StartApp> apps) => query =>
    {
        if (query.Length == 0)
            return [];
        return apps.Where(app => !app.Hidden && app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();
    };
}
