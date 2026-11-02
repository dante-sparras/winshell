using Winshell.DefaultShell.Taskbar;
using Winshell.Tray;
using Winshell.Updates;

namespace Winshell.DefaultShell.Tests;

/// <summary>
/// What Winshell Settings changes in the running preferences, and the update icon, without a window. The taskbar
/// widgets read these same properties.
/// </summary>
public sealed class SettingsShellTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-settings-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_preferences_file_edited_by_hand_applies_as_it_is_saved()
    {
        var path = Path.Combine(_folder, "preferences.toml");
        File.WriteAllText(path, "taskbarCentered = true\n");
        var loop = new ManualEventLoop();
        using var preferences = new Preferences(path, loop);
        Assert.True(preferences.TaskbarCentered);

        File.WriteAllText(path, "taskbarCentered = false\ntaskbarAppsOn = \"where\"\n");

        Wait.Until(
            () => !preferences.TaskbarCentered && preferences.TaskbarAppsOn == "where",
            "the hand edit",
            loop.Process);
        Assert.Equal("where", preferences.AppsOn);
    }

    [Fact]
    public void Hand_edits_apply_when_the_config_folder_did_not_exist_at_start()
    {
        var config = Path.Combine(_folder, "missing");
        var path = Path.Combine(config, "preferences.toml");
        Assert.False(Directory.Exists(config));
        var loop = new ManualEventLoop();
        using var preferences = new Preferences(path, loop);

        preferences.TaskbarAutoHide = true;
        loop.Process();

        Assert.True(File.Exists(path));
        Assert.Equal(true, Read(path, "taskbarAutoHide"));
        Assert.Equal(true, Read(path, "taskbarCentered"));

        File.WriteAllText(path, "taskbarAutoHide = false\ntaskbarCentered = false\n");

        Wait.Until(
            () => !preferences.TaskbarAutoHide && !preferences.TaskbarCentered,
            "the hand edit",
            loop.Process);
    }

    [Fact]
    public void An_update_ready_to_install_shows_in_the_notification_area_and_opens_its_card()
    {
        var updatesDir = Path.Combine(_folder, "updates");
        var staged = Path.Combine(updatesDir, "0.6.0");
        Directory.CreateDirectory(staged);
        File.WriteAllBytes(Path.Combine(staged, "winshell.exe"), []);
        var loop = new ManualEventLoop();
        var log = new Log { Sink = static (_, _, _) => { } };
        using var updater = new Updater("dante-sparras/winshell", Path.Combine(_folder, "install"), updatesDir, "0.5.0", "win-x64", log, new Refuse());
        using var updates = new UpdatesService(loop, log, updater);
        using var tray = new SystemTray(loop, log, new FakeTrayHost());
        using var preferences = new Preferences(Path.Combine(_folder, "update.toml"), loop);
        using var status = new StatusRig();
        var area = new TrayArea(tray, status.Status, updates);

        Assert.True(area.UpdateReady);
        Assert.Equal("Winshell 0.6.0 is ready. Restart Winshell to update", area.UpdateLabel);
        Assert.True(preferences.AutoUpdate);

        var clicks = 0;
        area.UpdatesRequested += (_, _) => clicks++;
        area.RequestUpdates();
        Assert.Equal(1, clicks);

        var navigation = new SettingsNavigation();
        Assert.True(navigation.Open("winshellUpdates"));
        Assert.Equal("about", navigation.Page);
    }

    [Fact]
    public void Without_an_update_the_notification_area_has_no_update_icon()
    {
        var loop = new ManualEventLoop();
        var log = new Log { Sink = static (_, _, _) => { } };
        using var updates = new UpdatesService(loop, log, null);
        using var tray = new SystemTray(loop, log, new FakeTrayHost());
        using var status = new StatusRig();
        var area = new TrayArea(tray, status.Status, updates);

        Assert.False(area.UpdateReady);
    }

    private static object? Read(string path, string key)
    {
        Assert.True(TomlDocument.TryParse(path, File.ReadAllText(path), out var document, out var error), error);
        Assert.True(document.Values.TryGetValue(key, out var value), key);
        return value;
    }

    private sealed class Refuse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("This test does not reach the network."));
    }
}
