using System.Reflection;
using SkiaSharp;
using Winshell.Platform.Windows.Services.Network;
using Winshell.Platform.Windows.Services.Power;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>The default shell's quick settings, against fake services.</summary>
[Collection(ThemeCollection.Name)]
public class QuickSettingsTests
{
    private const string Content = """
        import QtQuick
        QuickSettingsContent {
          property int closed: 0
          onCloseRequested: closed++
        }
        """;

    private const int Escape = 0x01000000;
    private const int Delete = 0x01000007;
    private const int Right = 0x01000014;
    private const int Down = 0x01000015;
    private const int AltShift = 0x08000000 | 0x02000000;

    private static readonly string[] DefaultTiles = ["wifi", "bluetooth", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"];

    /// <summary>The content with its layout kept in layout.json in the shell's temporary folder.</summary>
    private const string Saved = """
        import Winshell
        import QtQuick
        QuickSettingsContent {
          property int closed: 0
          layoutPath: Shell.shellDir + "/layout.json"
          onCloseRequested: closed++
        }
        """;

    /// <summary>A laptop on Wi-Fi with Bluetooth headphones, a battery and a built-in panel.</summary>
    private static FakeServices Laptop()
    {
        var services = new FakeServices();
        var home = new FakeNetwork("Home") { Connected = true };
        var wifi = new FakeNetworkDevice(DeviceType.Wifi, new FakeNetwork("Neighbour"), home) { Connected = true };
        services.Networking.Devices.Add(wifi);
        services.Networking.WifiEnabled = true;
        services.Networking.WifiState = WifiRadioState.On;
        services.Networking.Connectivity = NetworkConnectivity.InternetAccess;
        services.Bluetooth.DefaultAdapter = new FakeBluetoothAdapter { Enabled = true };
        services.Bluetooth.Devices.Add(new FakeBluetoothDevice("Mouse"));
        services.Bluetooth.Devices.Add(new FakeBluetoothDevice("Headphones") { Connected = true });
        services.Power.DisplayDevice.IsPresent = true;
        services.Power.DisplayDevice.Percentage = 0.85;
        services.Power.DisplayDevice.State = PowerDeviceState.Discharging;
        services.Brightness.Monitors.Add(new FakeBrightnessMonitor(BrightnessConnection.External, 0, 100) { Brightness = 70 });
        services.Brightness.Monitors.Add(new FakeBrightnessMonitor(BrightnessConnection.Internal, 0, 100) { Brightness = 40 });
        var speakers = new FakeAudioNode("Speakers");
        speakers.Audio.Volume = 0.3;
        services.Audio.Nodes.Add(speakers);
        services.Audio.DefaultPlaybackDevice = speakers;
        return services;
    }

    /// <summary>A desktop on Ethernet: no Wi-Fi, no Bluetooth, no battery, no built-in panel, no power modes.</summary>
    private static FakeServices Desktop()
    {
        var services = new FakeServices();
        services.Networking.Devices.Add(new FakeNetworkDevice(DeviceType.Wired) { Connected = true });
        services.Networking.Connectivity = NetworkConnectivity.InternetAccess;
        services.PowerMode.Available = false;
        services.Brightness.Monitors.Add(new FakeBrightnessMonitor(BrightnessConnection.External, 0, 100) { Brightness = 70 });
        return services;
    }

    private static Item Tile(DefaultShellHarness shell, string key) => shell.Find("tile:" + key);

    private static string[] Tiles(DefaultShellHarness shell) =>
        shell.Find("tiles").ChildItems.Where(i => i.Visible && i.ObjectName.StartsWith("tile:", StringComparison.Ordinal))
            .Select(i => i.ObjectName["tile:".Length..]).ToArray();

    private static Item Button(DefaultShellHarness shell, string tile) =>
        DefaultShellHarness.FindAll(Tile(shell, tile)).Single(i => i.ObjectName == "tileButton");

    private static Item OutputChevron(DefaultShellHarness shell) =>
        DefaultShellHarness.FindAll(shell.Find("volume")).Single(i => i.ObjectName == "page");

    private static Slider Slider(DefaultShellHarness shell, string row) =>
        (Slider)DefaultShellHarness.FindAll(shell.Find(row)).Single(i => i.ObjectName == "slider");

    [Fact]
    public void Tiles_show_what_the_services_report()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        Assert.Empty(shell.Messages);
        Assert.Equal(DefaultTiles, Tiles(shell));

        Assert.Equal("Home", Tile(shell, "wifi").ReadProperty("label"));
        Assert.True((bool)Tile(shell, "wifi").ReadProperty("on")!);
        Assert.Equal("Headphones", Tile(shell, "bluetooth").ReadProperty("label"));
        Assert.True((bool)Tile(shell, "bluetooth").ReadProperty("on")!);
        Assert.False((bool)Tile(shell, "airplaneMode").ReadProperty("on")!);
        Assert.False((bool)Tile(shell, "energySaver").ReadProperty("on")!);
        Assert.Equal("Balanced", Tile(shell, "powerMode").ReadProperty("description"));

        var services = shell.Services;
        services.Networking.WifiEnabled = false;
        ((FakeNetwork)((FakeNetworkDevice)services.Networking.Devices.Values[0]!).Networks.Values[1]!).Connected = false;
        services.Bluetooth.DefaultAdapter!.Enabled = false;
        ((FakeBluetoothDevice)services.Bluetooth.Devices.Values[1]!).Connected = false;
        services.Networking.AirplaneMode = AirplaneModeState.On;
        services.PowerMode.Effective = EffectivePowerMode.EnergySaver;
        services.PowerMode.Mode = PowerModeLevel.BestPerformance;
        shell.Settle();

        Assert.Equal("Wi-Fi", Tile(shell, "wifi").ReadProperty("label"));
        Assert.False((bool)Tile(shell, "wifi").ReadProperty("on")!);
        Assert.Equal("Bluetooth", Tile(shell, "bluetooth").ReadProperty("label"));
        Assert.False((bool)Tile(shell, "bluetooth").ReadProperty("on")!);
        Assert.True((bool)Tile(shell, "airplaneMode").ReadProperty("on")!);
        Assert.True((bool)Tile(shell, "energySaver").ReadProperty("on")!);
        Assert.Equal("Best performance", Tile(shell, "powerMode").ReadProperty("description"));
    }

