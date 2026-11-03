using System.Reflection;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.System;
using Winshell.Audio;
using Winshell.Bluetooth;
using Winshell.DefaultShell.Taskbar;
using Winshell.Media;
using Winshell.Network;
using Winshell.Power;

namespace Winshell.DefaultShell.Tests;

/// <summary>Quick settings against the real services and fake backends. Nothing here opens a window.</summary>
public sealed class QuickSettingsTests
{
    private static readonly string[] DefaultTiles = ["wifi", "bluetooth", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"];

    private const string LayoutComment =
        "The quick settings tiles, in order: wifi, bluetooth, airplaneMode, energySaver, powerMode, nightLight,\n" +
        "accessibility. A tile left out is not shown until quick settings or Winshell Settings adds it back.";

    [Fact]
    public async Task Tiles_show_what_the_services_report()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        Assert.Empty(rig.Messages);
        Assert.Equal(DefaultTiles, Keys(rig.Quick));

        var wifi = Tile(rig.Quick, "wifi");
        var bluetooth = Tile(rig.Quick, "bluetooth");
        Assert.Equal("Home", wifi.Label);
        Assert.True(wifi.On);
        Assert.Equal("\uE701", wifi.Glyph);
        Assert.Equal("Opens Wi-Fi settings", wifi.Description);
        Assert.True(wifi.Toggle);
        Assert.Equal("Headphones", bluetooth.Label);
        Assert.True(bluetooth.On);
        Assert.Equal("\uE702", bluetooth.Glyph);
        Assert.False(Tile(rig.Quick, "airplaneMode").On);
        Assert.Equal("\uE709", Tile(rig.Quick, "airplaneMode").Glyph);
        Assert.False(Tile(rig.Quick, "energySaver").On);
        Assert.Equal("\uE863", Tile(rig.Quick, "energySaver").Glyph);
        Assert.Equal("Balanced", Tile(rig.Quick, "powerMode").Description);
        Assert.Equal("\uEC4A", Tile(rig.Quick, "powerMode").Glyph);
        Assert.False(Tile(rig.Quick, "powerMode").Toggle);
        Assert.Equal("\uF08C", Tile(rig.Quick, "nightLight").Glyph);
        Assert.False(Tile(rig.Quick, "nightLight").Toggle);
        Assert.Equal("\uE776", Tile(rig.Quick, "accessibility").Glyph);
        Assert.False(Tile(rig.Quick, "accessibility").Toggle);

        rig.Network.Publish(QuickSettingsRig.LaptopNetwork(enabled: false, home: false, airplane: true));
        rig.BluetoothBackend.Snapshot = QuickSettingsRig.LaptopBluetooth(on: false, headphones: false);
        await rig.Bluetooth.RefreshAsync();
        // The mode setter does not store a value. The snapshot does, once the loop applies it.
        rig.Modes.Push(new PowerModeSnapshot(true, PowerModeLevel.BestPerformance, EffectivePowerMode.EnergySaver, true));
        rig.Loop.Process();

        Assert.Equal("Wi-Fi", Tile(rig.Quick, "wifi").Label);
        Assert.False(Tile(rig.Quick, "wifi").On);
        Assert.Equal("Bluetooth", Tile(rig.Quick, "bluetooth").Label);
        Assert.False(Tile(rig.Quick, "bluetooth").On);
        Assert.True(Tile(rig.Quick, "airplaneMode").On);
        Assert.True(Tile(rig.Quick, "energySaver").On);
        Assert.Equal("Best performance", Tile(rig.Quick, "powerMode").Description);
    }

