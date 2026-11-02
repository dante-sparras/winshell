using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Audio;
using Winshell.Network;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The glyph Windows 11 draws for a signal, a volume or a charge. Plain values in, one Segoe Fluent Icons character out.
/// </summary>
public static class StatusGlyphs
{
    private static readonly string[] BatteryNormal = ["\uE850", "\uE851", "\uE852", "\uE853", "\uE854", "\uE855", "\uE856", "\uE857", "\uE858", "\uE859", "\uE83F"];
    private static readonly string[] BatteryCharging = ["\uE85A", "\uE85B", "\uE85C", "\uE85D", "\uE85E", "\uE85F", "\uE860", "\uE861", "\uE862", "\uE83E", "\uEA93"];
    private static readonly string[] BatterySaver = ["\uE863", "\uE864", "\uE865", "\uE866", "\uE867", "\uE868", "\uE869", "\uE86A", "\uE86B", "\uEA94", "\uEA95"];

    /// <summary>Wi-Fi signal in bars, from 1 to 4. No reading draws all four.</summary>
    public static int WifiBars(double? signal) =>
        signal is > 0 ? Math.Clamp((int)Math.Ceiling(signal.Value * 4), 1, 4) : 4;

    /// <summary>Internet, a local network or a captive portal. Anything else is offline.</summary>
    public static bool Online(NetworkConnectivity connectivity) =>
        connectivity is NetworkConnectivity.InternetAccess or NetworkConnectivity.LocalAccess or NetworkConnectivity.CaptivePortal;

    /// <summary>Wi-Fi when that adapter is connected, with its bars. Any other connection is drawn as wired.</summary>
    public static string NetworkGlyph(bool wifiConnected, int bars, bool online)
    {
        if (!wifiConnected)
            return online ? "\uE839" : "\uF384";
        return bars switch
        {
            1 => "\uE872",
            2 => "\uE873",
            3 => "\uE874",
            _ => "\uE701",
        };
    }

    /// <summary>The bars Wi-Fi does not have, drawn faintly under <see cref="NetworkGlyph"/>.</summary>
    public static string NetworkTrackGlyph(bool wifiConnected, int bars) =>
        wifiConnected && bars < 4 ? "\uE701" : "";

    /// <summary>Mute, then silence, then low, medium and high.</summary>
    public static string VolumeGlyph(bool muted, double volume)
    {
        if (muted)
            return "\uE74F";
        if (volume == 0)
            return "\uE992";
        if (volume < 1d / 3)
            return "\uE993";
        if (volume < 2d / 3)
            return "\uE994";
        return "\uE995";
    }

    /// <summary>Battery in tenths. Charging wins over energy saver. The charge is rounded half away from zero.</summary>
    public static string BatteryGlyph(double percentage, bool charging, bool energySaver)
    {
        var tenth = Math.Clamp((int)Math.Round(percentage * 10, MidpointRounding.AwayFromZero), 0, 10);
        var glyphs = charging ? BatteryCharging : energySaver ? BatterySaver : BatteryNormal;
        return glyphs[tenth];
    }
}

/// <summary>
/// The network, volume and battery the status icons show. One instance is shared by every taskbar. It follows the
/// first Wi-Fi adapter, the default playback device and the display battery.
/// </summary>
public sealed class SystemStatus : ObservableObject
{
    private readonly NetworkingService _networking;
    private readonly AudioService _audio;
    private readonly PowerService _power;
    private readonly PowerModeService _powerMode;
    private readonly List<WifiNetwork> _networks = [];
    private NetworkDevice? _wifi;
    private AudioNode? _output;
    private string _networkGlyph = "";
    private string _networkTrackGlyph = "";
    private string _wifiName = "";
    private string _volumeGlyph = "";
    private string _batteryGlyph = "";
    private bool _hasBattery;

    public SystemStatus(NetworkingService networking, AudioService audio, PowerService power, PowerModeService powerMode)
    {
        _networking = networking;
        _audio = audio;
        _power = power;
        _powerMode = powerMode;
        networking.PropertyChanged += OnNetworking;
        networking.Devices.CollectionChanged += OnDevices;
        audio.PropertyChanged += OnAudio;
        power.DisplayDevice.PropertyChanged += OnPower;
        powerMode.PropertyChanged += OnPowerMode;
        BindWifi();
        BindOutput();
        PublishBattery();
    }

    public string NetworkGlyph
    {
        get => _networkGlyph;
        private set => Set(ref _networkGlyph, value);
    }

    public string NetworkTrackGlyph
    {
        get => _networkTrackGlyph;
        private set => Set(ref _networkTrackGlyph, value);
    }