    [Fact]
    public void Tiles_a_PC_has_no_hardware_for_are_left_out()
    {
        using var shell = new DefaultShellHarness(Content, services: Desktop());
        Assert.Equal(["airplaneMode", "energySaver", "nightLight", "accessibility"], Tiles(shell));
        Assert.False(shell.Has("battery"));
        Assert.False(shell.Has("brightness"));

        shell.Services.Bluetooth.DefaultAdapter = new FakeBluetoothAdapter();
        shell.Settle();
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "nightLight", "accessibility"], Tiles(shell));
    }

    [Theory]
    [InlineData("wifi", "ms-settings:network-wifi")]
    [InlineData("bluetooth", "ms-settings:bluetooth")]
    [InlineData("airplaneMode", "ms-settings:network-airplanemode")]
    [InlineData("energySaver", "ms-settings:batterysaver")]
    [InlineData("nightLight", "ms-settings:nightlight")]
    [InlineData("accessibility", "ms-settings:easeofaccess")]
    public void A_tile_Winshell_cannot_change_opens_Settings_and_closes_quick_settings(string tile, string uri)
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        shell.Click(Button(shell, tile));
        Assert.Equal([uri], shell.Urls.Opened);
        Assert.Equal(1, shell.Root.ReadProperty("closed"));
        Assert.True(shell.Services.Networking.WifiEnabled);
        Assert.True(shell.Services.Bluetooth.DefaultAdapter!.Enabled);
        Assert.Equal(AirplaneModeState.Off, shell.Services.Networking.AirplaneMode);
    }

    [Fact]
    public void The_power_mode_page_sets_the_mode_the_user_picks()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        shell.Click(Button(shell, "powerMode"));
        Assert.Empty(shell.Urls.Opened);
        Assert.False(shell.Has("mainPage"));
        Assert.True((bool)shell.Find("powerMode:1").ReadProperty("checked")!);

        shell.Click(shell.Find("powerMode:2"));
        Assert.Equal(PowerModeLevel.BestPerformance, shell.Services.PowerMode.Mode);

        // Windows changing the mode moves the choice with it.
        shell.Services.PowerMode.Mode = PowerModeLevel.BestEfficiency;
        shell.Settle();
        Assert.True((bool)shell.Find("powerMode:0").ReadProperty("checked")!);
        Assert.False((bool)shell.Find("powerMode:2").ReadProperty("checked")!);

        shell.Click(shell.Find("back"));
        Assert.True(shell.Has("mainPage"));
        Assert.Equal(0, shell.Root.ReadProperty("closed"));
    }

    [Fact]
    public void Escape_closes_quick_settings_from_a_focused_tile()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        Button(shell, "wifi").ForceActiveFocus();
        shell.Key(Escape);
        Assert.Equal(1, shell.Root.ReadProperty("closed"));
    }

    [Fact]
    public void Escape_leaves_a_page_then_closes_quick_settings()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        shell.Click(Button(shell, "powerMode"));
        shell.Key(Escape);
        Assert.True(shell.Has("mainPage"));
        Assert.Equal(0, shell.Root.ReadProperty("closed"));
        shell.Key(Escape);
        Assert.Equal(1, shell.Root.ReadProperty("closed"));
    }

    [Fact]
    public void The_volume_slider_follows_and_sets_the_default_playback_device()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        var speakers = shell.Services.Audio.DefaultPlaybackDevice!;
        var slider = Slider(shell, "volume");
        Assert.Equal(0.3, slider.Value, 3);

        shell.Click(slider, 0.8);
        Assert.InRange(speakers.Audio.Volume, 0.7, 0.9);

        speakers.Audio.Volume = 0.5;
        shell.Settle();
        Assert.Equal(0.5, slider.Value, 3);

        var mute = DefaultShellHarness.FindAll(shell.Find("volume")).Single(i => i.ObjectName == "glyph");
        shell.Click(mute);
        Assert.True(speakers.Audio.Muted);
        Assert.Equal(0.5, speakers.Audio.Volume, 3);

        shell.Services.Audio.DefaultPlaybackDevice = null;
        shell.Settle();
        Assert.False(shell.Find("volume").Enabled);
    }

    [Fact]
    public void The_brightness_slider_sets_the_built_in_panel_only()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        var builtIn = (FakeBrightnessMonitor)shell.Services.Brightness.Monitors.Values[1]!;
        var external = (FakeBrightnessMonitor)shell.Services.Brightness.Monitors.Values[0]!;
        var slider = Slider(shell, "brightness");
        Assert.Equal(0.4, slider.Value, 3);

        shell.Click(slider, 0.8);
        Assert.InRange(builtIn.Brightness, 70, 90);
        Assert.Equal(70, external.Brightness);
    }

    [Fact]
    public void The_footer_shows_the_battery_and_opens_Settings()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        var battery = shell.Find("battery");
        Assert.Equal("Battery 85%", battery.ReadProperty("text"));

        shell.Services.Power.DisplayDevice.State = PowerDeviceState.Charging;
        shell.Settle();
        Assert.Equal("Battery 85%, charging", battery.ReadProperty("text"));

        shell.Click(shell.Find("settings"));
        Assert.Equal(["ms-settings:"], shell.Urls.Opened);
        shell.Click(battery);
        Assert.Equal(["ms-settings:", "ms-settings:batterysaver"], shell.Urls.Opened);
    }

    [Fact]
    public void The_output_page_makes_the_device_the_user_picks_the_default()
    {
        var services = Laptop();
        var headset = new FakeAudioNode("Headset");
        services.Audio.Nodes.Add(new FakeAudioNode("Microphone", isPlayback: false));
        services.Audio.Nodes.Add(new FakeAudioNode("Music app", isSession: true));
        services.Audio.Nodes.Add(headset);
        using var shell = new DefaultShellHarness(Content, services: services);

        shell.Click(OutputChevron(shell));
        Assert.False(shell.Has("mainPage"));
        Assert.Equal("Speakers", shell.Find("output:0").ReadProperty("text"));
        Assert.Equal("Headset", shell.Find("output:1").ReadProperty("text"));
        Assert.False(shell.Has("output:2"));
        Assert.True((bool)shell.Find("output:0").ReadProperty("checked")!);

        shell.Click(shell.Find("output:1"));
        Assert.Same(headset, services.Audio.DefaultPlaybackDevice);
        Assert.False((bool)shell.Find("output:0").ReadProperty("checked")!);

        // Windows changing the default moves the choice with it.
        services.Audio.DefaultPlaybackDevice = (FakeAudioNode)services.Audio.Nodes.Values[0]!;
        shell.Settle();
        Assert.True((bool)shell.Find("output:0").ReadProperty("checked")!);
        Assert.False((bool)shell.Find("output:1").ReadProperty("checked")!);

        shell.Click(shell.Find("moreVolume"));
        Assert.Equal(["ms-settings:apps-volume"], shell.Urls.Opened);
        Assert.Equal(1, shell.Root.ReadProperty("closed"));
    }

    [Fact]
    public void The_output_list_scrolls_inside_quick_settings_when_there_are_many_devices()
    {
        var services = Laptop();
        for (var i = 0; i < 20; i++)
            services.Audio.Nodes.Add(new FakeAudioNode($"Monitor {i}"));
        using var shell = new DefaultShellHarness(Content, services: services);
        shell.Click(OutputChevron(shell));
        var more = shell.Find("moreVolume");
        Assert.True(more.MapToScene(0, 0).Y + more.Height <= shell.Find("footer").MapToScene(0, 0).Y);
    }

    [Fact]
    public void The_media_card_shows_the_playing_session_and_its_controls()
    {
        var services = Laptop();
        var paused = new FakeMediaSession("Spotify", "Paused song", "Someone");
        var playing = new FakeMediaSession("Firefox", "A video", "A channel") { IsPlaying = true };
        services.Media.Sessions.Add(paused);
        services.Media.Sessions.Add(playing);
        using var shell = new DefaultShellHarness(Content, services: services);

        Assert.Equal("A video", shell.Find("mediaTitle").ReadProperty("text"));
        Assert.Equal("A channel", shell.Find("mediaArtist").ReadProperty("text"));
        Assert.False(shell.Has("mediaArt"));
        Assert.False(shell.Find("previous").Enabled);
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        Assert.Equal("Pause", tree.Find(shell.Find("playPause"))!.Name);

        shell.Click(shell.Find("next"));
        shell.Click(shell.Find("playPause"));
        Assert.Equal(["next", "togglePlaying"], playing.Calls);
        Assert.Empty(paused.Calls);

        // Nothing plays now, so the card shows the first session.
        Assert.Equal("Paused song", shell.Find("mediaTitle").ReadProperty("text"));
        tree.Refresh();
        Assert.Equal("Play", tree.Find(shell.Find("playPause"))!.Name);

        services.Media.Sessions.RemoveAt(1);
        services.Media.Sessions.RemoveAt(0);
        shell.Settle();
        Assert.False(shell.Has("media"));
    }

    private static string[] SavedTiles(DefaultShellHarness shell) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(shell.Directory, "layout.json")))
            .RootElement.GetProperty("pinned").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static DefaultShellHarness Editing(FakeServices? services = null)
    {
        var shell = new DefaultShellHarness(Saved, services: services ?? Laptop());
        shell.Click(shell.Find("edit"));
        return shell;
    }

    [Fact]
    public void Edit_mode_unpins_and_adds_tiles_and_saves_them()
    {
        using var shell = Editing();
        Assert.True((bool)shell.Root.ReadProperty("editing")!);
        Assert.False(shell.Has("battery"));
        Assert.False(shell.Has("add:wifi"));
        Assert.False(shell.Find("add").Enabled);

        // A tile does nothing while it is being edited.
        shell.Click(Button(shell, "wifi"));
        Assert.Empty(shell.Urls.Opened);

        shell.Click(DefaultShellHarness.FindAll(Tile(shell, "bluetooth")).Single(i => i.ObjectName == "unpin"));
        Assert.Equal(["wifi", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"], Tiles(shell));
        Assert.Equal(Tiles(shell), SavedTiles(shell));

        shell.Click(shell.Find("add"));
        Assert.True(shell.Has("addPage"));
        Assert.Equal("Bluetooth", shell.Find("add:bluetooth").ReadProperty("text"));
        shell.Click(shell.Find("add:bluetooth"));
        Assert.True(shell.Has("mainPage"));
        Assert.Equal(["wifi", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility", "bluetooth"], Tiles(shell));
        Assert.Equal(Tiles(shell), SavedTiles(shell));

        shell.Click(shell.Find("done"));
        Assert.False((bool)shell.Root.ReadProperty("editing")!);
        Assert.True(shell.Has("battery"));
        Assert.False(shell.Has("unpin"));
    }

    [Fact]
    public void Dragging_a_tile_moves_it_to_where_it_is_let_go()
    {
        using var shell = Editing();
        var wifi = Tile(shell, "wifi");
        var airplane = Button(shell, "airplaneMode");
        shell.Drag(Button(shell, "wifi"), airplane.MapToScene(80, 24), () =>
        {
            Assert.Equal(0.5, wifi.Opacity);
            Assert.Equal(1, Tile(shell, "airplaneMode").ReadProperty("dropSide"));
        });
        Assert.Equal(["bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "nightLight", "accessibility"], Tiles(shell));
        Assert.Equal(Tiles(shell), SavedTiles(shell));
        Assert.All(Tiles(shell), t => Assert.Equal(0, Tile(shell, t).ReadProperty("dropSide")));

        // Back to the start of a row, before the tile there.
        shell.Drag(Button(shell, "nightLight"), Button(shell, "bluetooth").MapToScene(10, 24));
        Assert.Equal(["nightLight", "bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "accessibility"], Tiles(shell));

        // A press that does not move is not a move.
        shell.Click(Button(shell, "energySaver"));
        Assert.Equal(["nightLight", "bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "accessibility"], Tiles(shell));
        Assert.Empty(shell.Urls.Opened);
    }

    [Fact]
    public void Alt_Shift_and_the_arrow_keys_move_the_focused_tile_and_Delete_unpins_it()
    {
        using var shell = Editing();
        Button(shell, "wifi").ForceActiveFocus();
        shell.Key(Right, AltShift);
        Assert.Equal(["bluetooth", "wifi", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"], Tiles(shell));
        // The keyboard stays on the tile that moved.
        Assert.True((bool)Button(shell, "wifi").ReadProperty("activeFocus")!);

        shell.Key(Down, AltShift);
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "powerMode", "wifi", "nightLight", "accessibility"], Tiles(shell));
        Assert.True((bool)Button(shell, "wifi").ReadProperty("activeFocus")!);

        // Without both modifiers an arrow key only moves the focus.
        shell.Key(Right);
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "powerMode", "wifi", "nightLight", "accessibility"], Tiles(shell));

        Button(shell, "nightLight").ForceActiveFocus();
        shell.Key(Delete);
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "powerMode", "wifi", "accessibility"], SavedTiles(shell));
        // The tile that took its place has the keyboard.
        Assert.True((bool)Button(shell, "accessibility").ReadProperty("activeFocus")!);

        shell.Key(Escape);
        Assert.False((bool)shell.Root.ReadProperty("editing")!);
        Assert.Equal(0, shell.Root.ReadProperty("closed"));
    }

    [Fact]
    public void A_saved_layout_is_restored_and_keeps_tiles_this_PC_has_no_hardware_for()
    {
        var services = Desktop();
        var shell = new DefaultShellHarness(Saved, services: services);
        try
        {
            Assert.Equal(["airplaneMode", "energySaver", "nightLight", "accessibility"], Tiles(shell));
            shell.Click(shell.Find("edit"));
            shell.Click(DefaultShellHarness.FindAll(Tile(shell, "energySaver")).Single(i => i.ObjectName == "unpin"));
            // Wi-Fi, Bluetooth and power mode are hidden here, and stay pinned in their places.
            Assert.Equal(["wifi", "bluetooth", "airplaneMode", "powerMode", "nightLight", "accessibility"], SavedTiles(shell));
        }
        finally
        {
            shell.Dispose();
        }

        using var again = new DefaultShellHarness(Saved, services: Laptop());
        File.WriteAllText(Path.Combine(again.Directory, "layout.json"), """{ "pinned": ["nightLight", "gone", "wifi", 3] }""");
        again.Root.WriteProperty("layoutPath", "");
        again.Root.WriteProperty("layoutPath", Path.Combine(again.Directory, "layout.json"));
        again.Settle();
        Assert.Equal(["nightLight", "wifi"], Tiles(again));
        Assert.Empty(again.Messages);
    }

    [Fact]
    public void Quick_settings_follows_its_layout_file_as_Winshell_Settings_changes_it()
    {
        using var shell = new DefaultShellHarness(Saved, services: Laptop());
        File.WriteAllText(Path.Combine(shell.Directory, "layout.json"), """{ "pinned": ["wifi", "bluetooth"] }""");
        shell.Root.WriteProperty("layoutPath", "");
        shell.Root.WriteProperty("layoutPath", Path.Combine(shell.Directory, "layout.json"));
        shell.Settle();
        Assert.Equal(["wifi", "bluetooth"], Tiles(shell));

        // Past the time a FileView takes its own writes to be.
        Thread.Sleep(300);
        File.WriteAllText(Path.Combine(shell.Directory, "layout.json"), """{ "pinned": ["bluetooth", "nightLight"] }""");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!Tiles(shell).SequenceEqual(["bluetooth", "nightLight"]))
        {
            Assert.True(DateTime.UtcNow < until, "Quick settings did not follow the file.");
            Thread.Sleep(20);
            shell.Settle();
        }
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_layout_file_that_is_not_JSON_leaves_the_default_tiles_and_warns()
    {
        using var shell = new DefaultShellHarness(Saved, services: Laptop());
        File.WriteAllText(Path.Combine(shell.Directory, "broken.json"), "not json");
        shell.Root.WriteProperty("layoutPath", Path.Combine(shell.Directory, "broken.json"));
        shell.Settle();
        Assert.Equal(DefaultTiles, Tiles(shell));
        Assert.Contains(shell.Messages, m => m.Contains("broken.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Narrator_reads_the_edit_buttons_and_how_to_move_a_tile()
    {
        using var shell = Editing();
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        var wifi = tree.Find(Button(shell, "wifi"))!;
        Assert.Null(wifi.Toggle);
        Assert.Equal("Alt+Shift+arrow keys move it, Delete unpins it", wifi.Description);
        Assert.Equal("Unpin Wi-Fi", tree.Find(DefaultShellHarness.FindAll(Tile(shell, "wifi")).Single(i => i.ObjectName == "unpin"))!.Name);
        Assert.Equal("Done", tree.Find(shell.Find("done"))!.Name);
        shell.Click(shell.Find("done"));
        tree.Refresh();
        Assert.Equal("Edit quick settings", tree.Find(shell.Find("edit"))!.Name);
    }

    [Fact]
    public void Tiles_sit_in_three_columns_of_96_by_48_with_the_name_under_each()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        Assert.Equal(360, shell.Root.ImplicitWidth);
        var buttons = Tiles(shell).Select(t => Button(shell, t)).ToArray();
        var rowHeight = Tile(shell, "wifi").Height + 12;
        for (var i = 0; i < buttons.Length; i++)
        {
            var at = buttons[i].MapToScene(0, 0);
            Assert.Equal(24 + i % 3 * 108, at.X);
            Assert.Equal(24 + i / 3 * rowHeight, at.Y);
            Assert.Equal(96, buttons[i].Width);
            Assert.Equal(48, buttons[i].Height);
        }
        // The sliders follow the tiles, and the 48 high footer is at the bottom.
        var footer = shell.Find("footer");
        Assert.Equal(48, footer.Height);
        Assert.Equal(shell.Root.ImplicitHeight - 48, footer.MapToScene(0, 0).Y);
        Assert.True(shell.Find("brightness").MapToScene(0, 0).Y + 40 <= footer.MapToScene(0, 0).Y);
    }

    [Fact]
    public void A_tile_that_is_on_is_filled_with_the_accent_color()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            using var shell = new DefaultShellHarness(Content, services: Laptop());
            using var bitmap = shell.Paint(SKColors.Black);
            var t = Theme.Current;
            // Inside each button, clear of its glyph and border.
            var on = Button(shell, "wifi").MapToScene(10, 24);
            var off = Button(shell, "airplaneMode").MapToScene(10, 24);
            AssertNear(t.AccentFillColorDefault, bitmap.GetPixel((int)on.X, (int)on.Y));
            AssertNear(QColor.Blend(QColor.Black, t.ControlFillColorDefault.WithAlpha(1), t.ControlFillColorDefault.A), bitmap.GetPixel((int)off.X, (int)off.Y));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void Narrator_reads_tiles_as_buttons_with_their_state_and_sliders_by_name()
    {
        using var shell = new DefaultShellHarness(Content, services: Laptop());
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();

        var wifi = tree.Find(Button(shell, "wifi"))!;
        Assert.Equal(AccessibleAttached.RoleValue.Button, wifi.Role);
        Assert.Equal("Home", wifi.Name);
        Assert.Equal("Opens Wi-Fi settings", wifi.Description);
        Assert.Equal(ToggleState.On, wifi.Toggle);
        Assert.Equal(ToggleState.Off, tree.Find(Button(shell, "airplaneMode"))!.Toggle);
        // Winshell cannot read night light, so the tile does not claim a state.
        Assert.Null(tree.Find(Button(shell, "nightLight"))!.Toggle);
        Assert.Equal("Balanced", tree.Find(Button(shell, "powerMode"))!.Description);

        Assert.Equal("Volume", tree.Find(Slider(shell, "volume"))!.Name);
        Assert.Equal("Brightness", tree.Find(Slider(shell, "brightness"))!.Name);
        Assert.Equal("All settings", tree.Find(shell.Find("settings"))!.Name);
        Assert.Equal("Battery 85%", tree.Find(shell.Find("battery"))!.Name);
        // The names under the tiles are not read twice.
        Assert.DoesNotContain(Flatten(tree.Root), n => n.Item is Text && n.Name == "Home");
    }

    [Theory]
    [InlineData(true, 0.3, false, true, 0.85, PowerDeviceState.Discharging, new[] { "\uE701", "\uE993", "\uE859" })]
    [InlineData(false, 0.9, false, false, 0, PowerDeviceState.Unknown, new[] { "\uE839", "\uE995" })]
    [InlineData(false, 0.9, true, true, 0.42, PowerDeviceState.Charging, new[] { "\uE839", "\uE74F", "\uE85E" })]
    public void The_bar_button_shows_the_network_volume_and_battery_icons(bool wifi, double volume, bool muted, bool battery, double charge, PowerDeviceState state, string[] glyphs)
    {
        var services = wifi ? Laptop() : Desktop();
        services.Audio.DefaultPlaybackDevice ??= new FakeAudioNode("Speakers");
        services.Audio.DefaultPlaybackDevice.Audio.Volume = volume;
        services.Audio.DefaultPlaybackDevice.Audio.Muted = muted;
        services.Power.DisplayDevice.IsPresent = battery;
        services.Power.DisplayDevice.Percentage = charge;
        services.Power.DisplayDevice.State = state;
        using var shell = new DefaultShellHarness("""
            import QtQuick
            QuickSettingsButton {}
            """, services: services);
        var shown = DefaultShellHarness.FindAll(shell.Root).OfType<Text>().Where(t => t.Visible).Select(t => (string)t.ReadProperty("text")!).ToArray();
        Assert.Equal(glyphs, shown);
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        Assert.Equal("Quick settings", tree.Find(shell.Root)!.Name);
    }

    [Fact]
    public void Each_fake_service_has_the_QML_names_of_the_real_one()
    {
        static Dictionary<string, PropertyInfo> Qml(Type type) => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<QmlAttribute>() is not null)
            .ToDictionary(p => p.Name);

        static HashSet<string> Methods(Type type) => type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<QmlAttribute>() is not null)
            .Select(m => m.Name)
            .ToHashSet();

        foreach (var (fake, real) in FakeServices.Pairs)
        {
            Assert.Subset(Methods(real), Methods(fake));
            var realProperties = Qml(real);
            foreach (var property in Qml(fake).Values)
            {
                Assert.True(realProperties.ContainsKey(property.Name), $"{real.Name} has no QML property {property.Name}.");
                // The shell may only write what the real service lets it write.
                if (property.SetMethod is { IsPublic: true })
                    Assert.True(realProperties[property.Name].SetMethod is { IsPublic: true }, $"{real.Name}.{property.Name} is read-only.");
            }
        }
    }

    private static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static void AssertNear(QColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.R8 - actual.Red), 0, 2);
        Assert.InRange(Math.Abs(expected.G8 - actual.Green), 0, 2);
        Assert.InRange(Math.Abs(expected.B8 - actual.Blue), 0, 2);
    }
}
