using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Winshell.Apps;
using Winshell.Native;

namespace Winshell.Services.Tests;

/// <summary>
/// The Start menu index: shortcuts, packaged apps, ranked search, usage, and a packaged launch that
/// returns before activation finishes.
/// </summary>
public sealed class AppIndexTests
{
    private static readonly long Now = DateTimeOffset.Parse("2026-06-15T12:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();

    [Fact]
    public void Shortcuts_keep_the_user_folder_and_hide_what_start_hides()
    {
        var root = Temp();
        try
        {
            var common = Path.Combine(root, "common");
            var user = Path.Combine(root, "user");
            var program = Path.Combine(root, "Program Files");
            Directory.CreateDirectory(program);
            var demo = Path.Combine(program, "demo.exe");
            File.WriteAllBytes(demo, []);
            var readme = Path.Combine(root, "readme.txt");
            File.WriteAllText(readme, "notes");
            var cmd = Cmd();

            WriteLink(Path.Combine(common, "Accessories", "Edit.lnk"), demo, "/common", program, "common editor");
            WriteLink(Path.Combine(user, "Accessories", "Edit.lnk"), demo, "/user", program, "user editor");
            WriteLink(Path.Combine(common, "Startup", "Boot.lnk"), cmd, "", "", "boot");
            WriteLink(Path.Combine(common, "Uninstall Edit.lnk"), demo, "", program, "remove");
            WriteLink(Path.Combine(common, "Readme.lnk"), readme, "", "", "notes");
            var hidden = Path.Combine(common, "Secret.lnk");
            WriteLink(hidden, cmd, "", "", "secret");
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

            var apps = AppIndex.Shortcuts([common, user]);

            var edit = Assert.Single(apps, app => app.Id == "Accessories/Edit");
            Assert.Equal("/user", edit.Arguments);
            Assert.Equal("user editor", edit.Description);
            Assert.Equal(["Accessories"], edit.Folders);
            Assert.Equal(demo, edit.Target);
            Assert.Equal(program, edit.StartIn);
            Assert.Equal("demo", edit.AppId);
            Assert.Equal($"\"{demo}\" /user", edit.CommandLine);
            Assert.Equal(edit.LaunchPath, edit.Icon);
            Assert.False(edit.Hidden);
            Assert.False(edit.Packaged);

            Assert.DoesNotContain(apps, app => app.Id.Contains("Startup", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(apps, app => app.Name == "Secret");

            var uninstall = Assert.Single(apps, app => app.Name == "Uninstall Edit");
            Assert.True(uninstall.Hidden);
            var document = Assert.Single(apps, app => app.Name == "Readme");
            Assert.True(document.Hidden);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Packaged_apps_replace_a_shortcut_with_the_same_app_id()
    {
        Assert.Null(AppIndex.Packaged("", "Calculator", ""));
        Assert.Null(AppIndex.Packaged("id", "ms-resource:Name", ""));
        Assert.Null(AppIndex.Packaged("id", "  ", ""));

        var packaged = AppIndex.Packaged("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", " Calculator ", " A calculator ");
        Assert.NotNull(packaged);
        Assert.True(packaged.Packaged);
        Assert.Equal(packaged.Id, packaged.AppId);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", packaged.Icon);
        Assert.Equal("Calculator", packaged.Name);
        Assert.Equal("A calculator", packaged.Description);
        Assert.False(packaged.Hidden);

        var shortcut = Entry("Calculator", "Calculator", packaged.AppId, target: Cmd());
        var custom = Entry("Editor", "Accessories/Editor", "demo", target: Cmd());
        var apps = AppIndex.Combine([shortcut, custom], [packaged]);

        Assert.DoesNotContain(apps, app => app.Id == "Calculator");
        var calc = Assert.Single(apps, app => app.Packaged);
        Assert.Equal(packaged.Id, calc.Id);
        Assert.Contains(apps, app => app.Id == "Accessories/Editor");
        Assert.Equal(["Accessories/Editor", packaged.Id], apps.Select(app => app.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Find_matches_id_then_app_id_then_name()
    {
        var hidden = Entry("Uninstall Notes", "Uninstall Notes", hidden: true);
        var notepad = Entry("Notepad", "Accessories/Notepad", "notepad");
        var apps = new List<AppRecord> { notepad, hidden };

        Assert.Equal("Accessories/Notepad", AppIndex.Find(apps, "Accessories/Notepad")!.Id);
        Assert.Equal("Accessories/Notepad", AppIndex.Find(apps, "accessories/notepad")!.Id);
        Assert.Equal("Accessories/Notepad", AppIndex.Find(apps, "notepad")!.Id);
        Assert.Equal("Accessories/Notepad", AppIndex.Find(apps, "Notepad")!.Id);
        Assert.Equal("Uninstall Notes", AppIndex.Find(apps, "Uninstall Notes")!.Id);
        Assert.Null(AppIndex.Find(apps, ""));
        Assert.Null(AppIndex.Find(apps, "missing"));
    }

    [Fact]
    public void Search_ranks_the_name_ahead_of_usage()
    {
        var notes = Entry("Notes");
        var notepad = Entry("Notepad");
        var oneNote = Entry("OneNote");
        var code = Entry("Visual Studio Code");
        var toolbox = Entry("Toolbox");
        var alpha = Entry("Alpha Tool");
        var paint = Entry("Paint", description: "draw pictures");
        var hidden = Entry("Uninstall Notes", hidden: true);
        var apps = new List<AppRecord> { oneNote, code, toolbox, alpha, paint, hidden, notepad, notes };

        Assert.Equal(["Notes", "Notepad", "OneNote"], Names("note", apps));
        Assert.Equal(["Notes", "Notepad", "OneNote"], Names("NOTE", apps));
        Assert.Equal(["Notepad"], Names("notpad", apps));
        Assert.Equal(["Visual Studio Code"], Names("vsc", apps));
        Assert.Equal(["Toolbox", "Alpha Tool"], Names("tool", apps));
        Assert.Equal(["Paint"], Names("draw", apps));
        Assert.Empty(Names("xyz", apps));
        Assert.Empty(Names("   ", apps));
        Assert.Empty(Names("notes", [hidden]));

        var used = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase)
        {
            ["Notepad"] = new AppUsage(50, Now - 1_000),
        };
        Assert.Equal(["Notepad", "Notes", "OneNote"], Names("note", apps, app => used.TryGetValue(app.Id, out var usage) ? usage : default));

        var heavy = new AppUsage(100, Now);
        Assert.True(AppRank.Score("note", notepad, default, Now) > AppRank.Score("draw", paint, heavy, Now));
        Assert.True(AppRank.Score("Notepad", notepad, default, Now) > AppRank.Score("note", notepad, default, Now));
        Assert.True(AppRank.Score("note", notepad, default, Now) > AppRank.Score("pad", notepad, default, Now));
        Assert.True(AppRank.Score("pad", notepad, default, Now) > AppRank.Score("notpad", notepad, default, Now));
        Assert.True(AppRank.Score("notpad", notepad, default, Now) > AppRank.Score("draw", paint, default, Now));
        Assert.Equal(0, AppRank.Score("xyz", notepad, heavy, Now));
    }

    [Fact]
    public void Recent_and_frequent_follow_usage()
    {
        var alpha = Entry("Alpha");
        var beta = Entry("Beta");
        var gamma = Entry("Gamma");
        var hidden = Entry("Hidden", hidden: true);
        var apps = new List<AppRecord> { alpha, beta, gamma, hidden };
        var usage = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpha"] = new AppUsage(2, 2_000),
            ["Beta"] = new AppUsage(5, 1_000),
            ["Gamma"] = new AppUsage(0, 0),
            ["Hidden"] = new AppUsage(9, 3_000),
        };
        AppUsage Use(AppRecord app) => usage.TryGetValue(app.Id, out var found) ? found : default;

        Assert.Equal(["Alpha", "Beta"], AppIndex.Recent(apps, Use).Select(app => app.Name).ToArray());
        Assert.Equal(["Beta", "Alpha"], AppIndex.Frequent(apps, Use).Select(app => app.Name).ToArray());
    }

    [Fact]
    public void Usage_reads_feature_counts_and_user_assist()
    {
        Assert.Equal("notepad.exe", AppUsageReader.Rot13(AppUsageReader.Rot13("notepad.exe")));

        var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var data = Assist(14, when);
        var legacy = new byte[16];
        BitConverter.TryWriteBytes(legacy.AsSpan(0), 3);
        BitConverter.TryWriteBytes(legacy.AsSpan(8), when.ToFileTime());
        Assert.True(AppUsageReader.TryParse(data, out var parsed));
        Assert.Equal(14, parsed.RunCount);
        Assert.Equal(when.ToUnixTimeMilliseconds(), parsed.LastUsedUnixMilliseconds);
        Assert.True(AppUsageReader.TryParse(legacy, out var old));
        Assert.Equal(3, old.RunCount);
        Assert.False(AppUsageReader.TryParse(new byte[72], out _));

        var path = @"C:\Windows\System32\notepad.exe";
        var folder = AppUsageReader.ExpandKnownFolder(@" {1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe".Trim());
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "notepad.exe"), folder, StringComparer.OrdinalIgnoreCase);

        var usage = AppUsageReader.From(
            [new KeyValuePair<string, int>(path, 3)],
            [new KeyValuePair<string, int>(path, 9), new KeyValuePair<string, int>("OnlySwitched", 2)],
            [
                (AppUsageReader.Rot13(path), data),
                (AppUsageReader.Rot13(@" {1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe".Trim()), data),
                (AppUsageReader.Rot13("UEME_CTLCUACount:ctor"), data),
            ]);

        Assert.Equal(14, usage[path].RunCount);
        Assert.Equal(when.ToUnixTimeMilliseconds(), usage[path].LastUsedUnixMilliseconds);
        Assert.Equal(2, usage["OnlySwitched"].RunCount);
        Assert.Equal(14, usage[folder].RunCount);
        Assert.DoesNotContain(usage.Keys, key => key.Contains("UEME_", StringComparison.Ordinal));

        var notepad = Entry("Notepad", appId: "notepad", target: path);
        Assert.Equal(14, AppUsage.Lookup(usage, notepad).RunCount);

        var live = AppUsageReader.Read();
        Assert.All(live.Values, row =>
        {
            Assert.InRange(row.RunCount, 0, 1_000_000);
            if (row.LastUsedUnixMilliseconds != 0)
            {
                Assert.InRange(
                    row.LastUsedUnixMilliseconds,
                    DateTimeOffset.Parse("2000-01-01T00:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds(),
                    DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeMilliseconds());
            }
        });
    }

    [Fact(Timeout = 30_000)]
    public async Task Catalog_updates_when_a_shortcut_is_added_and_removed()
    {
        var root = Temp();
        var folder = Path.Combine(root, "programs");
        Directory.CreateDirectory(folder);
        using var catalog = new AppCatalog([folder], () => [], () => new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase), TimeSpan.FromMilliseconds(200), false, false);
        var changed = new ManualResetEventSlim(false);
        catalog.Changed += () => changed.Set();
        try
        {
            catalog.Start();
            await catalog.Ready.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var link = Path.Combine(folder, "Probe.lnk");
            WriteLink(link, Cmd(), "", "", "probe");
            Assert.True(Changed(changed, () => catalog.Snapshot.Entries.Any(app => app.Id == "Probe")));
            File.Delete(link);
            Assert.True(Changed(changed, () => catalog.Snapshot.Entries.All(app => app.Id != "Probe")));
        }
        finally
        {
            changed.Dispose();
            Delete(root);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task A_folder_that_comes_back_is_watched_again()
    {
        var root = Temp();
        var folder = Path.Combine(root, "programs");
        Directory.CreateDirectory(folder);
        WriteLink(Path.Combine(folder, "First.lnk"), Cmd(), "", "", "first");
        using var catalog = new AppCatalog([folder], () => [], () => new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase), TimeSpan.FromMilliseconds(500), false, false);
        var changed = new ManualResetEventSlim(false);
        catalog.Changed += () => changed.Set();
        try
        {
            catalog.Start();
            await catalog.Ready.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Contains(catalog.Snapshot.Entries, app => app.Id == "First");
            Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            Assert.True(Changed(changed, () => catalog.Snapshot.Entries.All(app => app.Id != "First")));
            WriteLink(Path.Combine(folder, "Second.lnk"), Cmd(), "", "", "second");
            Assert.True(Changed(changed, () => catalog.Snapshot.Entries.Any(app => app.Id == "Second")));
        }
        finally
        {
            changed.Dispose();
            Delete(root);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Packaged_launch_returns_before_activation_and_falls_back_on_the_loop()
    {
        using var activating = new ManualResetEventSlim(false);
        var loop = new ManualEventLoop();
        var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Woken += () => posted.TrySetResult();
        var warnings = new List<string>();
        int? fallbackThread = null;
        var started = 0;

        // An activation that blocks until the test lets it fail. A launch that waited for it would hang here.
        AppLaunch.Packaged(
            () =>
            {
                activating.Wait();
                return "not registered";
            },
            () =>
            {
                fallbackThread = Environment.CurrentManagedThreadId;
                return null;
            },
            loop,
            warnings.Add,
            () => started++);
        Assert.False(posted.Task.IsCompleted);

        activating.Set();
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(warnings);
        Assert.Null(fallbackThread);
        Assert.Equal(0, started);

        loop.Process();
        Assert.Equal(["not registered"], warnings);
        Assert.Equal(Environment.CurrentManagedThreadId, fallbackThread);
        Assert.Equal(1, started);
    }

    private static IReadOnlyList<string> Names(string query, IReadOnlyList<AppRecord> apps, Func<AppRecord, AppUsage>? usage = null) =>
    AppIndex.Search(apps, query, usage ?? (_ => default), Now).Select(app => app.Name).ToList();

    private static AppRecord Entry(string name, string? id = null, string appId = "", string description = "", string target = "", string folders = "", bool hidden = false, bool packaged = false)
    {
        var identifier = id ?? name;
        IReadOnlyList<string> folderList = folders.Length == 0 ? [] : folders.Split('/');
        return new AppRecord(identifier, name, description, target, target, "", target, "", appId, folderList, hidden, packaged, target);
    }

    private static byte[] Assist(int count, DateTimeOffset when)
    {
        var data = new byte[72];
        BitConverter.TryWriteBytes(data.AsSpan(4), count);
        BitConverter.TryWriteBytes(data.AsSpan(60), when.ToFileTime());
        return data;
    }

    private static void WriteLink(string path, string target, string arguments, string workingDirectory, string description)
    {
        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
            var shell = (IShellLinkW)link;
            var file = (IPersistFile)link;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            shell.SetPath(target);
            shell.SetArguments(arguments);
            shell.SetWorkingDirectory(workingDirectory);
            shell.SetDescription(description);
            file.Save(Path.GetFullPath(path), true);
        }
        finally
        {
            if (link is not null)
                Marshal.ReleaseComObject(link);
        }
    }

    private static string Cmd() => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static bool Changed(ManualResetEventSlim changed, Func<bool> ready)
    {
        var until = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < until)
        {
            if (ready())
                return true;
            changed.Reset();
            if (ready())
                return true;
            var left = (int)(until - Environment.TickCount64);
            if (left <= 0)
                break;
            changed.Wait(Math.Min(left, 500));
        }
        return ready();
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path) => Wait.Until(() => Deleted(path), $"{path} to be deleted");

    private static bool Deleted(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
