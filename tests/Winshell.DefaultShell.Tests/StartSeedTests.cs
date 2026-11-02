using Winshell.Apps;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

/// <summary>Over a real <see cref="AppsService"/>, which adds a scan's apps to its lists one at a time.</summary>
public sealed class StartSeedTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-start-seed-").FullName;
    private readonly ManualEventLoop _loop = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static AppRecord App(string name) => AppIndex.Packaged($"{name}!App", name, "")!;

    private async Task<Start> Started(AppCatalog catalog)
    {
        var apps = new AppsService(_loop, Log.Default, catalog);
        var start = new Start(Path.Combine(_folder, "launcher.toml"), _loop, Log.Default);
        start.Use(apps);
        await catalog.Ready.WaitAsync(TestContext.Current.CancellationToken);
        _loop.Process();
        return start;
    }

    [Fact]
    public async Task The_default_pins_come_from_the_whole_scan_and_not_its_first_app()
    {
        // The first app is one no preferred slot takes. Seeding from it pins that app, and the file then says so for good.
        using var catalog = new AppCatalog([_folder], () => [App("A first app"), App("Notepad"), App("Settings")], () => new Dictionary<string, AppUsage>(), TimeSpan.Zero, false, false);
        using var start = await Started(catalog);

        Assert.Equal(["Settings", "Notepad"], start.Pinned.Select(app => app.Name));
        var file = await File.ReadAllTextAsync(Path.Combine(_folder, "launcher.toml"), TestContext.Current.CancellationToken);
        Assert.Contains("Notepad!App", file, StringComparison.Ordinal);
        Assert.DoesNotContain("A first app", file, StringComparison.Ordinal);
        Assert.Single(file.Split('\n'), line => line.Contains("without this file Start pins its defaults", StringComparison.Ordinal));

        // A new run reads the file as a choice, so what it holds is what the user is left with.
        using var next = new Start(Path.Combine(_folder, "launcher.toml"), _loop, Log.Default);
        next.SetApps(start.AllApps, []);
        Assert.Equal(["Settings", "Notepad"], next.Pinned.Select(app => app.Name));
    }

    [Fact]
    public async Task A_scan_reads_the_app_list_once()
    {
        using var catalog = new AppCatalog([_folder], () => [App("One"), App("Two"), App("Three")], () => new Dictionary<string, AppUsage>(), TimeSpan.Zero, false, false);
        var apps = new AppsService(_loop, Log.Default, catalog);
        using var start = new Start(Path.Combine(_folder, "launcher.toml"), _loop, Log.Default);
        start.Use(apps);
        var updates = 0;
        start.Updated += (_, _) => updates++;

        await catalog.Ready.WaitAsync(TestContext.Current.CancellationToken);
        _loop.Process();

        Assert.Equal(3, start.AllApps.Count);
        Assert.InRange(updates, 1, 2);
    }

    [Fact]
    public void A_row_is_named_for_its_app_so_a_list_item_does_not_read_as_a_type()
    {
        using var start = new Start(Path.Combine(_folder, "launcher.toml"), _loop, Log.Default);
        start.SetApps([new StartApp("one", "One", "", "", "", false, false)], []);
        var popup = new StartPopup(start);

        popup.ShowAll();

        Assert.Equal("One", popup.All[0].ToString());
    }
}
