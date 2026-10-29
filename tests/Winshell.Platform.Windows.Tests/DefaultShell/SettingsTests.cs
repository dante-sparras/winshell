using System.Text.Json;
using SkiaSharp;
using Winshell.Platform.Windows.Services.Session;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// Winshell Settings, against fake services, with its files in the harness's temporary folder. Nothing here changes
/// a Windows setting, the user's preferences or the user's Run key.
/// </summary>
[Collection(ThemeCollection.Name)]
public class SettingsTests
{
    // The files are read once the test has written them: ready points the view and Preferences at them.
    private const string View = """
        import Winshell
        import Winshell.Apps
        import QtQuick
        SettingsView {
          property bool ready: false
          anchors.fill: parent
          showIcons: false
          apps: Apps.apps.values
          pinsPath: ready ? Shell.shellDir + "/launcher.json" : ""
          tilesPath: ready ? Shell.shellDir + "/quick-settings.json" : ""
          onReadyChanged: Preferences.path = Shell.shellDir + "/preferences.json"
        }
        """;

    private const int Down = 0x01000015;
    private const int Right = 0x01000014;
    private const int Return = 0x01000004;

    private static readonly string[] Pages = ["personalization", "taskbar", "start", "quickSettings", "notifications", "hotkeys", "startup", "about"];

    private static DefaultShellHarness Open(string qml = View, Dictionary<string, string>? files = null, FakeServices? services = null, Action<QmlEngine>? configure = null, double width = 1000, double height = 700)
    {
        var shell = new DefaultShellHarness(qml, width, height, services, configure);
        foreach (var (name, text) in files ?? [])
            File.WriteAllText(Path.Combine(shell.Directory, name), text);
        if (shell.Root.HasProperty("ready"))
            shell.Root.WriteProperty("ready", true);
        shell.Settle();
        AssertQuiet(shell);
        return shell;
    }

    private static void AssertQuiet(DefaultShellHarness shell) =>
        Assert.True(shell.Messages.Count == 0, string.Join("\n", shell.Messages.Select(m => m.Replace(shell.Directory, ""))));

    private static void Show(DefaultShellHarness shell, string page, string subpage = "")
    {
        shell.Root.WriteProperty("page", page);
        shell.Root.WriteProperty("subpage", subpage);
        shell.Settle();
    }

    private static Item Card(DefaultShellHarness shell, string key) => shell.Find("setting:" + key);

    private static T Control<T>(DefaultShellHarness shell, string key) where T : Item =>
        DefaultShellHarness.FindAll(Card(shell, key)).OfType<T>().Single();

    private static void Pick(DefaultShellHarness shell, string key, int index)
    {
        Control<ComboBox>(shell, key).Emit("activated", index);
        shell.Settle();
    }

