using System.Net.NetworkInformation;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;
using static Winshell.Platform.Windows.Services.Network.NetworkNative;
using WinNetworkInformation = Windows.Networking.Connectivity.NetworkInformation;

namespace Winshell.Platform.Windows.Services.Network;

internal sealed record NetworkInfo(string Name, ConnectionState State, bool Known, double SignalStrength, WifiSecurityType Security);

internal sealed record NetworkDeviceInfo(
    Guid Id,
    DeviceType Type,
    string Name,
    string Address,
    ConnectionState State,
    bool HasLink,
    uint LinkSpeed,
    IReadOnlyList<NetworkInfo> Networks);

internal sealed record NetworkSnapshot(
    bool Ready,
    IReadOnlyList<NetworkDeviceInfo> Devices,
    NetworkConnectivity Connectivity,
    bool WifiEnabled,
    bool WifiHardwareEnabled)
{
    public static readonly NetworkSnapshot Initial = new(false, [], NetworkConnectivity.Unknown, false, false);
}

/// <summary>
/// The network adapters of this PC. Wired and Wi-Fi adapters come from System.Net and iphlpapi; Wi-Fi
/// networks from WinRT's WiFiAdapter, which needs the location permission for desktop apps; saved
/// networks and connection states from wlanapi; connectivity from WinRT's NetworkInformation.
/// </summary>
internal sealed class NetworkBackend : SnapshotSource<NetworkSnapshot>
{
    public static readonly NetworkBackend Instance = new();

    private readonly object _lock = new();
    private readonly Dictionary<Guid, WiFiAdapter> _wifiAdapters = [];
    private readonly Dictionary<Guid, int> _scanners = [];
    private readonly List<Radio> _radios = [];
    private bool _wifiAccess;

    private NetworkBackend()
        : base(NetworkSnapshot.Initial)
    {
    }

    protected override string Category => "quickshell.network";