    [Fact]
    public async Task Tiles_a_PC_has_no_hardware_for_are_left_out()
    {
        using var rig = QuickSettingsRig.Create(laptop: false);
        await rig.Load();
        Assert.Equal(["airplaneMode", "energySaver", "nightLight", "accessibility"], Keys(rig.Quick));
        Assert.False(rig.Status.HasBattery);
        Assert.False(rig.Quick.Brightness.HasPanel);

        rig.BluetoothBackend.Snapshot = QuickSettingsRig.LaptopBluetooth();
        await rig.Bluetooth.RefreshAsync();
        rig.Loop.Process();
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "nightLight", "accessibility"], Keys(rig.Quick));

        rig.Network.Publish(QuickSettingsRig.LaptopNetwork());
        rig.Loop.Process();
        Assert.Equal(["wifi", "bluetooth", "airplaneMode", "energySaver", "nightLight", "accessibility"], Keys(rig.Quick));
    }

    [Theory]
    [InlineData("wifi", "ms-settings:network-wifi")]
    [InlineData("bluetooth", "ms-settings:bluetooth")]
    [InlineData("airplaneMode", "ms-settings:network-airplanemode")]
    [InlineData("energySaver", "ms-settings:batterysaver")]
    [InlineData("nightLight", "ms-settings:nightlight")]
    [InlineData("accessibility", "ms-settings:easeofaccess")]
    public async Task A_tile_Winshell_cannot_change_opens_Settings_and_closes_quick_settings(string key, string uri)
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.Activate(Tile(rig.Quick, key));
        Assert.Equal([uri], rig.Urls);
        Assert.False(rig.Popup.IsOpen);
        Assert.True(rig.Networking.WifiEnabled);
        Assert.True(rig.Bluetooth.DefaultAdapter!.Enabled);
        Assert.Equal(AirplaneModeState.Off, rig.Networking.AirplaneMode);
    }

    [Fact]
    public async Task The_power_mode_page_sets_the_mode_the_user_picks()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.Activate(Tile(rig.Quick, "powerMode"));
        Assert.Empty(rig.Urls);
        Assert.True(rig.Popup.ShowingPowerMode);
        Assert.False(rig.Popup.ShowingTiles);
        Assert.True(rig.Quick.PowerMode.Choices[1].Chosen);

        rig.Quick.PowerMode.Choices[2].Choose();
        Assert.Equal(PowerModeLevel.BestPerformance, rig.PowerMode.Mode);
        Assert.True(rig.Quick.PowerMode.Choices[2].Chosen);

        rig.Modes.Push(new PowerModeSnapshot(true, PowerModeLevel.BestEfficiency, EffectivePowerMode.Balanced, true));
        rig.Loop.Process();
        Assert.True(rig.Quick.PowerMode.Choices[0].Chosen);
        Assert.False(rig.Quick.PowerMode.Choices[2].Chosen);

        rig.Popup.Back();
        Assert.True(rig.Popup.ShowingTiles);
        Assert.True(rig.Popup.IsOpen);
    }

    [Fact]
    public async Task Escape_closes_quick_settings_from_the_tiles()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        Assert.False(rig.Popup.Escape());
        Assert.True(rig.Popup.IsOpen);
        rig.Popup.Hide();
        Assert.False(rig.Popup.IsOpen);
    }

    [Fact]
    public async Task Escape_leaves_a_page_then_closes_quick_settings()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.Activate(Tile(rig.Quick, "powerMode"));
        Assert.True(rig.Popup.Escape());
        Assert.True(rig.Popup.ShowingTiles);
        Assert.True(rig.Popup.IsOpen);
        Assert.False(rig.Popup.Escape());
        rig.Popup.Hide();
        Assert.False(rig.Popup.IsOpen);
    }

    [Fact]
    public async Task Opening_quick_settings_returns_to_the_tiles()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.ShowOutput();
        rig.Popup.Show();
        Assert.True(rig.Popup.ShowingTiles);
        Assert.True(rig.Popup.IsOpen);
        var running = rig.Bluetooth.MonitorTask;
        rig.Popup.ShowVolume();
        rig.Popup.Show();
        Assert.True(rig.Popup.ShowingTiles);
        Assert.Same(running, rig.Bluetooth.MonitorTask);
    }

    [Fact]
    public async Task The_volume_slider_follows_and_sets_the_default_playback_device()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        Assert.Equal(0.3, rig.Quick.Sound.Volume, 3);
        Assert.Equal("Mute", rig.Quick.Sound.MuteLabel);
        Assert.Equal(StatusGlyphs.VolumeGlyph(false, rig.Speakers.Volume), rig.Status.VolumeGlyph);

        rig.Quick.Sound.SetVolume(0.8);
        Assert.Equal(0.8, rig.Speakers.Volume, 3);
        Assert.Equal(0.8, rig.Quick.Sound.Volume, 3);

        rig.Speakers.Volume = 0.5;
        Assert.Equal(0.5, rig.Quick.Sound.Volume, 3);

        rig.Quick.Sound.ToggleMute();
        Assert.True(rig.Speakers.Muted);
        Assert.Equal(0.5, rig.Speakers.Volume, 3);
        Assert.Equal("Unmute", rig.Quick.Sound.MuteLabel);
        Assert.Equal("\uE74F", rig.Status.VolumeGlyph);

        rig.Graph.Use(null);
        Assert.False(rig.Quick.Sound.HasOutput);
        Assert.Equal(0, rig.Quick.Sound.Volume);
        Assert.Equal("Unmute", rig.Quick.Sound.MuteLabel);
        rig.Quick.Sound.SetVolume(0.4);
        rig.Quick.Sound.ToggleMute();
        Assert.Equal(0, rig.Quick.Sound.Volume);
    }

    [Fact]
    public async Task The_brightness_slider_sets_the_built_in_panel_only()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        var panel = Assert.Single(rig.Brightness.Monitors, monitor => monitor.Connection == BrightnessConnection.Internal);
        var external = Assert.Single(rig.Brightness.Monitors, monitor => monitor.Connection == BrightnessConnection.External);
        Assert.Equal(0.4, rig.Quick.Brightness.Level, 3);
        Assert.True(rig.Quick.Brightness.HasPanel);

        rig.Quick.Brightness.SetLevel(0.8);
        Assert.Equal(80, panel.Brightness);
        Assert.Equal(70, external.Brightness);
        await Wait.UntilAsync(() => !rig.Writer.IsWriting(panel.DeviceName), "the brightness write");
    }

    [Fact]
    public async Task The_footer_shows_the_battery_and_opens_Settings()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        Assert.True(rig.Status.HasBattery);
        Assert.Equal("85%", rig.Quick.Battery.Percent);
        Assert.Equal("Battery 85%", rig.Quick.Battery.Text);

        rig.Power.Push(QuickSettingsRig.Battery(0.85, PowerDeviceState.Charging));
        rig.Loop.Process();
        Assert.Equal("Battery 85%, charging", rig.Quick.Battery.Text);
        Assert.Equal("85%", rig.Quick.Battery.Percent);

        rig.Popup.Show();
        rig.Popup.OpenAllSettings();
        rig.Popup.OpenBattery();
        Assert.Equal(["ms-settings:", "ms-settings:batterysaver"], rig.Urls);
        Assert.False(rig.Popup.IsOpen);
    }

    [Fact]
    public async Task The_output_page_makes_the_device_the_user_picks_the_default()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        var microphone = rig.Add("Microphone", AudioNodeType.RecordingDevice);
        var music = rig.Add("Music app", AudioNodeType.PlaybackSession);
        var headset = rig.Add("Headset", AudioNodeType.PlaybackDevice);

        rig.Popup.Show();
        rig.Popup.ShowOutput();
        Assert.False(rig.Popup.ShowingTiles);
        Assert.Equal(["Speakers", "Headset"], rig.Quick.Sound.Outputs.Select(choice => choice.Text));
        Assert.DoesNotContain(rig.Quick.Sound.Outputs, choice => ReferenceEquals(choice.Node, microphone) || ReferenceEquals(choice.Node, music));
        Assert.True(rig.Quick.Sound.Outputs[0].Chosen);

        rig.Quick.Sound.Outputs[1].Choose();
        Assert.Same(headset, rig.Audio.DefaultPlaybackDevice);
        Assert.False(rig.Quick.Sound.Outputs[0].Chosen);
        Assert.True(rig.Quick.Sound.Outputs[1].Chosen);

        rig.Graph.Use(rig.Speakers.Node);
        Assert.True(rig.Quick.Sound.Outputs[0].Chosen);
        Assert.False(rig.Quick.Sound.Outputs[1].Chosen);

        rig.Popup.OpenVolumeSettings();
        Assert.Equal(["ms-settings:apps-volume"], rig.Urls);
        Assert.False(rig.Popup.IsOpen);
    }

    [Fact]
    public async Task The_output_page_lists_every_playback_device()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        for (var i = 0; i < 20; i++)
            rig.Add($"Monitor {i}", AudioNodeType.PlaybackDevice);
        rig.Popup.ShowOutput();
        Assert.Equal(21, rig.Quick.Sound.Outputs.Count);
        Assert.Equal("Speakers", rig.Quick.Sound.Outputs[0].Text);
        Assert.Equal("Monitor 19", rig.Quick.Sound.Outputs[20].Text);
    }

    [Fact]
    public async Task The_volume_page_sets_each_audio_session_and_stays_hidden_until_it_is_opened()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        var microphone = rig.Add("Microphone", AudioNodeType.RecordingDevice);
        var music = rig.Add("Music", AudioNodeType.PlaybackSession, 0.4f);
        var call = rig.Add("Call", AudioNodeType.RecordingSession, 0.2f);

        Assert.False(rig.Popup.ShowingVolume);
        rig.Popup.Show();
        rig.Popup.ShowOutput();
        Assert.Equal(["Speakers"], rig.Quick.Sound.Outputs.Select(choice => choice.Text));
        Assert.False(rig.Popup.ShowingVolume);

        rig.Popup.ShowVolume();
        Assert.False(rig.Popup.ShowingTiles);
        Assert.False(rig.Popup.ShowingOutput);
        Assert.True(rig.Popup.ShowingVolume);
        Assert.Equal(["Music", "Call"], rig.Quick.Sound.Sessions.Select(session => session.Label));
        Assert.DoesNotContain(rig.Quick.Sound.Sessions, session => ReferenceEquals(session.Node, microphone));
        Assert.Equal(0.4, rig.Quick.Sound.Sessions[0].Volume, 3);
        Assert.Equal(0.2, rig.Quick.Sound.Sessions[1].Volume, 3);

        rig.Quick.Sound.Sessions[0].SetVolume(0.8);
        Assert.Equal(0.8, music.Volume, 3);
        Assert.Equal(0.2, call.Volume, 3);
        Assert.Equal(0.3, rig.Speakers.Volume, 3);

        rig.Quick.Sound.Sessions[0].ToggleMute();
        Assert.True(music.Muted);
        Assert.False(call.Muted);
        Assert.Equal(0.8, music.Volume, 3);
        Assert.Equal("Unmute", rig.Quick.Sound.Sessions[0].MuteLabel);
        Assert.Equal("Mute", rig.Quick.Sound.Sessions[1].MuteLabel);

        music.Volume = 0.5;
        Assert.Equal(0.5, rig.Quick.Sound.Sessions[0].Volume, 3);

        rig.Popup.Back();
        Assert.True(rig.Popup.ShowingTiles);
        Assert.False(rig.Popup.ShowingVolume);

        rig.Popup.ShowOutput();
        rig.Popup.ShowVolume();
        rig.Popup.OpenVolumeSettings();
        Assert.Equal(["ms-settings:apps-volume"], rig.Urls);
        Assert.False(rig.Popup.IsOpen);
    }

    [Fact]
    public async Task A_session_glyph_uses_the_status_volume_steps()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        var music = rig.Add("Music", AudioNodeType.PlaybackSession, 0f);
        var session = Assert.Single(rig.Quick.Sound.Sessions);
        Assert.Equal("\uE992", session.Glyph);

        music.Volume = 0.2;
        Assert.Equal("\uE993", session.Glyph);
        music.Volume = 0.5;
        Assert.Equal("\uE994", session.Glyph);
        music.Volume = 1;
        Assert.Equal("\uE995", session.Glyph);
        music.Muted = true;
        Assert.Equal("\uE74F", session.Glyph);
        Assert.Equal(StatusGlyphs.VolumeGlyph(music.Muted, music.Volume), session.Glyph);
    }

    [Fact]
    public async Task The_media_card_shows_the_playing_session_and_its_controls()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        var paused = rig.Sessions.Add("Spotify", "Paused song", "Someone", playing: false);
        var playing = rig.Sessions.Add("Firefox", "A video", "A channel", playing: true, next: true);

        Assert.True(rig.Quick.Media.HasSession);
        Assert.Equal("Firefox", rig.Quick.Media.App);
        Assert.Equal("A video", rig.Quick.Media.Title);
        Assert.Equal("A channel", rig.Quick.Media.Artist);
        Assert.False(rig.Quick.Media.HasArt);
        Assert.False(rig.Quick.Media.CanPrevious);
        Assert.True(rig.Quick.Media.CanNext);
        Assert.True(rig.Quick.Media.CanToggle);
        Assert.True(rig.Quick.Media.Playing);
        Assert.Equal("Pause", rig.Quick.Media.PlayLabel);
        Assert.Equal("\uE769", rig.Quick.Media.PlayGlyph);

        rig.Quick.Media.Next();
        rig.Quick.Media.Toggle();
        Assert.Equal(["next", "pause"], playing.Sent);
        Assert.Empty(paused.Sent!);

        // The service records the command. The card follows the session once it reports that it stopped.
        playing.ApplyPlayback(new SmtcPlayback(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) { CanPlay = true, CanGoNext = true });
        Assert.Equal("Paused song", rig.Quick.Media.Title);
        Assert.Equal("Someone", rig.Quick.Media.Artist);
        Assert.False(rig.Quick.Media.Playing);
        Assert.Equal("Play", rig.Quick.Media.PlayLabel);
        Assert.Equal("\uE768", rig.Quick.Media.PlayGlyph);

        rig.Sessions.Remove(playing);
        rig.Sessions.Remove(paused);
        Assert.False(rig.Quick.Media.HasSession);
        Assert.Equal("", rig.Quick.Media.Title);
    }

    [Fact]
    public async Task Edit_mode_unpins_and_adds_tiles_and_saves_them()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.Edit();
        Assert.True(rig.Popup.Editing);
        Assert.False(rig.Popup.Browsing);
        Assert.Empty(rig.Quick.Pins.Unpinned);
        Assert.Equal("Wi-Fi", Tile(rig.Quick, "wifi").Title);
        Assert.Equal("Home", Tile(rig.Quick, "wifi").Label);

        rig.Popup.Activate(Tile(rig.Quick, "wifi"));
        Assert.Empty(rig.Urls);
        Assert.True(rig.Popup.IsOpen);

        rig.Popup.Unpin("bluetooth", fromKeyboard: false);
        Assert.Equal(["wifi", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"], Keys(rig.Quick));
        Assert.Equal(Keys(rig.Quick), Saved(rig));
        Assert.Equal(["bluetooth"], rig.Quick.Pins.Unpinned.Select(tile => tile.Key));
        Assert.Equal("Bluetooth", Assert.Single(rig.Quick.Pins.Unpinned).Title);

        rig.Popup.ShowAdd();
        Assert.True(rig.Popup.ShowingAdd);
        Assert.False(rig.Popup.ShowingTiles);
        rig.Popup.Add("bluetooth");
        Assert.True(rig.Popup.ShowingTiles);
        Assert.False(rig.Popup.ShowingAdd);
        Assert.Equal(["wifi", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility", "bluetooth"], Keys(rig.Quick));
        Assert.Equal(Keys(rig.Quick), Saved(rig));
        Assert.Empty(rig.Quick.Pins.Unpinned);

        rig.Popup.Done();
        Assert.False(rig.Popup.Editing);
        Assert.True(rig.Popup.Browsing);
        Assert.True(rig.Popup.IsOpen);

        rig.Popup.Edit();
        rig.Popup.Hide();
        Assert.False(rig.Popup.Editing);
        Assert.False(rig.Popup.IsOpen);
        rig.Popup.Show();
        Assert.False(rig.Popup.Editing);

        File.WriteAllText(rig.LayoutPath, """pinned = ["nightLight"]""");
        Wait.Until(() => Keys(rig.Quick).SequenceEqual(["nightLight"]), "the file written after an edit", rig.Loop.Process);
        Assert.Empty(rig.Messages);
    }

    [Fact]
    public async Task Dragging_a_tile_moves_it_to_where_it_is_let_go()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.Edit();

        rig.Popup.BeginDrag(Tile(rig.Quick, "wifi"));
        // The airplane tile is the first tile of the second row, whose cell starts at y = 82.
        rig.Popup.DragTo(80, 82 + 24, 70);
        Assert.Equal(2, rig.Popup.DropIndex);
        Assert.Equal(0.5, rig.Popup.DragOpacity(0));
        Assert.Equal(1, rig.Popup.DropSide(2));
        Assert.Equal(0, rig.Popup.DropSide(0));
        rig.Popup.EndDrag();
        Assert.Equal(["bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "nightLight", "accessibility"], Keys(rig.Quick));
        Assert.Equal(Keys(rig.Quick), Saved(rig));
        Assert.All(Enumerable.Range(0, rig.Quick.Pins.Shown.Count), index => Assert.Equal(0, rig.Popup.DropSide(index)));

        rig.Popup.BeginDrag(Tile(rig.Quick, "nightLight"));
        rig.Popup.DragTo(10, 24, 70);
        rig.Popup.EndDrag();
        Assert.Equal(["nightLight", "bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "accessibility"], Keys(rig.Quick));

        rig.Popup.BeginDrag(Tile(rig.Quick, "energySaver"));
        rig.Popup.EndDrag();
        Assert.Equal(["nightLight", "bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "accessibility"], Keys(rig.Quick));
        rig.Popup.Activate(Tile(rig.Quick, "energySaver"));
        Assert.Empty(rig.Urls);
    }

    [Fact]
    public async Task Alt_Shift_and_the_arrow_keys_move_the_focused_tile_and_Delete_unpins_it()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        rig.Popup.Show();
        rig.Popup.Edit();

        rig.Popup.MoveBy("wifi", QuickSettingsTileView.KeyOffset(true, true, VirtualKey.Down));
        Assert.Equal(["bluetooth", "airplaneMode", "wifi", "energySaver", "powerMode", "nightLight", "accessibility"], Keys(rig.Quick));
        Assert.Equal("wifi", rig.Popup.FocusKey);

        rig.Popup.MoveBy("wifi", QuickSettingsTileView.KeyOffset(true, true, VirtualKey.Right));
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "wifi", "powerMode", "nightLight", "accessibility"], Keys(rig.Quick));
        Assert.Equal("wifi", rig.Popup.FocusKey);

        Assert.Equal(1, QuickSettingsTileView.KeyOffset(alt: true, shift: true, VirtualKey.Right));
        Assert.Equal(-1, QuickSettingsTileView.KeyOffset(true, true, VirtualKey.Left));
        Assert.Equal(-2, QuickSettingsTileView.KeyOffset(true, true, VirtualKey.Up));
        Assert.Equal(2, QuickSettingsTileView.KeyOffset(true, true, VirtualKey.Down));
        Assert.Equal(0, QuickSettingsTileView.KeyOffset(alt: false, shift: false, VirtualKey.Right));
        Assert.Equal(0, QuickSettingsTileView.KeyOffset(alt: true, shift: false, VirtualKey.Right));
        Assert.Equal(0, QuickSettingsTileView.KeyOffset(true, true, VirtualKey.Delete));

        // Without both modifiers an arrow key is not a move, so the order stays.
        Assert.Equal(0, QuickSettingsTileView.KeyOffset(false, false, VirtualKey.Right));
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "wifi", "powerMode", "nightLight", "accessibility"], Keys(rig.Quick));

        rig.Popup.Unpin("nightLight", fromKeyboard: true);
        Assert.Equal(["bluetooth", "airplaneMode", "energySaver", "wifi", "powerMode", "accessibility"], Saved(rig));
        Assert.Equal("accessibility", rig.Popup.FocusKey);

        Assert.True(rig.Popup.Escape());
        Assert.False(rig.Popup.Editing);
        Assert.True(rig.Popup.IsOpen);
        Assert.Empty(rig.Urls);
    }

    [Fact]
    public async Task Unpinning_keeps_tiles_this_PC_has_no_hardware_for()
    {
        using var rig = QuickSettingsRig.Create(laptop: false);
        await rig.Load();
        Assert.Equal(["airplaneMode", "energySaver", "nightLight", "accessibility"], Keys(rig.Quick));
        rig.Popup.Edit();
        rig.Popup.Unpin("energySaver", fromKeyboard: false);
        Assert.Equal(["airplaneMode", "nightLight", "accessibility"], Keys(rig.Quick));
        Assert.Equal(["wifi", "bluetooth", "airplaneMode", "powerMode", "nightLight", "accessibility"], Saved(rig));

        rig.Popup.MoveBy("nightLight", 1);
        Assert.Equal(["airplaneMode", "accessibility", "nightLight"], Keys(rig.Quick));
        Assert.Equal(["wifi", "bluetooth", "airplaneMode", "powerMode", "accessibility", "nightLight"], Saved(rig));
    }

    [Fact]
    public async Task A_saved_layout_is_restored_and_keeps_tiles_this_PC_has_no_hardware_for()
    {
        using (var desktop = QuickSettingsRig.Create(laptop: false, file: """pinned = ["wifi", "bluetooth", "airplaneMode", "powerMode", "nightLight", "accessibility"]"""))
        {
            await desktop.Load();
            Assert.Equal(["airplaneMode", "nightLight", "accessibility"], Keys(desktop.Quick));
            Assert.Equal(["wifi", "bluetooth", "airplaneMode", "powerMode", "nightLight", "accessibility"], Strings(desktop.Layout.Pinned));
        }

        using var laptop = QuickSettingsRig.Create(laptop: true, file: """pinned = ["nightLight", "gone", "wifi", 3]""");
        await laptop.Load();
        Assert.Equal(["nightLight", "wifi"], Keys(laptop.Quick));
        Assert.Equal("gone", laptop.Layout.Pinned![1]);
        Assert.Equal(3L, laptop.Layout.Pinned[3]);
        Assert.Empty(laptop.Messages);
    }

    [Fact]
    public async Task Quick_settings_follows_its_layout_file_as_Winshell_Settings_changes_it()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        Assert.Equal(DefaultTiles, Keys(rig.Quick));

        File.WriteAllText(rig.LayoutPath, """pinned = ["wifi", "bluetooth"]""");
        Wait.Until(() => Keys(rig.Quick).SequenceEqual(["wifi", "bluetooth"]), "the first save", rig.Loop.Process);
        File.WriteAllText(rig.LayoutPath, """pinned = ["bluetooth", "nightLight"]""");
        Wait.Until(() => Keys(rig.Quick).SequenceEqual(["bluetooth", "nightLight"]), "the next save", rig.Loop.Process);
        Assert.True(rig.Layout.Loaded);
        Assert.Empty(rig.Messages);
    }

    [Fact]
    public async Task A_layout_file_that_is_not_TOML_leaves_the_default_tiles_and_warns()
    {
        using var rig = QuickSettingsRig.Create(laptop: true, file: "not toml");
        await rig.Load();
        Assert.Equal(DefaultTiles, Keys(rig.Quick));
        Assert.False(rig.Layout.Loaded);
        Assert.Contains(rig.Messages, message => message.Contains("quick-settings.toml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_later_bad_layout_file_leaves_the_tiles_and_warns()
    {
        using var rig = QuickSettingsRig.Create(laptop: true, file: """pinned = ["bluetooth", "nightLight"]""");
        await rig.Load();
        Assert.Equal(["bluetooth", "nightLight"], Keys(rig.Quick));
        var before = rig.Messages.Count;
        File.WriteAllText(rig.LayoutPath, "not toml");
        Wait.Until(() => rig.Messages.Count > before, "the bad file", rig.Loop.Process);
        Assert.Equal(["bluetooth", "nightLight"], Keys(rig.Quick));
        Assert.Contains(rig.Messages, message => message.Contains("quick-settings.toml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_empty_pinned_list_shows_no_tiles()
    {
        using var rig = QuickSettingsRig.Create(laptop: true, file: "pinned = []");
        await rig.Load();
        Assert.Empty(rig.Quick.Pins.Shown);
        Assert.Empty(rig.Messages);
    }

    [Fact]
    public async Task A_missing_layout_file_keeps_the_default_tiles()
    {
        using var rig = QuickSettingsRig.Create(laptop: true);
        await rig.Load();
        Assert.False(rig.Layout.Loaded);
        Assert.Empty(rig.Messages);
        Assert.Equal(DefaultTiles, Keys(rig.Quick));
    }

    [Fact]
    public void The_layout_comment_is_the_one_quick_settings_writes()
    {
        var comment = typeof(QuickSettingsLayout).GetProperty(nameof(QuickSettingsLayout.Pinned))!
            .GetCustomAttribute<TomlCommentAttribute>()!
            .Text;
        Assert.Equal(LayoutComment, comment);
    }

    [Fact]
    public async Task Bluetooth_is_read_only_while_a_flyout_is_open()
    {
        using var rig = QuickSettingsRig.Create(laptop: false);
        Assert.Null(rig.Bluetooth.MonitorTask);

        rig.Popup.Show();
        var running = rig.Bluetooth.MonitorTask;
        Assert.NotNull(running);
        Assert.False(running.IsCompleted);

        var second = rig.Track(rig.Quick.CreatePopup());
        second.Show();
        Assert.Same(running, rig.Bluetooth.MonitorTask);
        second.Hide();
        rig.Bluetooth.StartMonitoring();
        Assert.Same(running, rig.Bluetooth.MonitorTask);

        rig.Popup.ShowOutput();
        rig.Popup.Show();
        Assert.True(rig.Popup.ShowingTiles);
        Assert.Same(running, rig.Bluetooth.MonitorTask);

        rig.Popup.Hide();
        await Wait.UntilAsync(() => running.IsCompleted, "monitoring to stop");
    }

    [Fact]
    public void Quick_settings_sits_above_its_button_and_lines_up_on_the_right()
    {
        var button = new Rect(1000, 800, 40, 32);
        var placed = QuickSettingsPlacement.Above(button, 420);
        Assert.Equal(button.Right - QuickSettingsPlacement.Width, placed.X);
        Assert.Equal(button.Top - QuickSettingsPlacement.Gap - 420, placed.Y);
        Assert.Equal(QuickSettingsPlacement.Width, placed.Width);
        Assert.Equal(420, placed.Height);
    }

    [Fact]
    public void A_slider_ignores_a_change_inside_half_a_step()
    {
        Assert.True(SliderRow.IsEcho(0.5, 0.504, 0.01));
        Assert.False(SliderRow.IsEcho(0.5, 0.505, 0.01));
    }

    private static string[] Keys(QuickSettings quick) => [.. quick.Pins.Shown.Select(tile => tile.Key)];

    private static QuickSettingsTile Tile(QuickSettings quick, string key) => Assert.Single(quick.Pins.Shown, tile => tile.Key == key);

    /// <summary>The order written to the layout file, read back by a second watcher.</summary>
    private static List<string> Saved(QuickSettingsRig rig)
    {
        rig.Loop.Process();
        using var again = new QuickSettingsLayout(rig.LayoutPath, rig.Loop, rig.Log);
        return again.Pinned!.OfType<string>().ToList();
    }

    private static List<string> Strings(IReadOnlyList<object?>? pinned) =>
        pinned is null ? [] : [.. pinned.OfType<string>()];
}

/// <summary>The services quick settings follows, fed by fakes. One laptop or one desktop.</summary>
internal sealed class QuickSettingsRig : IDisposable
{
    private readonly List<QuickSettingsPopup> _popups = [];
    private readonly string _directory;

    private QuickSettingsRig(bool laptop, string? file)
    {
        _directory = Path.Combine(Path.GetTempPath(), "winshell-quick-settings-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        Loop = new ManualEventLoop();
        Messages = [];
        Log = new Log { Sink = (_, _, message) => Messages.Add(message) };
        Network = new FakeNetwork();
        Graph = new ChoosingGraph();
        Power = new FakeSource<PowerSnapshot>(laptop ? Battery(0.85, PowerDeviceState.Discharging) : PowerSnapshot.Initial);
        Modes = new FakeSource<PowerModeSnapshot>(laptop
            ? new PowerModeSnapshot(true, PowerModeLevel.Balanced, EffectivePowerMode.Balanced, true)
            : PowerModeSnapshot.Initial);
        BrightnessSource = new FakeSource<BrightnessSnapshot>(laptop ? LaptopBrightness() : DesktopBrightness());
        Writer = new BrightnessWriter(static (_, _) => 0);
        BluetoothBackend = new SnapshotBluetooth();
        if (laptop)
            BluetoothBackend.Snapshot = LaptopBluetooth();
        if (file is not null)
            File.WriteAllText(Path.Combine(_directory, "quick-settings.toml"), file);
        Network.Publish(laptop ? LaptopNetwork() : DesktopNetwork());
        var speakers = new RigNode("Speakers", AudioNodeType.PlaybackDevice);
        speakers.SetAudio(0.3f, false);
        Graph.Add(speakers);
        Graph.Use(speakers);
        Networking = new NetworkingService(Loop, Network);
        Audio = new AudioService(Loop, Log, Graph);
        PowerService = new PowerService(Loop, Power);
        PowerMode = new PowerModeService(Loop, Log, Modes, SetMode);
        Brightness = new BrightnessService(Loop, Log, BrightnessSource, Writer);
        Bluetooth = new BluetoothService(Loop, Log, BluetoothBackend);
        Status = new SystemStatus(Networking, Audio, PowerService, PowerMode);
        LayoutPath = Path.Combine(_directory, "quick-settings.toml");
        Layout = new QuickSettingsLayout(LayoutPath, Loop, Log);
        Sessions = new RigSessions();
        Media = new MediaService(Loop, Log, Sessions);
        Quick = new QuickSettings(Status, Networking, Bluetooth, Audio, PowerService, PowerMode, Brightness, Layout, Media);
        Urls = [];
        Quick.SettingsRequested += Urls.Add;
        Speakers = Audio.DefaultPlaybackDevice!;
        Popup = Track(Quick.CreatePopup());
    }

    public ManualEventLoop Loop { get; }

    public Log Log { get; }

    public List<string> Messages { get; }

    public FakeNetwork Network { get; }

    public ChoosingGraph Graph { get; }

    public FakeSource<PowerSnapshot> Power { get; }

    public FakeSource<PowerModeSnapshot> Modes { get; }

    public FakeSource<BrightnessSnapshot> BrightnessSource { get; }

    public BrightnessWriter Writer { get; }

    public SnapshotBluetooth BluetoothBackend { get; }

    public NetworkingService Networking { get; }

    public AudioService Audio { get; }

    public PowerService PowerService { get; }

    public PowerModeService PowerMode { get; }

    public BrightnessService Brightness { get; }

    public BluetoothService Bluetooth { get; }


    public SystemStatus Status { get; }

    public string LayoutPath { get; }

    public QuickSettingsLayout Layout { get; }

    public RigSessions Sessions { get; }

    public MediaService Media { get; }

    public QuickSettings Quick { get; }

    public List<string> Urls { get; }

    public AudioNode Speakers { get; }

    public QuickSettingsPopup Popup { get; }

    public static QuickSettingsRig Create(bool laptop, string? file = null) => new(laptop, file);

    public async Task Load()
    {
        await Bluetooth.RefreshAsync();
        Loop.Process();
    }

    public QuickSettingsPopup Track(QuickSettingsPopup popup)
    {
        _popups.Add(popup);
        return popup;
    }

    public AudioNode Add(string description, AudioNodeType type, float volume = 0, bool muted = false)
    {
        var node = new RigNode(description, type);
        node.SetAudio(volume, muted);
        Graph.Add(node);
        return Audio.Nodes.First(item => item.Node == node);
    }

    public static NetworkSnapshot LaptopNetwork(bool enabled = true, bool home = true, bool airplane = false) =>
        new(
            true,
            [Wifi(Guid.Parse("11111111-1111-1111-1111-111111111111"), [Net("Neighbour", false, 0.2), Net("Home", home, 0.8)])],
            NetworkConnectivity.InternetAccess,
            enabled,
            true,
            AirplaneMode: airplane ? AirplaneModeState.On : AirplaneModeState.Off,
            WifiState: WifiRadioState.On);

    public static NetworkSnapshot DesktopNetwork() =>
        new(
            true,
            [new NetworkDeviceInfo(Guid.Parse("33333333-3333-3333-3333-333333333333"), DeviceType.Wired, "Ethernet", "", ConnectionState.Connected, true, 1000, [])],
            NetworkConnectivity.InternetAccess,
            false,
            false);

    public static BluetoothSnapshot LaptopBluetooth(bool on = true, bool headphones = true) =>
        new(
            true,
            new BluetoothAdapterInfo("adapter", "Bluetooth", "", on ? BluetoothAdapterState.On : BluetoothAdapterState.Off),
            [
                new BluetoothDeviceInfo("Mouse", "Mouse", "", "Mouse", false, true, true, false, null, null, []),
                new BluetoothDeviceInfo("Headphones", "Headphones", "", "Headphones", headphones, true, true, false, null, null, []),
            ]);

    public static PowerSnapshot Battery(double percentage, PowerDeviceState state) =>
        new(true, new PowerDeviceInfo("BAT", "Battery", PowerDeviceType.Battery, true, true, state, percentage, 0, 0, 0, 0, 0, 0), [], false);

    public void Dispose()
    {
        foreach (var popup in _popups)
            popup.Hide();
        Media.Dispose();
        Bluetooth.Dispose();
        Audio.Dispose();
        Networking.Dispose();
        PowerService.Dispose();
        PowerMode.Dispose();
        Brightness.Dispose();
        Layout.Dispose();
        BluetoothBackend.Dispose();
        Directory.Delete(_directory, true);
    }

    private uint SetMode(PowerModeLevel level)
    {
        Modes.Push(Modes.Current with { Mode = level });
        Loop.Process();
        return 0;
    }

    private static BrightnessSnapshot LaptopBrightness() =>
        new(true, [
            new BrightnessInfo("desk", "Desk", BrightnessConnection.External, 0, 100, 70),
            new BrightnessInfo("panel", "Built-in", BrightnessConnection.Internal, 0, 100, 40),
        ]);

    private static BrightnessSnapshot DesktopBrightness() =>
        new(true, [new BrightnessInfo("desk", "Desk", BrightnessConnection.External, 0, 100, 70)]);

    private static NetworkDeviceInfo Wifi(Guid id, NetworkInfo[] networks) =>
        new(id, DeviceType.Wifi, "Wi-Fi", "", ConnectionState.Connected, false, 0, networks, WifiDeviceMode.Station);

    private static NetworkInfo Net(string name, bool connected, double signal) =>
        new(name, connected ? ConnectionState.Connected : ConnectionState.Disconnected, false, signal, WifiSecurityType.Open);
}

/// <summary>A playback graph whose default follows <see cref="SetDefault"/>.</summary>
internal sealed class ChoosingGraph : IAudioGraph
{
    private readonly List<AudioGraphNode> _nodes = [];

    public IReadOnlyList<AudioGraphNode> Nodes => _nodes;

    public bool Ready => true;

    public AudioGraphNode? DefaultPlayback { get; private set; }

    public AudioGraphNode? DefaultRecording => null;

    public event Action<AudioGraphNode>? NodeAdded;

    public event Action? DefaultsChanged;

#pragma warning disable CS0067 // AudioService subscribes. These tests do not remove a node or add a link.
    public event Action<AudioGraphNode>? NodeRemoved;
#pragma warning restore CS0067

    public void SetDefault(AudioGraphNode node) => Use(node);

    public void Add(AudioGraphNode node)
    {
        _nodes.Add(node);
        NodeAdded?.Invoke(node);
    }

    public void Use(AudioGraphNode? node)
    {
        DefaultPlayback = node;
        DefaultsChanged?.Invoke();
    }
}

/// <summary>One device or session, with the volume the test sets.</summary>
internal sealed class RigNode : AudioGraphNode
{
    private static int _next = 1;

    public RigNode(string description, AudioNodeType type)
        : base(_next++)
    {
        Type = type;
        Description = description;
        Name = description;
        Channels = [AudioChannel.FrontLeft];
        ApplyAudio([0f], false);
    }

    public override IAudioMeterInformation? Meter => null;

    public override void SetVolumes(IReadOnlyList<float> volumes) => ApplyAudio([.. volumes], Muted);

    public override void SetMuted(bool muted) => ApplyAudio([.. Volumes], muted);

    public void SetAudio(float volume, bool muted) => ApplyAudio([volume], muted);
}

/// <summary>Media sessions a test adds, in order. Commands land on <see cref="SmtcSession.Sent"/>.</summary>
internal sealed class RigSessions : IMediaSessions
{
    private readonly List<SmtcSession> _sessions = [];

    public IReadOnlyList<SmtcSession> Sessions => _sessions;

    public event Action<SmtcSession>? SessionAdded;

    public event Action<SmtcSession>? SessionRemoved;

    public SmtcSession Add(string app, string title, string artist, bool playing, bool next = false)
    {
        var session = SmtcSession.CreateForTests(app);
        session.ApplyTrack(title, artist, "", "", 0, null, null);
        session.ApplyPlayback(new SmtcPlayback(playing
            ? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            : GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
        {
            CanPlay = true,
            CanPause = true,
            CanGoNext = next,
        });
        _sessions.Add(session);
        SessionAdded?.Invoke(session);
        return session;
    }

    public void Remove(SmtcSession session)
    {
        _sessions.Remove(session);
        SessionRemoved?.Invoke(session);
    }
}

/// <summary>A Bluetooth read that returns the snapshot a test stored.</summary>
internal sealed class SnapshotBluetooth : IBluetoothBackend
{
    public BluetoothSnapshot Snapshot { get; set; } = BluetoothSnapshot.Initial;

    public Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation) => Task.FromResult(Snapshot);

    public void StartDiscovery() => throw new NotSupportedException();

    public void StopDiscovery() => throw new NotSupportedException();

    public Task<string?> PairAsync(string id, CancellationToken cancellation) => throw new NotSupportedException();

    public Task<string?> UnpairAsync(string id) => throw new NotSupportedException();

    public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