    /// <summary>The connected network's name, or empty when Wi-Fi is not on a network.</summary>
    public string WifiName
    {
        get => _wifiName;
        private set => Set(ref _wifiName, value);
    }

    public string VolumeGlyph
    {
        get => _volumeGlyph;
        private set => Set(ref _volumeGlyph, value);
    }

    public bool HasBattery
    {
        get => _hasBattery;
        private set => Set(ref _hasBattery, value);
    }

    public string BatteryGlyph
    {
        get => _batteryGlyph;
        private set => Set(ref _batteryGlyph, value);
    }

    private void OnNetworking(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkingService.Connectivity))
            PublishNetwork();
    }

    private void OnDevices(object? sender, NotifyCollectionChangedEventArgs e) => BindWifi();

    private void OnWifiDevice(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkDevice.Connected))
            PublishNetwork();
    }

    private void OnNetworks(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnfollowNetworks();
        FollowNetworks();
        PublishNetwork();
    }

    private void OnNetwork(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WifiNetwork.SignalStrength) or nameof(WifiNetwork.Connected))
            PublishNetwork();
    }

    private void OnAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioService.DefaultPlaybackDevice))
            BindOutput();
    }

    private void OnVolume(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioVolume.Volume) or nameof(AudioVolume.Muted))
            PublishVolume();
    }

    private void OnPower(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PowerDevice.IsPresent) or nameof(PowerDevice.Percentage) or nameof(PowerDevice.State))
            PublishBattery();
    }

    private void OnPowerMode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PowerModeService.Effective))
            PublishBattery();
    }

    /// <summary>The first Wi-Fi adapter, which is the one the status icons draw. A later adapter is ignored.</summary>
    private void BindWifi()
    {
        var wifi = _networking.Devices.FirstOrDefault(device => device.Type == DeviceType.Wifi);
        if (!ReferenceEquals(wifi, _wifi))
        {
            UnfollowWifi();
            _wifi = wifi;
            if (_wifi is not null)
            {
                _wifi.PropertyChanged += OnWifiDevice;
                _wifi.Networks.CollectionChanged += OnNetworks;
                FollowNetworks();
            }
        }
        PublishNetwork();
    }

    private void UnfollowWifi()
    {
        if (_wifi is null)
            return;
        _wifi.PropertyChanged -= OnWifiDevice;
        _wifi.Networks.CollectionChanged -= OnNetworks;
        UnfollowNetworks();
        _wifi = null;
    }

    private void FollowNetworks()
    {
        if (_wifi is null)
            return;
        foreach (var network in _wifi.Networks)
        {
            if (network is not WifiNetwork wifi)
                continue;
            wifi.PropertyChanged += OnNetwork;
            _networks.Add(wifi);
        }
    }

    private void UnfollowNetworks()
    {
        foreach (var network in _networks)
            network.PropertyChanged -= OnNetwork;
        _networks.Clear();
    }

    private WifiNetwork? ConnectedNetwork()
    {
        if (_wifi is null)
            return null;
        foreach (var network in _wifi.Networks)
        {
            if (network is WifiNetwork wifi && wifi.Connected)
                return wifi;
        }
        return null;
    }

    private void PublishNetwork()
    {
        var network = ConnectedNetwork();
        var connected = _wifi is { Connected: true };
        var bars = StatusGlyphs.WifiBars(network?.SignalStrength);
        var online = StatusGlyphs.Online(_networking.Connectivity);
        NetworkGlyph = StatusGlyphs.NetworkGlyph(connected, bars, online);
        NetworkTrackGlyph = StatusGlyphs.NetworkTrackGlyph(connected, bars);
        WifiName = network?.Name ?? "";
    }

    private void BindOutput()
    {
        var output = _audio.DefaultPlaybackDevice;
        if (!ReferenceEquals(output, _output))
        {
            if (_output is not null)
                _output.Audio.PropertyChanged -= OnVolume;
            _output = output;
            if (_output is not null)
                _output.Audio.PropertyChanged += OnVolume;
        }
        PublishVolume();
    }

    private void PublishVolume()
    {
        var audio = _output?.Audio;
        VolumeGlyph = StatusGlyphs.VolumeGlyph(audio?.Muted ?? true, audio?.Volume ?? 0);
    }

    private void PublishBattery()
    {
        var battery = _power.DisplayDevice;
        HasBattery = battery.IsPresent;
        BatteryGlyph = StatusGlyphs.BatteryGlyph(
            battery.Percentage,
            battery.State == PowerDeviceState.Charging,
            _powerMode.Effective == EffectivePowerMode.EnergySaver);
    }
}
