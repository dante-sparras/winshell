using System.ComponentModel;
using Winshell.Apps;

namespace Winshell.Services.Tests;

/// <summary>
/// The Start menu's list, over a catalog of the test's own: its packaged apps are a list here and its folder is a
/// temporary one, so nothing reads this PC's Start menu and nothing is launched.
/// </summary>
public sealed class AppsServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "winshell-apps-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualEventLoop _loop = new();
    private readonly AppCatalog _catalog;
    private IReadOnlyList<AppRecord> _packaged = [];
    private Dictionary<string, AppUsage> _usage = new(StringComparer.OrdinalIgnoreCase);

    public AppsServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _catalog = new AppCatalog([_folder], () => _packaged, () => _usage, TimeSpan.Zero, false, false);
    }

    public void Dispose()
    {
        _catalog.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static AppRecord App(string name, bool hidden = false) => AppIndex.Packaged($"{name}!App", name, "")! with { Hidden = hidden };

    private async Task<AppsService> Start(TimeProvider? time = null)
    {
        var apps = new AppsService(_loop, Log.Default, _catalog, time);
        await _catalog.Ready.WaitAsync(TestContext.Current.CancellationToken);
        _loop.Process();
        return apps;
    }

    /// <summary>Touches the watched folder, so the catalog reads the lists again.</summary>
    private async Task Rescan(Func<bool> done)
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, Guid.NewGuid().ToString("N")), "", TestContext.Current.CancellationToken);
        await Wait.UntilAsync(done, "the catalog to scan again", _loop.Process);
    }

    private static List<string> Names(IEnumerable<AppEntry> entries) => entries.Select(entry => entry.Name).ToList();

    [Fact]
    public async Task Hidden_apps_stay_out_of_the_lists_and_can_still_be_found()
    {
        _packaged = [App("Zeta"), App("Alpha"), App("Uninstall", hidden: true)];
        using var apps = await Start();

        Assert.Equal(["Alpha", "Zeta"], Names(apps.Apps));
        Assert.Equal("Uninstall", apps.ById("Uninstall!App")?.Name);
        Assert.Equal("Uninstall", apps.Find("uninstall")?.Name);
        Assert.Null(apps.ById("Nothing"));
        Assert.Null(apps.Find(""));
        Assert.Equal(["Alpha"], Names(apps.Search("alp")));
        Assert.Empty(apps.Search("uninstall"));
        Assert.Empty(apps.Search("   "));
    }

    [Fact]
    public async Task An_app_that_stays_is_the_same_object_and_an_app_that_goes_leaves_every_list()
    {
        _packaged = [App("Alpha"), App("Beta")];
        _usage = new(StringComparer.OrdinalIgnoreCase) { ["Beta!App"] = new AppUsage(3, 1000) };
        using var apps = await Start();
        var alpha = apps.ById("Alpha!App")!;
        var beta = apps.ById("Beta!App")!;
        Assert.Equal(["Beta"], Names(apps.Recent));
        Assert.Equal(["Beta"], Names(apps.Frequent));

        _packaged = [App("Alpha")];
        await Rescan(() => apps.Apps.Count == 1);

        Assert.Same(alpha, Assert.Single(apps.Apps));
        Assert.Empty(apps.Recent);
        Assert.Empty(apps.Frequent);
        Assert.Null(apps.ById("Beta!App"));
        Assert.Null(beta.Launched);
    }

    [Fact]
    public async Task The_use_of_an_app_orders_recent_and_frequent()
    {
        _packaged = [App("Alpha"), App("Beta")];
        _usage = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpha!App"] = new AppUsage(1, 2000),
            ["Beta!App"] = new AppUsage(5, 1000),
        };
        using var apps = await Start();

        Assert.Equal(["Alpha", "Beta"], Names(apps.Recent));
        Assert.Equal(["Beta", "Alpha"], Names(apps.Frequent));
        Assert.Equal(5, apps.ById("Beta!App")!.RunCount);
        Assert.Equal(2000, apps.ById("Alpha!App")!.LastUsed);
    }

    [Fact]
    public async Task A_launch_counts_as_a_use_at_once()
    {
        var alpha = App("Alpha");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        _packaged = [alpha];
        using var apps = await Start(time);
        var entry = apps.ById(alpha.Id)!;
        var changes = new List<string>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        entry.Launched!(alpha);

        Assert.Equal(1, entry.RunCount);
        Assert.Equal(time.GetUtcNow().ToUnixTimeMilliseconds(), entry.LastUsed);
        Assert.Equal([nameof(AppEntry.RunCount), nameof(AppEntry.LastUsed)], changes);
        Assert.Same(entry, Assert.Single(apps.Recent));
        Assert.Same(entry, Assert.Single(apps.Frequent));
    }

    [Fact]
    public async Task A_disposed_service_stops_following_the_catalog()
    {
        _packaged = [App("Alpha")];
        var apps = await Start();
        var seen = 0;
        _catalog.Changed += () => seen++;

        apps.Dispose();
        _packaged = [];
        await Rescan(() => seen > 0);

        Assert.Single(apps.Apps);
    }

    [Fact]
    public void A_jump_list_is_read_for_an_id_and_stays_the_same_object()
    {
        var reader = new ScriptReader();
        reader.Result["Contoso.Notes"] = JumpListRead.From(["Budget.xlsx"], ["Budget.xlsx", "Notes.txt"]);
        var lists = new JumpListService(reader);

        var found = lists.ForApp("Contoso.Notes");
        Assert.True(found.Available);
        Assert.Equal(["Budget.xlsx", "Notes.txt"], found.Destinations);

        var blank = lists.ForApp("  ");
        Assert.False(blank.Available);
        Assert.Empty(blank.Destinations);
        Assert.Equal(["Contoso.Notes"], reader.Asked);

        reader.Result["Contoso.Notes"] = JumpListRead.Missing;
        var missing = lists.ForApp("Contoso.Notes");
        Assert.Same(found, missing);
        Assert.False(missing.Available);
        Assert.Empty(missing.Destinations);
    }

    [Fact]
    public void Recent_documents_match_every_word_newest_first_and_a_blank_query_reads_nothing()
    {
        Touch("Budget report.lnk", 2);
        Touch("Report notes.lnk", 3);
        Touch("Budget.lnk", 1);
        var documents = new RecentDocuments(_folder);

        Assert.Equal(["Report notes", "Budget report"], documents.Search("report").Select(document => document.Name));
        var hit = Assert.Single(documents.Search("budget REPORT"));
        Assert.Equal(Path.Combine(_folder, "Budget report.lnk"), hit.Path);
        Assert.Empty(documents.Search("   "));
        Assert.Empty(new RecentDocuments(Path.Combine(_folder, "missing")).Search("report"));
    }

    [Fact]
    public void Recent_documents_stop_at_the_limit()
    {
        for (var i = 0; i < RecentDocuments.Limit + 1; i++)
            Touch($"Doc {i:00}.lnk", i);

        var found = new RecentDocuments(_folder).Search("doc");

        Assert.Equal(RecentDocuments.Limit, found.Count);
        Assert.Equal("Doc 20", found[0].Name);
        Assert.Equal("Doc 01", found[^1].Name);
    }

    private void Touch(string name, int day)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, []);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day));
    }

    private sealed class ScriptReader : IJumpListReader
    {
        public List<string> Asked { get; } = [];

        public Dictionary<string, JumpListRead> Result { get; } = [];

        public JumpListRead Read(string appUserModelId)
        {
            Asked.Add(appUserModelId);
            return Result[appUserModelId];
        }
    }
}
