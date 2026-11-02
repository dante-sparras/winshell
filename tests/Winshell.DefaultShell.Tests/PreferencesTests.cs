namespace Winshell.DefaultShell.Tests;

public sealed class PreferencesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-preferences-").FullName;

    private string Toml => Path.Combine(_folder, "preferences.toml");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private Preferences Open(ManualEventLoop loop, Log? log = null) => new(Toml, loop, log);

    [Fact]
    public void A_missing_file_gives_the_defaults()
    {
        using var preferences = Open(new ManualEventLoop());

        Assert.Equal("system", preferences.ColorScheme);
        Assert.Equal(WindowBackdrop.Acrylic, preferences.Material);
        Assert.True(preferences.TaskbarCentered);
        Assert.False(preferences.TaskbarAutoHide);
        Assert.True(preferences.TaskbarAllScreens);
        Assert.Equal("Win", preferences.StartHotkey);
        Assert.Equal("", preferences.SettingsHotkey);
        Assert.False(preferences.Loaded);
    }

    [Fact]
    public void A_key_the_file_has_replaces_the_default_and_the_rest_keep_theirs()
    {
        File.WriteAllText(Toml, "backdrop = \"mica\"\ntaskbarCentered = false\n");

        using var preferences = Open(new ManualEventLoop());

        Assert.Equal(WindowBackdrop.Mica, preferences.Material);
        Assert.False(preferences.TaskbarCentered);
        Assert.True(preferences.TaskbarAllScreens);
    }

    [Fact]
    public void A_change_writes_the_file_under_its_comment_and_keeps_what_the_user_wrote()
    {
        File.WriteAllText(Toml, "# Mine\ncolorScheme = \"dark\"\nmyOwnKey = 7\n");
        var loop = new ManualEventLoop();
        using var preferences = Open(loop);

        preferences.ClockSeconds = true;
        loop.Process();

        var text = File.ReadAllText(Toml);
        Assert.Contains("# Mine", text, StringComparison.Ordinal);
        Assert.Contains("colorScheme = \"dark\"", text, StringComparison.Ordinal);
        Assert.Contains("myOwnKey = 7", text, StringComparison.Ordinal);
        Assert.Contains("clockSeconds = true", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_backdrop_Winshell_would_not_offer_is_reported_and_the_one_in_use_stays()
    {
        File.WriteAllText(Toml, "backdrop = \"blur\"\n");
        var lines = new List<string>();

        using var preferences = Open(new ManualEventLoop(), new Log { Sink = (_, _, message) => lines.Add(message) });

        Assert.Equal("acrylic", preferences.Backdrop);
        Assert.Contains(lines, l => l.Contains("is not \"acrylic\" or \"mica\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hotkey_that_would_take_typing_from_every_app_is_refused()
    {
        File.WriteAllText(Toml, "startHotkey = \"Shift+K\"\n");
        var lines = new List<string>();

        using var preferences = Open(new ManualEventLoop(), new Log { Sink = (_, _, message) => lines.Add(message) });

        Assert.Equal("Win", preferences.StartHotkey);
        Assert.Contains(lines, l => l.Contains("still types in other apps", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Win", "")]
    [InlineData("Ctrl+Alt+S", "")]
    [InlineData("Left Win+K", "")]
    [InlineData("Alt+K, S", "")]
    [InlineData("K", "Start it with Win, Ctrl or Alt, so K still types in other apps")]
    [InlineData("Shift+K", "Start it with Win, Ctrl or Alt, so Shift+K still types in other apps")]
    public void A_hotkey_must_start_with_Win_Ctrl_or_Alt(string text, string refusal) =>
        Assert.Equal(refusal, Preferences.HotkeyRefusal(text));

    [Theory]
    [InlineData("all", "all")]
    [InlineData("main", "main")]
    [InlineData("where", "where")]
    [InlineData("nonsense", "all")]
    public void Apps_on_falls_back_to_all(string value, string used)
    {
        File.WriteAllText(Toml, $"taskbarAppsOn = \"{value}\"\n");

        using var preferences = Open(new ManualEventLoop());

        Assert.Equal(used, preferences.AppsOn);
    }

    [Theory]
    [InlineData("#112233", true)]
    [InlineData("#AABBCC", true)]
    [InlineData("", false)]
    [InlineData("red", false)]
    [InlineData("#12345", false)]
    public void An_accent_is_six_hex_digits(string value, bool valid)
    {
        using var preferences = Open(new ManualEventLoop());

        preferences.AccentColor = value;

        Assert.Equal(valid, preferences.ValidAccent);
    }

    [Fact]
    public void Hiding_an_app_leaves_out_its_notifications_and_showing_it_brings_them_back()
    {
        var loop = new ManualEventLoop();
        using var preferences = Open(loop);
        Assert.True(preferences.ShowsNotificationsFrom("App.One"));

        preferences.SetNotificationsFrom("App.One", "One", shown: false);
        loop.Process();
        Assert.False(preferences.ShowsNotificationsFrom("App.One"));

        preferences.SetNotificationsFrom("App.One", "One", shown: true);
        loop.Process();
        Assert.True(preferences.ShowsNotificationsFrom("App.One"));
    }

    [Fact]
    public void A_hidden_app_with_no_id_is_ignored()
    {
        File.WriteAllText(Toml, "hiddenNotificationApps = [{ name = \"No id\" }, { id = \"App.One\", name = \"One\" }]\n");

        using var preferences = Open(new ManualEventLoop());

        Assert.Single(preferences.HiddenApps);
    }
}
