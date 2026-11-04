using Winshell.DefaultShell.Taskbar;
using Winshell.Updates;

namespace Winshell.DefaultShell.Tests;

public sealed class SettingsPagesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-settings-pages-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void An_order_list_steps_through_the_rows_on_show_and_keeps_the_rest()
    {
        Assert.Null(SettingsOrder.Move(["edge"], ["edge"], "edge", -1));
        Assert.Null(SettingsOrder.Move(["edge"], ["edge"], "missing", 1));
        Assert.Equal(["gone", "explorer", "edge"], SettingsOrder.Move(["Edge", "gone", "explorer"], ["Edge", "explorer"], "edge", 1));
        Assert.Equal(["gone", "edge"], SettingsOrder.Remove(["gone", "Explorer", "edge"], "explorer"));
        Assert.Equal("edge", SettingsOrder.FocusIndex(["edge", "notepad"], 0));
        Assert.Equal("notepad", SettingsOrder.FocusIndex(["edge", "notepad"], 5));
        Assert.Null(SettingsOrder.FocusIndex([], 0));
    }

    [Fact]
    public void Start_pins_move_and_unpin_in_the_file_Start_keeps()
    {
        var loop = new ManualEventLoop();
        var path = Path.Combine(_folder, "launcher.toml");
        File.WriteAllText(path, """
            pins = ["edge", "gone", "explorer", "notepad"]

            """);
        using var start = new Start(path, loop, Quiet());
        start.SetApps(
            [
                new("edge", "Microsoft Edge", "", "", "", false),
                new("explorer", "File Explorer", "", "", "", false),
                new("notepad", "Notepad", "", "", "", false),
            ],
            []);
        Assert.Equal(["Microsoft Edge", "File Explorer", "Notepad"], start.Pinned.Select(app => app.Name));
        Assert.False(start.MoveShown("edge", -1));

        Assert.True(start.MoveShown("edge", 1));
        Assert.Equal(["gone", "explorer", "edge", "notepad"], start.PinIds);
        var shown = start.Pinned.Select(app => app.Id).ToArray();
        var index = Array.IndexOf(shown, "explorer");
        start.UnpinShown("explorer");
        Assert.Equal(["gone", "edge", "notepad"], start.PinIds);
        Assert.Equal(["Microsoft Edge", "Notepad"], start.Pinned.Select(app => app.Name));
        Assert.Equal("edge", SettingsOrder.FocusIndex(start.Pinned.Select(app => app.Id).ToArray(), index));

        loop.Process();
        using var again = new Start(path, new ManualEventLoop(), Quiet());
        Assert.Equal(["gone", "edge", "notepad"], again.PinIds);
    }

    [Fact]
    public void The_tile_catalog_is_quick_settings_order_and_an_empty_list_shows_nothing()
    {
        Assert.Equal(
            [
                "wifi|Wi-Fi|\uE701",
                "bluetooth|Bluetooth|\uE702",
                "airplaneMode|Airplane mode|\uE709",
                "energySaver|Energy saver|\uE863",
                "powerMode|Power mode|\uEC4A",
                "nightLight|Night light|\uF08C",
                "accessibility|Accessibility|\uE776",
            ],
            QuickSettingsKind.All.Select(kind => $"{kind.Key}|{kind.Title}|{kind.Glyph}"));

        var loop = new ManualEventLoop();
        using var missing = new QuickSettingsLayout(Path.Combine(_folder, "missing.toml"), loop, Quiet());
        Assert.Null(missing.Pinned);
        Assert.Equal(QuickSettingsKind.All.Select(kind => kind.Key), QuickSettingsOrder.Keys(null));

        File.WriteAllText(Path.Combine(_folder, "empty.toml"), "pinned = []\n");
        using var empty = new QuickSettingsLayout(Path.Combine(_folder, "empty.toml"), loop, Quiet());
        Assert.Empty(QuickSettingsOrder.Keys(empty.Pinned));
    }

    [Fact]
    public void Quick_settings_tiles_move_come_off_and_go_back_in_the_shared_layout()
    {
        var loop = new ManualEventLoop();
        var path = Path.Combine(_folder, "quick-settings.toml");
        File.WriteAllText(path, """
            pinned = ["bluetooth", "custom", "wifi"]

            """);
        using var layout = new QuickSettingsLayout(path, loop, Quiet());
        var keys = QuickSettingsOrder.Keys(layout.Pinned);

        // A key no tile has stays between the two on show, and a step lands on the neighbour's side of it.
        Assert.Equal("bluetooth", QuickSettingsOrder.Step(["bluetooth", "wifi"], "wifi", -1));
        Assert.Equal(["wifi", "bluetooth", "custom"], QuickSettingsOrder.Move(keys, "wifi", "bluetooth"));
        Assert.Equal(["custom", "bluetooth", "wifi"], QuickSettingsOrder.Move(keys, "bluetooth", "custom"));
        Assert.Equal(["custom", "wifi", "bluetooth"], QuickSettingsOrder.Move(keys, "bluetooth", "wifi"));
        Assert.Null(QuickSettingsOrder.Move(keys, "wifi", "wifi"));
        Assert.Null(QuickSettingsOrder.Move(keys, "wifi", "nightLight"));
        Assert.Equal("wifi", QuickSettingsOrder.Step(["bluetooth", "wifi"], "wifi", 1));
        Assert.Null(QuickSettingsOrder.Step(["bluetooth"], "wifi", 1));

        var removed = QuickSettingsOrder.Remove(keys, "bluetooth");
        QuickSettingsOrder.Save(layout, QuickSettingsOrder.Add(removed, "nightLight"));
        loop.Process();

        using var again = new QuickSettingsLayout(path, new ManualEventLoop(), Quiet());
        Assert.Equal(["custom", "wifi", "nightLight"], again.Pinned!.Cast<string>());
    }

    [Fact]
    public void A_key_the_file_lists_twice_is_removed_whole_moved_once_and_never_added_again()
    {
        List<string> twice = ["wifi", "bluetooth", "wifi"];

        Assert.Equal(["bluetooth"], QuickSettingsOrder.Remove(twice, "wifi"));
        Assert.Equal(twice, QuickSettingsOrder.Add(twice, "wifi"));
        Assert.Equal(["bluetooth", "wifi"], QuickSettingsOrder.Move(twice, "wifi", "bluetooth"));
    }

    [Fact]
    public void A_list_the_file_already_has_is_not_written_back()
    {
        var path = Path.Combine(_folder, "quick-settings.toml");
        File.WriteAllText(path, "pinned = [\"wifi\"]\n");
        using var layout = new QuickSettingsLayout(path, new ManualEventLoop(), Quiet());
        var heard = 0;
        layout.PropertyChanged += (_, _) => heard++;

        QuickSettingsOrder.Save(layout, ["wifi"]);
        Assert.Equal(0, heard);

        QuickSettingsOrder.Save(layout, ["wifi", "bluetooth"]);
        Assert.Equal(1, heard);
    }

    [Theory]
    [InlineData("S", "Start it with Win, Ctrl or Alt, so S still types in other apps")]
    [InlineData("Shift+S", "Start it with Win, Ctrl or Alt, so Shift+S still types in other apps")]
    [InlineData("Space, Ctrl+K", "Start it with Win, Ctrl or Alt, so Space, Ctrl+K still types in other apps")]
    [InlineData("win", "Open Start already uses Win")]
    public void A_hotkey_that_would_take_a_typing_key_or_repeats_the_other_is_not_saved(string typed, string reason)
    {
        var saved = SettingsHotkeys.Commit("  Ctrl+Alt+S  ", "", "Open Start", "Win");
        Assert.Equal("Ctrl+Alt+S", saved!.Value.Sequence);
        Assert.Equal("", saved.Value.Refused);

        Assert.Null(SettingsHotkeys.Commit("Ctrl+Alt+S", "Ctrl+Alt+S", "Open Start", "Win"));
        var refused = SettingsHotkeys.Commit(typed, "Ctrl+Alt+S", "Open Start", "Win");
        Assert.Equal("Ctrl+Alt+S", refused!.Value.Sequence);
        Assert.Equal(reason, refused.Value.Refused);

        var next = SettingsHotkeys.Commit("Win+Shift+S", "Ctrl+Alt+S", "Open Start", "Win");
        Assert.Equal("Win+Shift+S", next!.Value.Sequence);
        Assert.Equal("", next.Value.Refused);
        Assert.NotEqual(reason, SettingsHotkeys.Description(next.Value.Refused, next.Value.Sequence, new(true, "", false, true)));
    }

    [Fact]
    public void A_hotkey_says_when_it_is_off_on_or_taken()
    {
        Assert.True(SettingsHotkeys.Same("Win + S", "win+s"));
        Assert.Equal("Off", SettingsHotkeys.Description("", "", new(true, "", false, true)));
        Assert.Equal("", SettingsHotkeys.Description("", "Win", new(false, "", false, false)));
        Assert.Equal("no such key", SettingsHotkeys.Description("", "Win", new(true, "no such key", true, false)));
        Assert.Equal("Windows or another app is using Ctrl+Alt+S", SettingsHotkeys.Description("", "Ctrl+Alt+S", new(true, "", true, false)));
        Assert.Equal("On", SettingsHotkeys.Description("", "Win", new(true, "", false, true)));
        Assert.Equal("", SettingsHotkeys.Description("", "Win", new(true, "", false, false)));
        var off = SettingsHotkeys.Commit("   ", "Ctrl+Alt+S", "Open Start", "Win");
        Assert.Equal("", off!.Value.Sequence);
        Assert.Equal("Off", SettingsHotkeys.Description("", off.Value.Sequence, new(true, "", false, false)));
    }

    [Fact]
    public void About_says_who_updates_Winshell_and_what_a_check_found()
    {
        Assert.Equal("Winshell 1.2.3", SettingsAbout.CopyText("1.2.3"));
        Assert.Equal(SettingsAbout.NoUpdates, SettingsAbout.Updates(false, false, false, UpdateStatus.Idle, "", ""));
        Assert.Equal(SettingsAbout.WingetUpdates, SettingsAbout.Updates(false, true, false, UpdateStatus.Idle, "", ""));
        Assert.Equal(SettingsAbout.PackagedUpdates, SettingsAbout.Updates(false, false, true, UpdateStatus.Idle, "", ""));
        Assert.Equal("Checking for updates…", SettingsAbout.Updates(true, false, false, UpdateStatus.Checking, "", ""));
        Assert.Equal("You're up to date", SettingsAbout.Updates(true, false, false, UpdateStatus.UpToDate, "1.2.3", ""));
        Assert.Equal("Downloading Winshell 1.2.3…", SettingsAbout.Updates(true, false, false, UpdateStatus.Downloading, "1.2.3", ""));
        Assert.Equal("Winshell 0.6.0 is ready. Restart Winshell to finish updating", SettingsAbout.Updates(true, false, false, UpdateStatus.Ready, "0.6.0", ""));
        Assert.Equal("Another Winshell is running. Close it, then restart this one.", SettingsAbout.Updates(true, false, false, UpdateStatus.Ready, "0.6.0", "Another Winshell is running. Close it, then restart this one."));
        Assert.Equal("Restart now", SettingsAbout.UpdateButton(UpdateStatus.Ready));
        Assert.Equal("Restart Winshell to update", SettingsAbout.UpdateButtonName(UpdateStatus.Ready));
        Assert.Equal("Check for updates", SettingsAbout.UpdateButton(UpdateStatus.Idle));
        Assert.Equal("Check for Winshell updates", SettingsAbout.UpdateButtonName(UpdateStatus.Failed));
        Assert.True(SettingsAbout.UpdatesBusy(UpdateStatus.Checking));
        Assert.True(SettingsAbout.UpdatesBusy(UpdateStatus.Downloading));
        Assert.False(SettingsAbout.UpdatesBusy(UpdateStatus.Ready));
        Assert.False(SettingsAbout.AutomaticEnabled(false));
        Assert.Equal(SettingsAbout.AutomaticUnavailable, SettingsAbout.Automatic(false, "idle"));
        Assert.Equal("idle", SettingsAbout.Automatic(true, "idle"));
        Assert.False(SettingsAbout.AutomaticOn(false, true));
        Assert.True(SettingsAbout.AutomaticOn(true, true));
        Assert.Equal(SettingsAbout.NoFolder, SettingsAbout.Folder(""));
        Assert.Equal(@"C:\config", SettingsAbout.Folder(@"C:\config"));
        Assert.Equal(SettingsAbout.NoLog, SettingsAbout.Log(""));
        Assert.Equal("Get the newest Winshell from GitHub", SettingsAbout.Updates(true, false, false, UpdateStatus.Idle, "", ""));
        Assert.Equal("Winshell could not check for updates: No such host is known.", SettingsAbout.Updates(true, false, false, UpdateStatus.Failed, "", "Winshell could not check for updates: No such host is known."));
    }

    [Fact]
    public void Diagnostics_runs_off_the_caller_and_a_throw_is_one_failed_check()
    {
        var caller = Environment.CurrentManagedThreadId;
        var probe = 0;
        var session = new SettingsDiagnosticsSession(() =>
        {
            probe = Environment.CurrentManagedThreadId;
            return
            [
                new SettingsDiagnostic("windows", "pass", "Windows", "Windows 11 26H2, build 26300.1", ""),
                new SettingsDiagnostic("signIn", "warn", "Sign-in", "Startup apps turned it off.", "Turn Winshell on in Settings > Apps > Startup."),
                new SettingsDiagnostic("config", "fail", "Config", "App.xaml[1:1]: Expected }", "Correct that line."),
            ];
        });
        Action? posted = null;
        session.Run(action => posted = action);
        Assert.True(session.Running);
        Assert.Equal(SettingsAbout.DiagnosticsRunning, SettingsAbout.Diagnostics(session.Available, session.Running, session.Summary, "idle"));
        Wait.Until(() => posted is not null, "the diagnostics run to post its result");
        Assert.NotEqual(caller, probe);
        posted!();
        Assert.False(session.Running);
        Assert.Equal("1 passed, 1 warning, 1 failed.", session.Summary);
        Assert.Equal("Passed: Windows. Windows 11 26H2, build 26300.1", SettingsAbout.Spoken(session.Checks[0]));
        Assert.Equal("Warning: Sign-in. Startup apps turned it off. Fix: Turn Winshell on in Settings > Apps > Startup.", SettingsAbout.Spoken(session.Checks[1]));
        Assert.Equal("Failed: Config. App.xaml[1:1]: Expected } Fix: Correct that line.", SettingsAbout.Spoken(session.Checks[2]));

        var broken = new SettingsDiagnosticsSession(() => throw new InvalidOperationException("the registry is gone"));
        Action? failed = null;
        broken.Run(action => failed = action);
        Wait.Until(() => failed is not null, "the failed diagnostics run to post");
        failed!();
        Assert.Equal("0 passed, 0 warnings, 1 failed.", broken.Summary);
        var check = Assert.Single(broken.Checks);
        Assert.Equal("Could not run: InvalidOperationException: the registry is gone", check.Message);
        Assert.Equal($"Report it at {SettingsAbout.BugReports}.", check.Fix);

        var none = new SettingsDiagnosticsSession(null);
        Assert.False(none.Available);
        Assert.Equal(SettingsAbout.DiagnosticsUnavailable, SettingsAbout.Diagnostics(false, false, "", "Check how this PC is set up for Winshell, as winshell doctor does"));
        Assert.False(SettingsAbout.DiagnosticsEnabled(false, false));
        none.Run(static _ => throw new InvalidOperationException("a shell with no doctor posted a run"));
        Assert.False(none.Running);
    }

    [Fact]
    public void A_diagnostics_run_that_runs_out_of_memory_finishes()
    {
        var session = new SettingsDiagnosticsSession(() => throw new OutOfMemoryException("doctor"));
        Action? failed = null;

        session.Run(action => failed = action);
        Wait.Until(() => failed is not null, "the failed diagnostics run to post");
        failed!();

        Assert.False(session.Running);
        Assert.Equal("0 passed, 0 warnings, 1 failed.", session.Summary);
        Assert.Equal("Could not run: OutOfMemoryException: doctor", Assert.Single(session.Checks).Message);
    }

    [Fact]
    public void A_diagnostics_run_already_going_is_left_to_finish()
    {
        var entered = 0;
        var release = new ManualResetEventSlim(false);
        try
        {
            var session = new SettingsDiagnosticsSession(() =>
            {
                Interlocked.Increment(ref entered);
                release.Wait();
                return [new SettingsDiagnostic("windows", "pass", "Windows", "ok", "")];
            });
            Action? posted = null;
            session.Run(action => posted = action);
            Wait.Until(() => Volatile.Read(ref entered) == 1, "the diagnostics probe to start");
            session.Run(static _ => throw new InvalidOperationException("a second run started"));
            release.Set();
            Wait.Until(() => posted is not null, "the diagnostics run to post its result");
            posted!();
            Assert.False(session.Running);
            Assert.Equal(1, Volatile.Read(ref entered));
            Assert.Equal("1 passed, 0 warnings, 0 failed.", session.Summary);
        }
        finally
        {
            release.Set();
            release.Dispose();
        }
    }

    [Fact]
    public void Diagnostics_checks_the_configuration_project_without_building_it()
    {
        var project = Path.Combine(_folder, "Mine.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var before = Directory.EnumerateFileSystemEntries(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        var checks = SettingsAbout.Checks(_folder);

        Assert.Equal(
            ["windows", "dotnet", "project", "downloaded", "built", "build", "folders", "appBars", "dpi", "signIn", "notifications", "instances", "crashes"],
            checks.Select(c => c.Id));
        var projectCheck = Assert.Single(checks, c => c.Id == "project");
        var built = Assert.Single(checks, c => c.Id == "built");
        var build = Assert.Single(checks, c => c.Id == "build");
        Assert.Equal("pass", projectCheck.Status);
        // Doctor prints the user's local folder as %LOCALAPPDATA%, and this temp project lives under it.
        var printed = project.Replace(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "%LOCALAPPDATA%",
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(printed, projectCheck.Message);
        Assert.Equal("fail", built.Status);
        Assert.Equal("Run winshell run once to build it.", built.Fix);
        Assert.Equal("pass", build.Status);
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(_folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
    }

    private static Log Quiet() => new() { MinimumLevel = LogLevel.Fatal };
}