    protected override async Task StartAsync()
    {
        WinNetworkInformation.NetworkStatusChanged += _ => Refresh();
        NetworkChange.NetworkAddressChanged += (_, _) => Refresh();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Refresh();
        NetworkNative.Listen(Refresh);
        await RefreshNowAsync().ConfigureAwait(false);

        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
            {
                foreach (var radio in (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.WiFi))
                {
                    radio.StateChanged += (_, _) => Refresh();
                    lock (_lock)
                        _radios.Add(radio);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }

        // The list of Wi-Fi networks needs this access. A desktop app has it when location is on for
        // desktop apps; without it, a Wi-Fi device only shows the network it is connected to.
        var access = await WiFiAdapter.RequestAccessAsync();
        if (access == WiFiAccessStatus.Allowed)
        {
            foreach (var adapter in await WiFiAdapter.FindAllAdaptersAsync())
            {
                adapter.AvailableNetworksChanged += (_, _) => Refresh();
                lock (_lock)
                    _wifiAdapters[adapter.NetworkAdapter.NetworkAdapterId] = adapter;
            }
            lock (_lock)
                _wifiAccess = true;
        }
        else
        {
            Winshell.Runtime.Log.Default.Info(Category, $"Windows denied access to Wi-Fi networks ({access}); turn on location for desktop apps to list them");
        }
        await RefreshNowAsync().ConfigureAwait(false);
    }

    protected override Task<NetworkSnapshot> ReadAsync()
    {
        var profiles = WinNetworkInformation.GetConnectionProfiles()
            .Where(p => p.NetworkAdapter is not null && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
            .ToList();
        var wlanStates = InterfaceStates();

        var devices = new List<NetworkDeviceInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var type = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => DeviceType.Wifi,
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => DeviceType.Wired,
                _ => DeviceType.None,
            };
            if (type == DeviceType.None || !Guid.TryParse(nic.Id, out var id))
                continue;
            // Hyper-V switches, VPNs, Wi-Fi Direct and filter drivers are not devices a shell shows.
            if (GetInterface(id) is not { HardwareInterface: true } row)
                continue;
            if (type == DeviceType.Wired && nic.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                continue;

            var up = nic.OperationalStatus == OperationalStatus.Up;
            var profile = profiles.FirstOrDefault(p => p.NetworkAdapter.NetworkAdapterId == id);
            var address = ServiceSupport.FormatMac(nic.GetPhysicalAddress().GetAddressBytes());
            var hasLink = row.MediaConnected;
            var speed = (uint)Math.Min(uint.MaxValue, row.LinkSpeed / 1_000_000);

            if (type == DeviceType.Wired)
            {
                var state = up ? ConnectionState.Connected : ConnectionState.Disconnected;
                NetworkInfo[] networks = hasLink ? [new NetworkInfo(profile?.ProfileName ?? nic.Name, state, true, 0, WifiSecurityType.Unknown)] : [];
                devices.Add(new NetworkDeviceInfo(id, type, nic.Name, address, state, hasLink, hasLink ? speed : 0, networks));
            }
            else
            {
                var state = wlanStates.TryGetValue(id, out var wlan)
                    ? wlan switch
                    {
                        WlanInterfaceState.Connected or WlanInterfaceState.AdHocNetworkFormed => ConnectionState.Connected,
                        WlanInterfaceState.Associating or WlanInterfaceState.Discovering or WlanInterfaceState.Authenticating => ConnectionState.Connecting,
                        WlanInterfaceState.Disconnecting => ConnectionState.Disconnecting,
                        WlanInterfaceState.Disconnected => ConnectionState.Disconnected,
                        _ => ConnectionState.Unknown,
                    }
                    : up ? ConnectionState.Connected : ConnectionState.Disconnected;
                devices.Add(new NetworkDeviceInfo(id, type, nic.Name, address, state, false, 0, ReadWifiNetworks(id, state, profile)));
            }
        }

        var connectivity = WinNetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() switch
        {
            NetworkConnectivityLevel.InternetAccess => NetworkConnectivity.Full,
            NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkConnectivity.Portal,
            NetworkConnectivityLevel.LocalAccess => NetworkConnectivity.Limited,
            _ => NetworkConnectivity.None,
        };

        bool wifiEnabled, wifiHardware;
        lock (_lock)
        {
            // Without a Wi-Fi radio to ask, a Wi-Fi adapter that exists is on.
            var hasWifi = devices.Any(d => d.Type == DeviceType.Wifi);
            wifiEnabled = _radios.Count > 0 ? _radios.Any(r => r.State == RadioState.On) : hasWifi;
            wifiHardware = _radios.Count > 0 ? _radios.Any(r => r.State != RadioState.Disabled) : hasWifi;
        }
        return Task.FromResult(new NetworkSnapshot(true, devices, connectivity, wifiEnabled, wifiHardware));
    }

    private List<NetworkInfo> ReadWifiNetworks(Guid id, ConnectionState deviceState, ConnectionProfile? profile)
    {
        var known = ProfileNames(id);
        var connectedSsid = profile is null ? null : ConnectedSsid(profile);
        var networks = new Dictionary<string, NetworkInfo>(StringComparer.Ordinal);
        WiFiAdapter? adapter;
        lock (_lock)
            _wifiAdapters.TryGetValue(id, out adapter);
        if (adapter is not null)
        {
            foreach (var network in adapter.NetworkReport.AvailableNetworks)
            {
                // A hidden network has no name to show or connect by.
                if (string.IsNullOrEmpty(network.Ssid))
                    continue;
                var strength = Math.Clamp(2 * (network.NetworkRssiInDecibelMilliwatts + 100), 0, 100) / 100;
                if (networks.TryGetValue(network.Ssid, out var seen) && seen.SignalStrength >= strength)
                    continue;
                var state = network.Ssid == connectedSsid ? deviceState : ConnectionState.Disconnected;
                networks[network.Ssid] = new NetworkInfo(network.Ssid, state, known.Contains(network.Ssid), strength, Security(network.SecuritySettings));
            }
        }
        if (connectedSsid is not null && !networks.ContainsKey(connectedSsid))
        {
            var bars = profile!.GetSignalBars() is { } b ? b / 5.0 : 0;
            networks[connectedSsid] = new NetworkInfo(connectedSsid, deviceState, true, bars, Security(profile.NetworkSecuritySettings));
        }
        return [.. networks.Values.OrderByDescending(n => n.State == ConnectionState.Connected).ThenByDescending(n => n.SignalStrength)];
    }

    private static string? ConnectedSsid(ConnectionProfile profile)
    {
        if (!profile.IsWlanConnectionProfile)
            return null;
        // The SSID needs the location permission; the profile name is the SSID unless renamed.
        var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
        return string.IsNullOrEmpty(ssid) ? profile.ProfileName : ssid;
    }

    private static WifiSecurityType Security(NetworkSecuritySettings? settings)
    {
        if (settings is null)
            return WifiSecurityType.Unknown;
        return settings.NetworkAuthenticationType switch
        {
            NetworkAuthenticationType.Open80211 when settings.NetworkEncryptionType is NetworkEncryptionType.Wep or NetworkEncryptionType.Wep40 or NetworkEncryptionType.Wep104
                => WifiSecurityType.StaticWep,
            NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None => WifiSecurityType.Open,
            NetworkAuthenticationType.SharedKey80211 => WifiSecurityType.StaticWep,
            NetworkAuthenticationType.Wpa => WifiSecurityType.WpaEap,
            NetworkAuthenticationType.WpaPsk => WifiSecurityType.WpaPsk,
            NetworkAuthenticationType.Rsna or NetworkAuthenticationType.Wpa3Enterprise => WifiSecurityType.Wpa2Eap,
            NetworkAuthenticationType.RsnaPsk => WifiSecurityType.Wpa2Psk,
            NetworkAuthenticationType.Wpa3Enterprise192Bits => WifiSecurityType.Wpa3SuiteB192,
            NetworkAuthenticationType.Wpa3Sae => WifiSecurityType.Sae,
            NetworkAuthenticationType.Owe => WifiSecurityType.Owe,
            _ => WifiSecurityType.Unknown,
        };
    }

    // Actions. Each returns an error message, or null when Windows did what was asked.

    private WiFiAdapter? Adapter(Guid id)
    {
        lock (_lock)
            return _wifiAdapters.GetValueOrDefault(id);
    }

    /// <summary>Connects a Wi-Fi device to a network by its SSID, with a PSK or the saved credentials.</summary>
    public async Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? psk)
    {
        var adapter = Adapter(device);
        var network = adapter?.NetworkReport.AvailableNetworks
            .Where(n => n.Ssid == ssid)
            .OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts)
            .FirstOrDefault();
        if (adapter is null || network is null)
            return ConnectionFailReason.WifiNetworkLost;
        var result = psk is null
            ? await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic)
            : await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = psk });
        Refresh();
        return result.ConnectionStatus switch
        {
            WiFiConnectionStatus.Success => null,
            WiFiConnectionStatus.InvalidCredential => ConnectionFailReason.NoSecrets,
            WiFiConnectionStatus.Timeout => ConnectionFailReason.WifiAuthTimeout,
            WiFiConnectionStatus.NetworkNotAvailable => ConnectionFailReason.WifiNetworkLost,
            WiFiConnectionStatus.UnsupportedAuthenticationProtocol => ConnectionFailReason.WifiClientFailed,
            _ => ConnectionFailReason.Unknown,
        };
    }

    /// <summary>Disconnects a Wi-Fi device. False when Windows gave no access to it.</summary>
    public bool Disconnect(Guid device)
    {
        if (Adapter(device) is not { } adapter)
            return false;
        adapter.Disconnect();
        Refresh();
        return true;
    }

    public uint Forget(Guid device, string name)
    {
        var error = DeleteProfile(device, name);
        Refresh();
        return error;
    }

    public bool CanScan(Guid device) => Adapter(device) is not null;

    /// <summary>Scans for networks every few seconds while any engine wants to.</summary>
    public void SetScanning(Guid device, bool scanning)
    {
        bool start;
        lock (_lock)
        {
            var count = _scanners.GetValueOrDefault(device) + (scanning ? 1 : -1);
            start = scanning && count == 1;
            if (count <= 0)
                _scanners.Remove(device);
            else
                _scanners[device] = count;
        }
        if (start)
            ServiceSupport.Run(Category, () => ScanLoopAsync(device));
    }

    private async Task ScanLoopAsync(Guid device)
    {
        while (true)
        {
            lock (_lock)
            {
                if (!_scanners.ContainsKey(device))
                    return;
            }
            if (Adapter(device) is { } adapter)
                await adapter.ScanAsync();
            Refresh();
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }

    /// <summary>Turns the Wi-Fi radios on or off. False when Windows gave no access to them.</summary>
    public async Task<bool> SetWifiEnabledAsync(bool enabled)
    {
        List<Radio> radios;
        lock (_lock)
            radios = [.. _radios];
        if (radios.Count == 0)
            return false;
        var ok = true;
        foreach (var radio in radios)
            ok &= await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        Refresh();
        return ok;
    }

    public bool HasWifiAccess
    {
        get
        {
            lock (_lock)
                return _wifiAccess;
        }
    }
}