    private static JsonElement Saved(DefaultShellHarness shell, string file = "preferences.json") =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(shell.Directory, file))).RootElement;

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static AccessibleNode[] Nodes(DefaultShellHarness shell)
    {
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        return Flatten(tree.Root).ToArray();
    }

    private static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static AccessibleNode Node(DefaultShellHarness shell, Item item) => Nodes(shell).Single(n => n.Item == item);

    // The rows of an ordered list, top to bottom, by key and by the name Narrator reads.
    private static Item[] Rows(DefaultShellHarness shell, string list) =>
        DefaultShellHarness.FindAll(shell.Find(list)).Where(i => i.ObjectName.StartsWith("item:", StringComparison.Ordinal) && i.Visible)
            .OrderBy(i => i.MapToScene(0, 0).Y).ToArray();

    private static string[] Keys(DefaultShellHarness shell, string list) => Rows(shell, list).Select(i => i.ObjectName["item:".Length..]).ToArray();

    private static string[] Titles(DefaultShellHarness shell, string list)
    {
        var nodes = Nodes(shell);
        return Rows(shell, list).Select(r => nodes.Single(n => n.Item == r).Name).ToArray();
    }

    private static Item RowButton(DefaultShellHarness shell, string list, string key, string button) =>
        DefaultShellHarness.FindAll(Rows(shell, list).Single(r => r.ObjectName == "item:" + key)).Single(i => i.ObjectName == button);

    private static void Type(DefaultShellHarness shell, string text)
    {
        foreach (var c in text)
            shell.Scene.TextInput(c.ToString());
        shell.Settle();
    }

    private static void Clear(DefaultShellHarness shell)
    {
        var search = (TextField)shell.Find("search");
        search.WriteProperty("text", "");
        search.ForceActiveFocus();
        shell.Settle();
    }

    [Fact]
    public void The_pane_lists_every_page_and_a_click_opens_one_under_its_title()
    {
        using var shell = Open();

        AccessibleNode Pane() => Assert.Single(Nodes(shell), n => n.Role == AccessibleAttached.RoleValue.List && n.Name == "Settings");
        Assert.Equal(["Personalization", "Taskbar", "Start", "Quick settings", "Notifications", "Hotkeys", "Startup", "About"], Pane().Children.Select(n => n.Name));
        Assert.All(Pane().Children, n => Assert.Equal(AccessibleAttached.RoleValue.ListItem, n.Role));
        Assert.Equal("Personalization", Assert.Single(Pane().Children, n => n.IsSelected == true).Name);

        shell.Click(shell.Find("nav:taskbar"));
        Assert.Equal("taskbar", shell.Root.ReadProperty("page"));
        Assert.True(shell.Find("nav:taskbar").ChildItems.Single(i => i.ObjectName == "pill").Visible);
        Assert.False(shell.Find("nav:personalization").ChildItems.Single(i => i.ObjectName == "pill").Visible);
        Assert.Equal("Taskbar", shell.Find("crumb:taskbar").ReadProperty("text"));
        Assert.True(shell.Has("setting:taskbarAlignment"));
        Assert.False(shell.Has("setting:colorScheme"));
        Assert.Equal("Taskbar", Assert.Single(Pane().Children, n => n.IsSelected == true).Name);

        // Down in the pane moves to the next page.
        shell.Find("nav:taskbar").ForceActiveFocus();
        shell.Key(Down);
        Assert.Equal("start", shell.Root.ReadProperty("page"));
    }

    [Fact]
    public void A_page_under_a_page_shows_a_breadcrumb_that_goes_back()
    {
        using var shell = Open();
        shell.Click(shell.Find("nav:start"));
        shell.Click(Card(shell, "startPins"));

        Assert.Equal("pins", shell.Root.ReadProperty("subpage"));
        Assert.Equal("Pinned apps", shell.Find("crumb:pins").ReadProperty("text"));
        Assert.Equal("Start, Pinned apps", Node(shell, shell.Find("breadcrumb")).Name);
        Assert.Equal(AccessibleAttached.RoleValue.Link, Node(shell, shell.Find("crumb:start")).Role);
        Assert.True(shell.Has("pinList"));

        shell.Click(shell.Find("crumb:start"));
        Assert.Equal("", shell.Root.ReadProperty("subpage"));
        Assert.True(shell.Has("setting:startPins"));
    }

    [Fact]
    public void Search_finds_settings_by_their_words_and_opens_one_with_the_keyboard_on_it()
    {
        using var shell = Open();
        Clear(shell);
        Type(shell, "hide");

        Assert.Equal("Search results", shell.Find("crumb:").ReadProperty("text"));
        // No page reads as selected while the results show.
        var pane = Assert.Single(Nodes(shell), n => n.Role == AccessibleAttached.RoleValue.List && n.Name == "Settings");
        Assert.DoesNotContain(pane.Children, n => n.IsSelected == true);
        string[] Results() => DefaultShellHarness.FindAll(shell.Find("results"))
            .Where(i => i.ObjectName.StartsWith("result:", StringComparison.Ordinal) && i.Visible)
            .OrderBy(r => r.MapToScene(0, 0).Y).Select(r => r.ObjectName).ToArray();
        Assert.Equal(["result:taskbarAutoHide"], Results());

        // Down goes into the results; Enter opens the setting with the keyboard on its switch.
        shell.Key(Down);
        Assert.True(shell.Find("result:taskbarAutoHide").ActiveFocus);
        shell.Key(Return);
        Assert.Equal("taskbar", shell.Root.ReadProperty("page"));
        Assert.Equal("", shell.Root.ReadProperty("query"));
        Assert.True(Control<Switch>(shell, "taskbarAutoHide").ActiveFocus);

        // Every word must match; titles that start with the query come first, then the others in page order.
        Clear(shell);
        Type(shell, "displays");
        Assert.Equal(["result:taskbarAllScreens", "result:taskbarAppsOn"], Results());
        Clear(shell);
        Type(shell, "taskbar");
        Assert.Equal(["result:taskbarAlignment", "result:taskbarAutoHide", "result:taskbarAllScreens", "result:taskbarAppsOn", "result:colorScheme"], Results().Take(5));

        // Words match the description and keywords too.
        Clear(shell);
        Type(shell, "mica");
        Assert.Equal(["result:material"], Results());
        shell.Key(Return);
        Assert.Equal("personalization", shell.Root.ReadProperty("page"));
        Assert.True(Control<ComboBox>(shell, "material").ActiveFocus);

        // A result that is a link goes to its card, not to Windows.
        Clear(shell);
        Type(shell, "tray icons");
        shell.Click(shell.Find("result:trayIcons"));
        Assert.Empty(shell.Urls.Opened);
        Assert.True(Card(shell, "trayIcons").ActiveFocus);

        Clear(shell);
        Type(shell, "zzz");
        Assert.Empty(Results());
        Assert.Contains(DefaultShellHarness.FindAll(shell.Find("results")).OfType<Text>(), t => t.Visible && t.Text_ == "No results for “zzz”");
    }

    [Fact]
    public void Open_goes_to_a_page_or_to_a_setting_and_ignores_what_it_does_not_know()
    {
        using var shell = Open();
        void Call(string key)
        {
            shell.Engine.Js.CallFunction(shell.Root.GetFunction("open")!, shell.Engine.Js.Wrap(shell.Root), [key]);
            shell.Settle();
        }

        Call("about");
        Assert.Equal("about", shell.Root.ReadProperty("page"));
        Call("runAtSignIn");
        Assert.Equal("startup", shell.Root.ReadProperty("page"));
        Assert.True(Control<Switch>(shell, "runAtSignIn").ActiveFocus);
        Call("nonsense");
        Assert.Equal("startup", shell.Root.ReadProperty("page"));
    }

    [Fact]
    public void Choices_are_saved_to_preferences_json_and_a_saved_file_is_read_back()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(false);
            using (var shell = Open())
            {
                Pick(shell, "colorScheme", 2);
                Assert.True(Theme.Current.IsDark);
                Assert.Equal("dark", Saved(shell).GetProperty("colorScheme").GetString());

                Show(shell, "taskbar");
                Pick(shell, "taskbarAlignment", 1);
                shell.Click(Control<Switch>(shell, "taskbarAutoHide"));
                shell.Click(Control<Switch>(shell, "taskbarAllScreens"));
                var json = Saved(shell);
                Assert.False(json.GetProperty("taskbarCentered").GetBoolean());
                Assert.True(json.GetProperty("taskbarAutoHide").GetBoolean());
                Assert.False(json.GetProperty("taskbarAllScreens").GetBoolean());
                // With one taskbar, where apps show is moot.
                Assert.False(Control<ComboBox>(shell, "taskbarAppsOn").Enabled);
                AssertQuiet(shell);
            }
            // The shell going away gives the theme back to Windows.
            Assert.False(Theme.Current.IsDark);

            using var again = Open(files: new() { ["preferences.json"] = """{ "colorScheme": "light", "taskbarAppsOn": "where", "startRecommended": false, "unknown": 1 }""" });
            Assert.Equal(1, Control<ComboBox>(again, "colorScheme").ReadProperty("currentIndex"));
            Show(again, "taskbar");
            Assert.Equal(2, Control<ComboBox>(again, "taskbarAppsOn").ReadProperty("currentIndex"));
            Show(again, "start");
            Assert.False(Control<Switch>(again, "startRecommended").Checked);
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void A_preferences_file_with_wrong_values_follows_Windows()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            using var shell = Open(files: new() { ["preferences.json"] = """{ "colorScheme": "sepia", "accentColor": "red" }""" });
            Assert.True(Theme.Current.IsDark);
            Assert.Equal(AccentColors.Default, Theme.Current.Accents);
            Assert.Equal(0, Control<ComboBox>(shell, "colorScheme").ReadProperty("currentIndex"));
            Assert.Equal(0, Control<ComboBox>(shell, "accentColor").ReadProperty("currentIndex"));
            Assert.False(shell.Has("accentSwatches"));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void A_manual_accent_starts_from_the_one_in_use_and_a_swatch_picks_another()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(false);
            using var shell = Open();
            Pick(shell, "accentColor", 1);
            Assert.Equal("#0078D4", Saved(shell).GetProperty("accentColor").GetString());
            Assert.True(shell.Has("accentSwatches"));

            var swatches = Assert.Single(Nodes(shell), n => n.Name == "Windows colors").Children;
            Assert.Equal(48, swatches.Count);
            Assert.Equal("Yellow gold", swatches[0].Name);
            Assert.All(swatches, s => Assert.Equal(AccessibleAttached.RoleValue.RadioButton, s.Role));
            Assert.Equal("Default blue", Assert.Single(swatches, s => s.Toggle == ToggleState.On).Name);

            shell.Click(shell.Find("swatch:#E81123"));
            Assert.Equal("#E81123", Saved(shell).GetProperty("accentColor").GetString());
            Assert.Equal(QColor.Parse("#e81123"), Theme.Current.Accents.Accent);

            // The arrow keys move between swatches, eight to a row; Enter picks.
            shell.Find("swatch:#E81123").ForceActiveFocus();
            shell.Key(Right);
            shell.Key(Down);
            Assert.True(shell.Find("swatch:#8E8CD8").ActiveFocus);
            shell.Key(Return);
            Assert.Equal("#8E8CD8", Saved(shell).GetProperty("accentColor").GetString());

            Pick(shell, "accentColor", 0);
            Assert.Equal("", Saved(shell).GetProperty("accentColor").GetString());
            Assert.Equal(AccentColors.Default, Theme.Current.Accents);
            Assert.False(shell.Has("accentSwatches"));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Theory]
    [InlineData("personalization", "windowsColors", "ms-settings:colors")]
    [InlineData("taskbar", "trayIcons", "ms-settings:taskbar")]
    [InlineData("notifications", "doNotDisturb", "ms-settings:notifications")]
    [InlineData("startup", "startupApps", "ms-settings:startupapps")]
    public void A_Windows_setting_is_a_link_to_its_page_in_Windows_Settings(string page, string key, string uri)
    {
        using var shell = Open();
        Show(shell, page);

        Assert.Equal(AccessibleAttached.RoleValue.Link, Node(shell, Card(shell, key)).Role);
        shell.Click(Card(shell, key));
        Assert.Equal([uri], shell.Urls.Opened);

        Card(shell, key).ForceActiveFocus();
        shell.Key(Return);
        Assert.Equal([uri, uri], shell.Urls.Opened);
    }

    [Fact]
    public void Start_pins_move_and_unpin_in_the_file_Start_keeps()
    {
        var services = new FakeServices();
        services.Apps.Add("edge", "Microsoft Edge", "edge");
        services.Apps.Add("explorer", "File Explorer", "explorer");
        services.Apps.Add("notepad", "Notepad", "notepad");
        using var shell = Open(files: new() { ["launcher.json"] = """{"pins":["edge","gone","explorer","notepad"]}""" }, services: services);
        Show(shell, "start", "pins");

        Assert.Equal(["Microsoft Edge", "File Explorer", "Notepad"], Titles(shell, "pinList"));
        Assert.False(RowButton(shell, "pinList", "edge", "up").Enabled);

        shell.Click(RowButton(shell, "pinList", "edge", "down"));
        Assert.Equal(["File Explorer", "Microsoft Edge", "Notepad"], Titles(shell, "pinList"));
        // A pin Start cannot show keeps its place.
        Assert.Equal(["gone", "explorer", "edge", "notepad"], Strings(Saved(shell, "launcher.json").GetProperty("pins")));
        Assert.True(RowButton(shell, "pinList", "edge", "down").ActiveFocus);

        shell.Click(RowButton(shell, "pinList", "explorer", "remove"));
        Assert.Equal(["Microsoft Edge", "Notepad"], Titles(shell, "pinList"));
        Assert.Equal(["gone", "edge", "notepad"], Strings(Saved(shell, "launcher.json").GetProperty("pins")));
        Assert.True(RowButton(shell, "pinList", "edge", "remove").ActiveFocus);
        Assert.Equal("Unpin Microsoft Edge", Node(shell, RowButton(shell, "pinList", "edge", "remove")).Name);
        AssertQuiet(shell);
    }

    [Fact]
    public void Without_a_pins_file_the_page_shows_the_pins_Start_starts_with()
    {
        var services = new FakeServices();
        services.Apps.Add("calc", "Calculator", "calc");
        services.Apps.Add("edge", "Microsoft Edge", "edge");
        using var shell = Open(services: services);
        Show(shell, "start", "pins");

        Assert.Equal(["Microsoft Edge", "Calculator"], Titles(shell, "pinList"));
        Assert.False(File.Exists(Path.Combine(shell.Directory, "launcher.json")));
    }

    [Fact]
    public void Quick_settings_tiles_move_come_off_and_go_back_in_the_file_quick_settings_keeps()
    {
        using var shell = Open(files: new() { ["quick-settings.json"] = """{ "pinned": ["bluetooth", "wifi", "custom"] }""" });
        Show(shell, "quickSettings");

        string[] Addable() => DefaultShellHarness.FindAll(shell.Root).Where(i => i.ObjectName.StartsWith("add:", StringComparison.Ordinal) && i.Visible)
            .OrderBy(i => i.MapToScene(0, 0).Y).Select(i => i.ObjectName[4..]).ToArray();
        Assert.Equal(["bluetooth", "wifi"], Keys(shell, "tileList"));
        Assert.Equal(["airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"], Addable());

        shell.Click(RowButton(shell, "tileList", "wifi", "up"));
        Assert.Equal(["wifi", "bluetooth"], Keys(shell, "tileList"));
        // A tile this Winshell does not know keeps its place.
        Assert.Equal(["wifi", "bluetooth", "custom"], Strings(Saved(shell, "quick-settings.json").GetProperty("pinned")));

        shell.Click(RowButton(shell, "tileList", "bluetooth", "remove"));
        shell.Click(DefaultShellHarness.FindAll(shell.Find("add:nightLight")).Single(i => i.ObjectName == "add"));
        Assert.Equal(["wifi", "nightLight"], Keys(shell, "tileList"));
        Assert.Equal(["wifi", "custom", "nightLight"], Strings(Saved(shell, "quick-settings.json").GetProperty("pinned")));
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "powerMode", "accessibility"], Addable());
        Assert.Equal("Add Bluetooth", Node(shell, DefaultShellHarness.FindAll(shell.Find("add:bluetooth")).Single(i => i.ObjectName == "add")).Name);
        AssertQuiet(shell);
    }

    [Fact]
    public void The_tiles_page_has_the_tiles_quick_settings_has_in_the_same_order()
    {
        using var shell = Open("""
            import QtQuick
            import "settings.js" as Settings
            Item {
              property var catalog: Settings.tiles.map(t => t.key + "|" + t.title + "|" + t.glyph)
              property var actual: (content.tiles ?? []).map(t => t.key + "|" + t.title + "|" + t.glyph)
              property var pinned: content.pinned
              QuickSettingsContent { id: content }
            }
            """, width: 360, height: 640);
        var catalog = ValueConverter.ToList(shell.Root.ReadProperty("catalog")).Cast<string>().ToArray();
        Assert.Equal(catalog, ValueConverter.ToList(shell.Root.ReadProperty("actual")).Cast<string>());
        Assert.Equal(catalog.Select(t => t.Split('|')[0]), ValueConverter.ToList(shell.Root.ReadProperty("pinned")).Cast<string>());
    }

    private const string Notifications = """
        import Winshell
        import Winshell.Notifications
        import QtQuick
        Item {
          property bool ready: false
          onReadyChanged: Preferences.path = Shell.shellDir + "/preferences.json"
          QtObject { id: chat; property string appId: "Contoso.Chat"; property string appName: "Chat"; property var notifications: [] }
          QtObject { id: mail; property string appId: "Fabrikam.Mail"; property string appName: "Mail"; property var notifications: [] }
          QtObject { id: nameless; property string appId: ""; property string appName: ""; property var notifications: [] }
          QtObject {
            id: listener
            property int doNotDisturbState: NotificationState.On
            property int focusState: NotificationState.Off
            property int accessStatus: NotificationAccessStatus.Allowed
            property string error: ""
            property var groups: QtObject { property var values: [chat, mail, nameless] }
            property var notifications: QtObject { property var values: [] }
          }
          SettingsView {
            width: 1000
            height: 700
            notificationListener: listener
            page: "notifications"
          }
          NotificationCenter {
            objectName: "center"
            x: 1000
            width: 360
            height: 700
            listener: listener
          }
        }
        """;

    [Fact]
    public void Notifications_reads_Do_not_disturb_and_turns_an_app_off_in_the_notification_center()
    {
        using var shell = Open(Notifications, width: 1360);

        Assert.Equal("On", shell.Find("doNotDisturbState").ReadProperty("text"));
        Assert.True(shell.Has("app:Contoso.Chat"));
        Assert.True(shell.Has("app:Fabrikam.Mail"));
        string[] Groups() => DefaultShellHarness.FindAll(shell.Find("center")).OfType<Text>().Where(t => t.Visible)
            .Select(t => t.Text_).Where(t => t is "Chat" or "Mail").ToArray();
        Assert.Equal(["Chat", "Mail"], Groups());

        var chat = DefaultShellHarness.FindAll(shell.Find("app:Contoso.Chat")).OfType<Switch>().Single();
        Assert.True(chat.Checked);
        Assert.Equal("Show notifications from Chat", Node(shell, chat).Name);
        shell.Click(chat);

        // The card stays, with the keyboard on it.
        Assert.Same(chat, DefaultShellHarness.FindAll(shell.Find("app:Contoso.Chat")).OfType<Switch>().Single());
        Assert.False(chat.Checked);
        Assert.Equal(["Mail"], Groups());
        var hidden = Saved(shell).GetProperty("hiddenNotificationApps").EnumerateArray().Single();
        Assert.Equal("Contoso.Chat", hidden.GetProperty("id").GetString());
        Assert.Equal("Chat", hidden.GetProperty("name").GetString());

        shell.Click(chat);
        Assert.Equal(["Chat", "Mail"], Groups());
        Assert.Empty(Saved(shell).GetProperty("hiddenNotificationApps").EnumerateArray());
    }

    [Fact]
    public void Clear_all_leaves_the_notifications_of_an_app_turned_off_alone()
    {
        var qml = Notifications
            .Replace("""property var notifications: [] }""", """property var notifications: []; property int dismissed: 0; function dismiss() { dismissed++ } }""")
            .Replace("values: [chat, mail, nameless]", "values: onlyChat ? [chat] : [chat, mail]")
            .Replace("property bool ready: false", "property bool ready: false; property bool onlyChat: false; property int chatCleared: chat.dismissed; property int mailCleared: mail.dismissed");
        using var shell = Open(qml, width: 1360,
            files: new() { ["preferences.json"] = """{ "hiddenNotificationApps": [{ "id": "Contoso.Chat", "name": "Chat" }] }""" });
        var center = shell.Find("center");
        Button ClearAll() => DefaultShellHarness.FindAll(center).OfType<Button>().Single(b => b.Text == "Clear all");
        Text CaughtUp() => DefaultShellHarness.FindAll(center).OfType<Text>().Single(t => t.Text_ == "You're all caught up");

        Assert.True(ClearAll().Enabled);
        Assert.False(CaughtUp().Visible);
        shell.Click(ClearAll());
        Assert.Equal(0, shell.Root.ReadProperty("chatCleared"));
        Assert.Equal(1, shell.Root.ReadProperty("mailCleared"));

        // Only the hidden app has notifications left: the center is empty and has nothing to clear.
        shell.Root.WriteProperty("onlyChat", true);
        shell.Settle();
        Assert.False(ClearAll().Enabled);
        Assert.True(CaughtUp().Visible);
    }

    [Fact]
    public void An_app_turned_off_earlier_stays_listed_by_name_without_notifications()
    {
        using var shell = Open(Notifications.Replace("values: [chat, mail, nameless]", "values: []"), width: 1360,
            files: new() { ["preferences.json"] = """{ "hiddenNotificationApps": [{ "id": "Contoso.Chat", "name": "Chat" }] }""" });

        Assert.True(shell.Has("app:Contoso.Chat"));
        Assert.False(DefaultShellHarness.FindAll(shell.Find("app:Contoso.Chat")).OfType<Switch>().Single().Checked);
    }

    [Fact]
    public void Hotkeys_save_the_sequence_typed_and_say_when_another_app_has_it()
    {
        using var shell = Open("""
            import Winshell
            import QtQuick
            Item {
              QtObject { id: start; property bool active: true; property bool taken: false; property string error: "" }
              QtObject { id: settings; property bool active: false; property bool taken: true; property string error: "" }
              SettingsView {
                anchors.fill: parent
                page: "hotkeys"
                startHotkey: start
                settingsHotkey: settings
                Component.onCompleted: Preferences.path = Shell.shellDir + "/preferences.json"
              }
            }
            """);

        Assert.Equal("On", Card(shell, "hotkeyStart").ReadProperty("description"));
        Assert.Equal("Off", Card(shell, "hotkeySettings").ReadProperty("description"));

        var field = Control<TextField>(shell, "hotkeySettings");
        Assert.Equal("Open Winshell Settings", Node(shell, field).Name);
        field.ForceActiveFocus();
        Type(shell, "Ctrl+Alt+S");
        shell.Key(Return);
        Assert.Equal("Ctrl+Alt+S", Saved(shell).GetProperty("settingsHotkey").GetString());
        Assert.Equal("Windows or another app is using Ctrl+Alt+S", Card(shell, "hotkeySettings").ReadProperty("description"));
    }

    [Theory]
    [InlineData("S", "Start it with Win, Ctrl or Alt, so S still types in other apps")]
    [InlineData("Shift+S", "Start it with Win, Ctrl or Alt, so Shift+S still types in other apps")]
    [InlineData("Space, Ctrl+K", "Start it with Win, Ctrl or Alt, so Space, Ctrl+K still types in other apps")]
    [InlineData("win", "Open Start already uses Win")]
    public void A_hotkey_that_would_take_a_key_from_every_app_or_repeats_the_other_is_not_saved(string typed, string reason)
    {
        using var shell = Open("""
            import Winshell
            import QtQuick
            SettingsView {
              anchors.fill: parent
              page: "hotkeys"
              Component.onCompleted: Preferences.path = Shell.shellDir + "/preferences.json"
            }
            """);
        var field = Control<TextField>(shell, "hotkeySettings");
        field.ForceActiveFocus();
        Type(shell, "Ctrl+Alt+S");
        shell.Key(Return);
        Assert.Equal("Ctrl+Alt+S", Saved(shell).GetProperty("settingsHotkey").GetString());

        field.WriteProperty("text", "");
        Type(shell, typed);
        shell.Key(Return);
        Assert.Equal("Ctrl+Alt+S", Saved(shell).GetProperty("settingsHotkey").GetString());
        Assert.Equal("Ctrl+Alt+S", field.ReadProperty("text"));
        Assert.Equal(reason, Card(shell, "hotkeySettings").ReadProperty("description"));

        // The next sequence that can be taken is saved, and the reason goes.
        field.WriteProperty("text", "");
        Type(shell, "Win+Shift+S");
        shell.Key(Return);
        Assert.Equal("Win+Shift+S", Saved(shell).GetProperty("settingsHotkey").GetString());
        Assert.NotEqual(reason, Card(shell, "hotkeySettings").ReadProperty("description"));
    }

    [Fact]
    public void Startup_turns_the_sign_in_entry_on_and_says_when_Windows_turned_it_off()
    {
        var windows = new FakeStartupRegistry();
        using var shell = Open(configure: e => e.AddService<IStartupRegistry>(windows));
        Show(shell, "startup");

        var toggle = Control<Switch>(shell, "runAtSignIn");
        Assert.True(toggle.Enabled);
        Assert.False(toggle.Checked);
        shell.Click(toggle);
        Assert.True(windows.Registered);
        Assert.True(toggle.Checked);

        // The Startup apps page turned it off while the page was away: coming back reads it again.
        windows.TurnedOff = true;
        Show(shell, "about");
        Show(shell, "startup");
        Assert.Equal("Turned off in Startup apps in Windows, which can turn it back on", Card(shell, "runAtSignIn").ReadProperty("description"));
        // Off, as Windows' page shows it, and only that page turns it back on.
        toggle = Control<Switch>(shell, "runAtSignIn");
        Assert.False(toggle.Checked);
        Assert.False(toggle.Enabled);
    }

    [Fact]
    public void An_entry_that_starts_another_winshell_reads_off_and_turning_it_on_points_it_here()
    {
        var windows = new FakeStartupRegistry { Registered = true, Elsewhere = true };
        using var shell = Open(configure: e => e.AddService<IStartupRegistry>(windows));
        Show(shell, "startup");

        var toggle = Control<Switch>(shell, "runAtSignIn");
        Assert.False(toggle.Checked);
        Assert.True(toggle.Enabled);
        Assert.Equal("Set to start Winshell from another folder or another way. Turn it on to start this one", Card(shell, "runAtSignIn").ReadProperty("description"));

        shell.Click(toggle);
        Assert.False(windows.Elsewhere);
        Assert.True(toggle.Checked);
        Assert.NotEqual("Set to start Winshell from another folder or another way. Turn it on to start this one", Card(shell, "runAtSignIn").ReadProperty("description"));
    }

    [Fact]
    public void A_refused_sign_in_entry_puts_the_switch_back()
    {
        var windows = new FakeStartupRegistry { Accepts = false };
        using var shell = new DefaultShellHarness(View, 1000, 700, configure: e => e.AddService<IStartupRegistry>(windows));
        Show(shell, "startup");

        var toggle = Control<Switch>(shell, "runAtSignIn");
        shell.Click(toggle);
        Assert.False(toggle.Checked);
        Assert.Contains(shell.Messages, m => m.Contains("Windows did not let Winshell register", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_winshell_exe_the_sign_in_switch_is_off_and_says_why()
    {
        using var shell = Open();
        Show(shell, "startup");
        Assert.False(Control<Switch>(shell, "runAtSignIn").Enabled);
        Assert.Equal("Only a shell started by winshell.exe can run at sign-in", Card(shell, "runAtSignIn").ReadProperty("description"));
    }

    [Fact]
    public void About_shows_the_version()
    {
        using var shell = Open();
        Show(shell, "about");
        Assert.Equal(ProductVersion.Current.Text, Card(shell, "version").ReadProperty("description"));
        // The harness has no config folder or log, so there is nothing to open.
        Assert.False(shell.Find("openConfigFolder").Enabled);
        Assert.False(shell.Find("openLog").Enabled);
    }

    [Fact]
    public void Cards_are_laid_out_as_Windows_Settings_lays_them_out()
    {
        using var shell = Open();
        Assert.Equal(264, shell.Find("navigation").Width);
        var mode = Card(shell, "colorScheme");
        var at = mode.MapToScene(0, 0);
        Assert.Equal(280 + 40, at.X);
        Assert.Equal(1000 - 280 - 80, mode.Width);
        Assert.Equal(68, mode.Height);
        Assert.Equal(4, Card(shell, "accentColor").MapToScene(0, 0).Y - (at.Y + mode.Height));
        var box = Control<ComboBox>(shell, "colorScheme");
        Assert.Equal(at.X + mode.Width - 16, box.MapToScene(box.Width, 0).X, 1);
        Assert.Equal(at.Y + (mode.Height - box.Height) / 2, box.MapToScene(0, 0).Y, 1);

        // At the narrowest window, the control goes under the text, inside the card.
        using var narrow = Open(width: 680, height: 480);
        var card = Card(narrow, "colorScheme");
        var combo = Control<ComboBox>(narrow, "colorScheme");
        var title = DefaultShellHarness.FindAll(card).Single(i => i.ObjectName == "cardTitle");
        Assert.Equal(680 - 280 - 80, card.Width);
        Assert.Equal(title.MapToScene(0, 0).X, combo.MapToScene(0, 0).X);
        Assert.True(combo.MapToScene(0, 0).Y > title.MapToScene(0, title.Height).Y);
        Assert.True(combo.MapToScene(0, combo.Height).Y < card.MapToScene(0, card.Height).Y);
        Assert.True(combo.MapToScene(combo.Width, 0).X <= card.MapToScene(card.Width, 0).X);
    }

    [Fact]
    public void Narrator_reads_each_control_by_its_setting()
    {
        using var shell = Open();
        foreach (var page in Pages)
        {
            Show(shell, page);
            foreach (var node in Nodes(shell).Where(n => n.Item is ComboBox or Switch or TextField or Button && n.Item.Visible))
                Assert.False(string.IsNullOrWhiteSpace(node.Name), $"A {node.Role} on {page} has no name.");
        }
        Show(shell, "taskbar");
        var autoHide = Node(shell, Control<Switch>(shell, "taskbarAutoHide"));
        Assert.Equal("Automatically hide the taskbar", autoHide.Name);
        Assert.Equal(ToggleState.Off, autoHide.Toggle);
        Assert.Equal("Taskbar alignment", Node(shell, Control<ComboBox>(shell, "taskbarAlignment")).Name);
    }

    [Fact]
    public void Every_page_draws_in_light_dark_and_contrast_at_each_scale()
    {
        var saved = Theme.Current;
        var desert = new SystemColors(
            Window: QColor.Parse("#fffaef"), WindowText: QColor.Parse("#3d3d3d"), Highlight: QColor.Parse("#903909"),
            HighlightText: QColor.Parse("#fff5e3"), Hotlight: QColor.Parse("#1c5e75"), GrayText: QColor.Parse("#676767"),
            ButtonFace: QColor.Parse("#fffaef"), ButtonText: QColor.Parse("#202020"));
        try
        {
            foreach (var (name, theme) in new[] { ("light", Theme.Default(false)), ("dark", Theme.Default(true)), ("contrast", Theme.Default(false) with { System = desert, IsHighContrast = true }) })
            {
                Theme.Current = theme;
                using var shell = Open();
                foreach (var page in Pages)
                {
                    Show(shell, page);
                    using var bitmap = Paint(shell);
                    // The page title is drawn in the primary text color.
                    var title = shell.Find("crumb:" + page);
                    var titleAt = title.MapToScene(0, 0);
                    Assert.True(Has(bitmap, Ink(theme), (int)titleAt.X, (int)titleAt.Y, (int)title.Width, (int)title.Height), $"{page} in {name} has no title");
                    Save(bitmap, $"{page}-{name}");
                }
                foreach (var scale in new[] { 1.5, 2.0 })
                {
                    Show(shell, "taskbar");
                    shell.Scene.DevicePixelRatio = scale;
                    shell.Settle();
                    using var bitmap = Paint(shell);
                    Save(bitmap, $"taskbar-{name}-{scale * 100}");
                    var title = shell.Find("crumb:taskbar");
                    var titleAt = title.MapToScene(0, 0);
                    Assert.True(Has(bitmap, Ink(theme), (int)(titleAt.X * scale), (int)(titleAt.Y * scale), (int)(title.Width * scale), (int)(title.Height * scale)), $"taskbar in {name} at {scale} has no title");
                }
                AssertQuiet(shell);
            }
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    // Primary text as it shows on the Mica base color: in light and dark it is not quite opaque.
    private static QColor Ink(Theme theme) => QColor.Tint(theme.SolidBackgroundFillColorBase, theme.TextFillColorPrimary);

    private static bool Has(SKBitmap bitmap, QColor color, int x, int y, int width, int height)
    {
        for (var py = Math.Max(0, y); py < Math.Min(bitmap.Height, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(bitmap.Width, x + width); px++)
            {
                var p = bitmap.GetPixel(px, py);
                if (Math.Abs(p.Red - color.R8) <= 24 && Math.Abs(p.Green - color.G8) <= 24 && Math.Abs(p.Blue - color.B8) <= 24)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Paints the scene on the Mica base color, once color changes have finished fading, in physical pixels at the
    /// scene's scale. A renderer scale is not a change to this PC's display settings.
    /// </summary>
    private static SKBitmap Paint(DefaultShellHarness shell)
    {
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        shell.Settle();
        var scale = shell.Scene.DevicePixelRatio;
        var bitmap = new SKBitmap((int)(shell.Scene.Root.Width * scale), (int)(shell.Scene.Root.Height * scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Theme.Current.SolidBackgroundFillColorBase.ToSk());
        shell.Scene.Paint(canvas, shell.Loop.Now);
        return bitmap;
    }

    /// <summary>Writes a render to the folder WINSHELL_SETTINGS_EVIDENCE names, if any. Nothing is committed.</summary>
    private static void Save(SKBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("WINSHELL_SETTINGS_EVIDENCE") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), png.ToArray());
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public bool Registered { get; set; }

        public bool TurnedOff { get; set; }

        public bool Accepts { get; set; } = true;

        public bool Elsewhere { get; set; }

        public bool IsRegistered() => Registered;

        public bool PointsElsewhere() => Elsewhere;

        public bool TurnedOffInWindows() => TurnedOff;

        public bool Register()
        {
            if (Accepts)
                (Registered, Elsewhere) = (true, false);
            return Accepts;
        }

        public bool Unregister()
        {
            if (Accepts)
                Registered = false;
            return Accepts;
        }
    }
}
