using Winshell.Notifications;

namespace Winshell.DefaultShell.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-settings-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_pane_is_the_pages_in_order()
    {
        Assert.Equal(
            ["Personalization", "Taskbar", "Start", "Quick settings", "Notifications", "Hotkeys", "Startup", "About"],
            SettingsCatalog.Pages.Select(page => page.Title));
    }

    [Fact]
    public void Search_finds_settings_by_their_words_and_ranks_a_title_first()
    {
        Assert.Equal(["taskbarAutoHide"], Keys(SettingsCatalog.Search("hide")));
        Assert.Equal(["taskbarAllScreens", "taskbarAppsOn"], Keys(SettingsCatalog.Search("displays")));
        Assert.Equal(
            ["taskbarAlignment", "taskbarAutoHide", "taskbarAllScreens", "taskbarAppsOn", "colorScheme"],
            Keys(SettingsCatalog.Search("taskbar")).Take(5));
        Assert.Equal(["material"], Keys(SettingsCatalog.Search("mica")));
        Assert.Equal(["trayIcons"], Keys(SettingsCatalog.Search("tray icons")));
        Assert.Empty(SettingsCatalog.Search("zzz"));
        Assert.Equal("No results for \u201czzz\u201d", SettingsCatalog.NoResults("zzz"));
        Assert.Equal("Personalization \u2022 Light or dark for the taskbar, Start, flyouts and this window", SettingsCatalog.ResultText(SettingsCatalog.RequireSetting("colorScheme")));
    }

    [Fact]
    public void Open_goes_to_a_page_or_a_setting_and_a_breadcrumb_goes_back()
    {
        var navigation = new SettingsNavigation();

        Assert.False(navigation.Open(""));
        Assert.Equal("personalization", navigation.Page);
        Assert.Null(SettingsCatalog.Unknown(""));

        Assert.True(navigation.Open("taskbar"));
        Assert.Equal("taskbar", navigation.Page);
        Assert.Null(navigation.FocusKey);
        Assert.Equal("Taskbar", navigation.Crumbs[0].Title);

        Assert.True(navigation.Open("runAtSignIn"));
        Assert.Equal("startup", navigation.Page);
        Assert.Equal("runAtSignIn", navigation.FocusKey);

        Assert.True(navigation.Open("startPins"));
        Assert.Equal("start", navigation.Page);
        Assert.Equal("", navigation.Subpage);
        Assert.Equal("startPins", navigation.FocusKey);

        navigation.Show("start", "pins");
        Assert.Equal("pins", navigation.Subpage);
        Assert.Equal("Start", navigation.Crumbs[0].Title);
        Assert.True(navigation.Crumbs[0].Link);
        Assert.Equal("Pinned apps", navigation.Crumbs[1].Title);
        Assert.False(navigation.Crumbs[1].Link);

        Assert.False(navigation.Open("pins"));
        Assert.False(navigation.Open("taskbr"));
        Assert.Equal("start", navigation.Page);
        Assert.Equal("pins", navigation.Subpage);
        Assert.Equal("Winshell Settings has no page or setting named \"taskbr\"", SettingsCatalog.Unknown("taskbr"));
        Assert.Equal("Winshell Settings has no page or setting named \"pins\"", SettingsCatalog.Unknown("pins"));
    }

    [Fact]
    public void Search_results_replace_the_page_until_one_is_opened()
    {
        var navigation = new SettingsNavigation();
        navigation.Search("mica");

        Assert.True(navigation.Searching);
        Assert.Equal("Search results", Assert.Single(navigation.Crumbs).Title);
        Assert.Equal(["material"], Keys(navigation.Results));

        Assert.True(navigation.Open(navigation.Results[0].Key));
        Assert.False(navigation.Searching);
        Assert.Equal("personalization", navigation.Page);
        Assert.Equal("material", navigation.FocusKey);

        navigation.Search("   ");
        Assert.False(navigation.Searching);
    }

    [Fact]
    public void A_wrong_scheme_or_accent_shows_the_Windows_choice_and_stays_in_the_file()
    {
        var path = Path.Combine(_folder, "preferences.toml");
        File.WriteAllText(path, "colorScheme = \"sepia\"\naccentColor = \"red\"\n");
        using var preferences = new Preferences(path, new ManualEventLoop());

        Assert.Equal("sepia", preferences.ColorScheme);
        Assert.Equal(0, SettingsCatalog.IndexOf(SettingsCatalog.ColorSchemes, preferences.ValidColorScheme));
        Assert.Equal("system", ShellPreferences.Scheme(preferences));
        Assert.False(preferences.ValidAccent);
        Assert.Equal("", ShellPreferences.Accent(preferences));
        Assert.Equal(0, SettingsCatalog.IndexOf(SettingsCatalog.AccentChoices, "windows"));
        Assert.Contains("sepia", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("red", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_manual_accent_starts_from_the_one_in_use_and_swatches_wrap()
    {
        Assert.Equal("#0078D4", SettingsCatalog.ChooseAccent("manual", "#0078d4"));
        Assert.Equal("#0078D4", SettingsCatalog.ChooseAccent("manual", "red"));
        Assert.Equal("#0078D4", SettingsCatalog.ChooseAccent("manual", ""));
        Assert.Equal("", SettingsCatalog.ChooseAccent("windows", "#0078D4"));
        Assert.Equal(48, SettingsCatalog.Accents.Count);
        Assert.Equal("Yellow gold", SettingsCatalog.Accents[0].Name);
        Assert.Equal("#FFB900", SettingsCatalog.Accents[0].Color);
        Assert.Equal("Default blue", Assert.Single(SettingsCatalog.Accents, accent => accent.Color == "#0078D4").Name);
        Assert.Equal(8, SettingsCatalog.SwatchColumns(640));
        Assert.Equal(5, SettingsCatalog.SwatchColumns(320));
        Assert.InRange(SettingsCatalog.SwatchColumns(320), 1, 7);
        Assert.True(SettingsCatalog.LightGlyph("#EF6950"));
        Assert.False(SettingsCatalog.LightGlyph("#0078D4"));
    }

    [Fact]
    public void A_Windows_setting_keeps_its_link_and_part_two_pages_stay_reachable()
    {
        Assert.Equal("ms-settings:colors", SettingsCatalog.RequireSetting("windowsColors").Uri);
        Assert.Equal("ms-settings:taskbar", SettingsCatalog.RequireSetting("trayIcons").Uri);
        Assert.Equal("ms-settings:notifications", SettingsCatalog.RequireSetting("doNotDisturb").Uri);
        Assert.Equal("ms-settings:startupapps", SettingsCatalog.RequireSetting("startupApps").Uri);
        Assert.Equal("pins", SettingsCatalog.RequireSetting("startPins").Subpage);
        Assert.Equal("about", SettingsCatalog.RequireSetting("winshellUpdates").Page);
        Assert.Equal("hotkeys", SettingsCatalog.RequireSetting("hotkeySettings").Page);
        Assert.Equal("hotkeys", SettingsCatalog.RequireSetting("desktopChords").Page);
        Assert.Equal("quickSettings", SettingsCatalog.RequireSetting("quickSettingsTiles").Page);
        Assert.Equal(2, SettingsCatalog.IndexOf(SettingsCatalog.TaskbarApps, "where"));
        Assert.Equal(0, SettingsCatalog.IndexOf(SettingsCatalog.TaskbarApps, "nope"));
        Assert.Equal(1, SettingsCatalog.IndexOf(SettingsCatalog.ColorSchemes, "light"));
        Assert.Equal(1, SettingsCatalog.IndexOf(SettingsCatalog.Alignments, "left"));
    }

    [Fact]
    public void Part_two_opens_from_its_page_or_its_setting_and_search_finds_desktop_chords()
    {
        var navigation = new SettingsNavigation();
        foreach (var key in new[]
        {
            "quickSettings", "hotkeys", "about", "quickSettingsTiles", "hotkeyStart", "hotkeySettings", "desktopChords",
            "version", "winshellUpdates", "autoUpdate", "configFolder", "log", "diagnostics",
        })
        {
            Assert.True(navigation.Open(key), key);
            Assert.Null(SettingsCatalog.Unknown(key));
            if (SettingsCatalog.FindSetting(key) is { } setting)
            {
                Assert.Equal(setting.Page, navigation.Page);
                Assert.Equal(key, navigation.FocusKey);
            }
            else
                Assert.Null(navigation.FocusKey);
        }

        Assert.Equal("desktopChords", Assert.Single(SettingsCatalog.Search("desktop chords")).Key);
        Assert.Contains(SettingsCatalog.Search("alt tab"), entry => entry.Key == "desktopChords");
    }

    [Fact]
    public void The_color_scheme_overrides_Windows_mode()
    {
        Assert.True(Shell.DarkFor("dark", false));
        Assert.False(Shell.DarkFor("light", true));
        Assert.True(Shell.DarkFor("system", true));
        Assert.False(Shell.DarkFor("system", false));
        Assert.True(Shell.DarkFor("sepia", true));
        Assert.False(Shell.DarkFor(null, false));
    }

    [Fact]
    public void Notifications_name_Do_not_disturb_and_keep_an_app_that_was_turned_off()
    {
        Assert.Equal("On", NotificationAppList.DoNotDisturbText(NotificationState.On));
        Assert.Equal("Off", NotificationAppList.DoNotDisturbText(NotificationState.Off));
        Assert.Equal("Unknown", NotificationAppList.DoNotDisturbText(NotificationState.Unknown));

        var found = NotificationAppList.Found(
            [("Fabrikam.Mail", "Mail"), ("Contoso.Chat", "Chat"), ("", "Nope"), ("Nameless", "")],
            [("Hidden.App", "Hidden"), ("Contoso.Chat", "Again")]);
        var merged = NotificationAppList.Merge([], found);
        Assert.Equal(["Chat", "Hidden", "Mail", "Nameless"], merged.Select(app => app.Name));
        Assert.Equal("Nameless", merged.Single(app => app.Id == "Nameless").Name);

        var again = NotificationAppList.Merge(merged, NotificationAppList.Found([("Zed.App", "Zed"), ("Contoso.Chat", "Chat")], []));
        Assert.Equal(["Chat", "Hidden", "Mail", "Nameless", "Zed"], again.Select(app => app.Name));
        Assert.Equal(1, again.Count(app => app.Id == "Contoso.Chat"));
    }

    [Fact]
    public void Startup_says_when_the_sign_in_entry_cannot_be_changed_here()
    {
        Assert.Equal("Only a shell started by winshell.exe can run at sign-in", SettingsStartupPage.Description(false, false, false));
        Assert.Equal("Turned off in Startup apps in Windows, which can turn it back on", SettingsStartupPage.Description(true, true, false));
        Assert.Equal("Set to start Winshell from another folder or another way. Turn it on to start this one", SettingsStartupPage.Description(true, false, true));
        Assert.Equal("For your account only", SettingsStartupPage.Description(true, false, false));
        Assert.False(SettingsStartupPage.SwitchEnabled(false, false));
        Assert.False(SettingsStartupPage.SwitchEnabled(true, true));
        Assert.True(SettingsStartupPage.SwitchEnabled(true, false));
        Assert.False(SettingsStartupPage.SwitchChecked(true, true));
        Assert.True(SettingsStartupPage.SwitchChecked(true, false));
        Assert.False(SettingsStartupPage.SwitchChecked(false, false));
    }

    private static IEnumerable<string> Keys(IEnumerable<SettingsEntry> settings) => settings.Select(setting => setting.Key);
}
