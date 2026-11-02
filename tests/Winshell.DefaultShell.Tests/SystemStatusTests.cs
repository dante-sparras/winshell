using System.ComponentModel;
using Winshell.Audio;
using Winshell.DefaultShell.Taskbar;
using Winshell.Network;
using Winshell.Power;

namespace Winshell.DefaultShell.Tests;

public sealed class SystemStatusTests
{
    private static readonly Guid WifiId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondWifiId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WiredId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(1e-12, 1)]
    [InlineData(0.25, 1)]
    [InlineData(0.2500001, 2)]
    [InlineData(0.5, 2)]
    [InlineData(0.5000001, 3)]
    [InlineData(0.75, 3)]
    [InlineData(0.7500001, 4)]
    [InlineData(1, 4)]
    [InlineData(1.2, 4)]
    [InlineData(0, 4)]
    [InlineData(-0.2, 4)]
    public void Wifi_bars_follow_the_quarters_of_the_signal(double signal, int bars) =>
        Assert.Equal(bars, StatusGlyphs.WifiBars(signal));

    [Fact]
    public void No_signal_reading_draws_four_bars() => Assert.Equal(4, StatusGlyphs.WifiBars(null));

    [Theory]
    [InlineData(NetworkConnectivity.Unknown, false)]
    [InlineData(NetworkConnectivity.None, false)]
    [InlineData(NetworkConnectivity.CaptivePortal, true)]
    [InlineData(NetworkConnectivity.LocalAccess, true)]
    [InlineData(NetworkConnectivity.InternetAccess, true)]
    public void Online_is_internet_a_local_network_or_a_captive_portal(NetworkConnectivity connectivity, bool online) =>
        Assert.Equal(online, StatusGlyphs.Online(connectivity));

    [Theory]
    [InlineData(true, 1, false, "\uE872")]
    [InlineData(true, 2, false, "\uE873")]
    [InlineData(true, 3, true, "\uE874")]
    [InlineData(true, 4, true, "\uE701")]
    [InlineData(false, 1, true, "\uE839")]
    [InlineData(false, 4, false, "\uF384")]
    public void The_network_glyph_is_wifi_when_that_adapter_is_connected_and_wired_or_offline_otherwise(bool wifiConnected, int bars, bool online, string glyph) =>
        Assert.Equal(glyph, StatusGlyphs.NetworkGlyph(wifiConnected, bars, online));

    [Theory]
    [InlineData(true, 1, "\uE701")]
    [InlineData(true, 2, "\uE701")]
    [InlineData(true, 3, "\uE701")]
    [InlineData(true, 4, "")]
    [InlineData(false, 1, "")]
    [InlineData(false, 4, "")]
    public void The_track_is_the_bars_wifi_does_not_have(bool wifiConnected, int bars, string glyph) =>
        Assert.Equal(glyph, StatusGlyphs.NetworkTrackGlyph(wifiConnected, bars));

    [Theory]
    [InlineData(true, 0, "\uE74F")]
    [InlineData(true, 0.5, "\uE74F")]
    [InlineData(true, 1, "\uE74F")]
    [InlineData(false, 0, "\uE992")]
    [InlineData(false, 1e-6, "\uE993")]
    public void Mute_wins_and_silence_is_its_own_glyph(bool muted, double volume, string glyph) =>
        Assert.Equal(glyph, StatusGlyphs.VolumeGlyph(muted, volume));

    [Fact]
    public void Volume_steps_at_zero_one_third_and_two_thirds()
    {
        Assert.Equal("\uE993", StatusGlyphs.VolumeGlyph(false, Math.BitDecrement(1d / 3)));
        Assert.Equal("\uE994", StatusGlyphs.VolumeGlyph(false, 1d / 3));
        Assert.Equal("\uE994", StatusGlyphs.VolumeGlyph(false, Math.BitDecrement(2d / 3)));
        Assert.Equal("\uE995", StatusGlyphs.VolumeGlyph(false, 2d / 3));
        Assert.Equal("\uE995", StatusGlyphs.VolumeGlyph(false, 1));
    }

    [Theory]
    [MemberData(nameof(BatteryTenths))]
    public void Each_tenth_of_the_charge_has_its_glyph_and_charging_wins_over_energy_saver(double percentage, bool charging, bool energySaver, string glyph) =>
        Assert.Equal(glyph, StatusGlyphs.BatteryGlyph(percentage, charging, energySaver));

    public static IEnumerable<object[]> BatteryTenths()
    {
        string[] normal = ["\uE850", "\uE851", "\uE852", "\uE853", "\uE854", "\uE855", "\uE856", "\uE857", "\uE858", "\uE859", "\uE83F"];
        string[] charging = ["\uE85A", "\uE85B", "\uE85C", "\uE85D", "\uE85E", "\uE85F", "\uE860", "\uE861", "\uE862", "\uE83E", "\uEA93"];
        string[] saver = ["\uE863", "\uE864", "\uE865", "\uE866", "\uE867", "\uE868", "\uE869", "\uE86A", "\uE86B", "\uEA94", "\uEA95"];
        for (var tenth = 0; tenth <= 10; tenth++)
        {
            var percentage = tenth / 10d;
            yield return [percentage, false, false, normal[tenth]];
            yield return [percentage, true, false, charging[tenth]];
            yield return [percentage, true, true, charging[tenth]];
            yield return [percentage, false, true, saver[tenth]];
        }
    }

    [Theory]
    [InlineData(0.04, "\uE850")]
    [InlineData(0.05, "\uE851")]
    [InlineData(0.14, "\uE851")]
    [InlineData(0.15, "\uE852")]
    [InlineData(0.25, "\uE853")]
    [InlineData(0.95, "\uE83F")]
    [InlineData(1, "\uE83F")]
    [InlineData(1.05, "\uE83F")]
    [InlineData(-0.05, "\uE850")]
    [InlineData(-1, "\uE850")]
    public void The_charge_rounds_half_away_from_zero_and_stays_inside_the_ten_glyphs(double percentage, string glyph) =>
        Assert.Equal(glyph, StatusGlyphs.BatteryGlyph(percentage, false, false));

    [Fact]
    public void The_first_wifi_adapter_and_its_connected_network_choose_the_glyph()
    {
        using var rig = new StatusRig();
        Assert.Equal("\uF384", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess));
        Assert.Equal("\uE839", rig.Status.NetworkGlyph);
        rig.Show(Snap(NetworkConnectivity.CaptivePortal));
        Assert.Equal("\uE839", rig.Status.NetworkGlyph);
        rig.Show(Snap(NetworkConnectivity.LocalAccess));
        Assert.Equal("\uE839", rig.Status.NetworkGlyph);
        rig.Show(Snap(NetworkConnectivity.None));
        Assert.Equal("\uF384", rig.Status.NetworkGlyph);
        rig.Show(Snap(NetworkConnectivity.Unknown));
        Assert.Equal("\uF384", rig.Status.NetworkGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: false, Net("Home", connected: true, 0.1))));
        Assert.Equal("\uE839", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);

        rig.Show(Snap(NetworkConnectivity.None, Wifi(WifiId, connected: false, Net("Home", connected: true, 0.1))));
        Assert.Equal("\uF384", rig.Status.NetworkGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: true, 0))));
        Assert.Equal("\uE701", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: false, 0.9))));
        Assert.Equal("\uE701", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true)));
        Assert.Equal("\uE701", rig.Status.NetworkGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: true, 0.25))));
        Assert.Equal("\uE872", rig.Status.NetworkGlyph);
        Assert.Equal("\uE701", rig.Status.NetworkTrackGlyph);

        var home = Assert.IsType<WifiNetwork>(Assert.Single(Assert.IsType<WifiDevice>(Assert.Single(rig.Networking.Devices)).Networks));
        var changes = Watch(rig.Status);
        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: true, 0.2500001))));
        Assert.Same(home, Assert.IsType<WifiNetwork>(Assert.Single(rig.Networking.Devices[0].Networks)));
        Assert.Equal("\uE873", rig.Status.NetworkGlyph);
        Assert.Equal("\uE701", rig.Status.NetworkTrackGlyph);
        Assert.Contains(nameof(SystemStatus.NetworkGlyph), changes);

        changes = Watch(rig.Status);
        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: true, 1), Net("Guest", connected: false, 0.1))));
        Assert.Equal("\uE701", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);
        Assert.Contains(nameof(SystemStatus.NetworkTrackGlyph), changes);

        changes = Watch(rig.Status);
        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: true, 1), Net("Guest", connected: false, 0.9))));
        Assert.Equal("\uE701", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);
        Assert.DoesNotContain(nameof(SystemStatus.NetworkGlyph), changes);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: false, 1), Net("Guest", connected: true, 0.1))));
        Assert.Equal("\uE872", rig.Status.NetworkGlyph);
        Assert.Equal("\uE701", rig.Status.NetworkTrackGlyph);

        rig.Show(Snap(NetworkConnectivity.InternetAccess, Wifi(WifiId, connected: true, Net("Home", connected: true, 0.1), Net("Guest", connected: true, 1))));
        Assert.Equal("\uE872", rig.Status.NetworkGlyph);

        rig.Show(Snap(
            NetworkConnectivity.InternetAccess,
            Wired(WiredId),
            Wifi(WifiId, connected: false, Net("A", connected: true, 0.1)),
            Wifi(SecondWifiId, connected: true, Net("B", connected: true, 1))));
        Assert.Equal("\uE839", rig.Status.NetworkGlyph);
        Assert.Equal("", rig.Status.NetworkTrackGlyph);

        rig.Show(Snap(
            NetworkConnectivity.InternetAccess,
            Wired(WiredId),
            Wifi(WifiId, connected: true, Net("A", connected: true, 0.1)),
            Wifi(SecondWifiId, connected: true, Net("B", connected: true, 1))));
        Assert.Equal("\uE872", rig.Status.NetworkGlyph);
        Assert.Equal("\uE701", rig.Status.NetworkTrackGlyph);
    }

    [Fact]
    public void The_default_playback_device_volume_and_mute_choose_the_glyph()
    {
        using var rig = new StatusRig();
        Assert.Equal("\uE74F", rig.Status.VolumeGlyph);

        var quiet = rig.Audio.Add(new FakePlayback("Quiet"));
        var loud = rig.Audio.Add(new FakePlayback("Loud"));
        rig.Audio.Use(quiet);
        Assert.Equal("\uE992", rig.Status.VolumeGlyph);

        quiet.SetAudio(0.01f, false);
        Assert.Equal("\uE993", rig.Status.VolumeGlyph);
        quiet.SetAudio(0.5f, false);
        Assert.Equal("\uE994", rig.Status.VolumeGlyph);
        quiet.SetAudio(1f, false);
        Assert.Equal("\uE995", rig.Status.VolumeGlyph);

        var changes = Watch(rig.Status);
        quiet.SetAudio(0.5f, true);
        Assert.Equal("\uE74F", rig.Status.VolumeGlyph);
        Assert.Contains(nameof(SystemStatus.VolumeGlyph), changes);

        quiet.SetAudio(0f, true);
        Assert.Equal("\uE74F", rig.Status.VolumeGlyph);

        rig.Audio.Use(loud);
        loud.SetAudio(1f, false);
        Assert.Equal("\uE995", rig.Status.VolumeGlyph);
        quiet.SetAudio(0f, false);
        Assert.Equal("\uE995", rig.Status.VolumeGlyph);

        loud.SetAudio(0f, false);
        Assert.Equal("\uE992", rig.Status.VolumeGlyph);

        rig.Audio.Use(null);
        Assert.Equal("\uE74F", rig.Status.VolumeGlyph);
    }

    [Fact]
    public void The_display_battery_and_energy_saver_choose_the_glyph()
    {
        using var rig = new StatusRig();
        Assert.False(rig.Status.HasBattery);
        Assert.Equal("\uE850", rig.Status.BatteryGlyph);

        var changes = Watch(rig.Status);
        ShowBattery(rig, 0.05, PowerDeviceState.Discharging);
        Assert.True(rig.Status.HasBattery);
        Assert.Equal("\uE851", rig.Status.BatteryGlyph);
        Assert.Contains(nameof(SystemStatus.HasBattery), changes);
        Assert.Contains(nameof(SystemStatus.BatteryGlyph), changes);

        ShowBattery(rig, 0.25, PowerDeviceState.Discharging);
        Assert.Equal("\uE853", rig.Status.BatteryGlyph);

        ShowBattery(rig, 0.5, PowerDeviceState.Charging);
        Assert.Equal("\uE85F", rig.Status.BatteryGlyph);

        rig.Modes.Push(new PowerModeSnapshot(true, PowerModeLevel.Balanced, EffectivePowerMode.EnergySaver, true));
        rig.Loop.Process();
        Assert.Equal("\uE85F", rig.Status.BatteryGlyph);

        ShowBattery(rig, 0.5, PowerDeviceState.Discharging);
        Assert.Equal("\uE868", rig.Status.BatteryGlyph);

        ShowBattery(rig, 1, PowerDeviceState.FullyCharged);
        Assert.Equal("\uEA95", rig.Status.BatteryGlyph);

        rig.Modes.Push(new PowerModeSnapshot(true, PowerModeLevel.Balanced, EffectivePowerMode.Balanced, true));
        rig.Loop.Process();
        Assert.Equal("\uE83F", rig.Status.BatteryGlyph);

        ShowBattery(rig, 0.5, PowerDeviceState.Discharging, present: false);
        Assert.False(rig.Status.HasBattery);
        Assert.Equal("\uE855", rig.Status.BatteryGlyph);
    }

    private static void ShowBattery(StatusRig rig, double percentage, PowerDeviceState state, bool present = true)
    {
        rig.Power.Push(new PowerSnapshot(true, Battery(percentage, state, present), [], false));
        rig.Loop.Process();
    }

    private static PowerDeviceInfo Battery(double percentage, PowerDeviceState state, bool present) =>
        new("BAT", "Battery", present ? PowerDeviceType.Battery : PowerDeviceType.Unknown, present, present, state, percentage, 0, 0, 0, 0, 0, 0, "");

    private static NetworkSnapshot Snap(NetworkConnectivity connectivity, params NetworkDeviceInfo[] devices) =>
        new(true, devices, connectivity, true, true);

    private static NetworkDeviceInfo Wifi(Guid id, bool connected, params NetworkInfo[] networks) =>
        new(id, DeviceType.Wifi, "Wi-Fi", "", connected ? ConnectionState.Connected : ConnectionState.Disconnected, false, 0, networks, WifiDeviceMode.Station);

    private static NetworkDeviceInfo Wired(Guid id) =>
        new(id, DeviceType.Wired, "Ethernet", "", ConnectionState.Connected, true, 1000, []);

    private static NetworkInfo Net(string name, bool connected, double signal) =>
        new(name, connected ? ConnectionState.Connected : ConnectionState.Disconnected, false, signal, WifiSecurityType.Open);

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        return names;
    }
}
