using System.Net.NetworkInformation;
using Microsoft.Win32;
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

internal sealed record NetworkProfileInfo(string Id, string Name, Guid AdapterId, NetworkConnectivity Connectivity,
    NetworkCost Cost, bool Roaming, bool OverDataLimit, double? SignalStrength);

internal sealed record NetworkSnapshot(
    bool Ready,
    IReadOnlyList<NetworkDeviceInfo> Devices,
    NetworkConnectivity Connectivity,
    bool WifiEnabled,
    bool WifiHardwareEnabled,
    IReadOnlyList<NetworkProfileInfo>? Profiles = null,
    NetworkAccessStatus Access = NetworkAccessStatus.Unspecified,
    AirplaneModeState AirplaneMode = AirplaneModeState.Unknown,
    string Error = "",
    WifiRadioState WifiState = WifiRadioState.Unknown)
{
    public static readonly NetworkSnapshot Initial = new(false, [], NetworkConnectivity.Unknown, false, false);
}

/// <summary>
/// The network adapters of this PC. Wired and Wi-Fi adapters come from System.Net and iphlpapi; Wi-Fi
/// networks from WinRT's WiFiAdapter, which needs the location permission for desktop apps; saved
/// networks and connection states from wlanapi; connectivity from WinRT's NetworkInformation.
/// </summary>
internal sealed class NetworkBackend : SnapshotSource<NetworkSnapshot>, INetworkBackend
{
    public static readonly NetworkBackend Instance = new();

    private readonly object _lock = new();
    private readonly Dictionary<Guid, WiFiAdapter> _wifiAdapters = [];
    private NetworkAccessStatus _access;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private readonly List<Radio> _radios = [];


    private NetworkBackend()
        : base(NetworkSnapshot.Initial)
    {
    }

    protected override string Category => "network";

    protected override async Task StartAsync()
    {
        WinNetworkInformation.NetworkStatusChanged += _ => Refresh();
        NetworkChange.NetworkAddressChanged += (_, _) => Refresh();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Refresh();
        NetworkNative.Listen(Refresh);
        await RefreshNowAsync().ConfigureAwait(false);

        // Reading radio state needs no control permission and must never prompt on import.
        try
        {
            foreach (var radio in (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.WiFi))
            {
                radio.StateChanged += (_, _) => Refresh();
                lock (_lock) _radios.Add(radio);
            }
        }
        catch (UnauthorizedAccessException) { }
        await RefreshNowAsync().ConfigureAwait(false);
    }

    public async Task RequestAccessAsync()
    {
        await _accessGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var access = await WiFiAdapter.RequestAccessAsync();
            var adapters = access == WiFiAccessStatus.Allowed ? await WiFiAdapter.FindAllAdaptersAsync() : null;
            lock (_lock)
            {
                _wifiAdapters.Clear();
                if (adapters is not null)
                {
                    foreach (var adapter in adapters)
                        _wifiAdapters[adapter.NetworkAdapter.NetworkAdapterId] = adapter;
                }

                _access = MapAccess(access);
            }
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            lock (_lock)
            {
                _wifiAdapters.Clear();
                _access = NetworkAccessStatus.Unspecified;
            }
            throw;
        }
        finally
        {
            _accessGate.Release();
            await RefreshNowAsync().ConfigureAwait(false);
        }
    }

    internal static NetworkAccessStatus MapAccess(WiFiAccessStatus status) => status switch
    {
        WiFiAccessStatus.Allowed => NetworkAccessStatus.Allowed,
        WiFiAccessStatus.DeniedByUser => NetworkAccessStatus.DeniedByUser,
        WiFiAccessStatus.DeniedBySystem => NetworkAccessStatus.DeniedBySystem,
        _ => NetworkAccessStatus.Unspecified,
    };
    internal static bool IsExpectedFailure(Exception e) => e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException
        or InvalidOperationException or NotSupportedException or ArgumentException or System.ComponentModel.Win32Exception or IOException;

    protected override Task<NetworkSnapshot> ReadAsync()
    {
        try { return ReadStateAsync(); }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return Task.FromResult(NetworkSnapshot.Initial with { Error = e.Message });
        }
    }

    private Task<NetworkSnapshot> ReadStateAsync()
    {
        var profiles = WinNetworkInformation.GetConnectionProfiles().Where(p => p.NetworkAdapter is not null).ToList();
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

        var connectivity = MapConnectivity(WinNetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() ?? NetworkConnectivityLevel.None);

        bool wifiEnabled, wifiHardware;
        WifiRadioState wifiState;
        lock (_lock)
        {

            wifiEnabled = _radios.Any(r => r.State == RadioState.On);
            wifiHardware = _radios.Any(r => r.State is RadioState.On or RadioState.Off);
            wifiState = wifiEnabled ? WifiRadioState.On : _radios.Count == 0 ? WifiRadioState.Unknown :
                _radios.All(r => r.State == RadioState.Disabled) ? WifiRadioState.Disabled : WifiRadioState.Off;
        }
        var connectionProfiles = profiles.Select(p =>
        {
            var cost = p.GetConnectionCost();
            return new NetworkProfileInfo(p.NetworkAdapter.NetworkAdapterId + ":" + p.ProfileName, p.ProfileName,
                p.NetworkAdapter.NetworkAdapterId, MapConnectivity(p.GetNetworkConnectivityLevel()),
                MapCost(cost.NetworkCostType), cost.Roaming, cost.OverDataLimit,
                p.GetSignalBars() is { } bars ? bars / 5.0 : null);
        }).ToArray();
        return Task.FromResult(new NetworkSnapshot(true, devices, connectivity, wifiEnabled, wifiHardware,
            connectionProfiles, _access, ReadAirplaneMode(), WifiState: wifiState));
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
            networks[connectedSsid] = new NetworkInfo(connectedSsid, deviceState, known.Contains(connectedSsid), bars, Security(profile.NetworkSecuritySettings));
        }
        return [.. networks.Values.OrderByDescending(n => n.State == ConnectionState.Connected).ThenByDescending(n => n.SignalStrength)];
    }

    private static string? ConnectedSsid(ConnectionProfile profile)
    {
        if (!profile.IsWlanConnectionProfile)
            return null;
        // Unlike scan/BSSID access, reading the connected SSID does not request location consent.
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
            return ConnectionFailReason.NetworkUnavailable;
        var result = psk is null
            ? await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic)
            : await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = psk });
        Refresh();
        return MapConnectionResult(result.ConnectionStatus);
    }

    internal static ConnectionFailReason? MapConnectionResult(WiFiConnectionStatus status) => status switch
        {
            WiFiConnectionStatus.Success => null,
            WiFiConnectionStatus.InvalidCredential => ConnectionFailReason.InvalidPassword,
            WiFiConnectionStatus.Timeout => ConnectionFailReason.Timeout,
            WiFiConnectionStatus.NetworkNotAvailable => ConnectionFailReason.NetworkUnavailable,
            WiFiConnectionStatus.UnsupportedAuthenticationProtocol => ConnectionFailReason.UnsupportedAuthentication,
            _ => ConnectionFailReason.Unknown,
        };

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

    public async Task ScanAsync(Guid device)
    {
        var adapter = Adapter(device) ?? throw new UnauthorizedAccessException("Call Networking.requestAccess() before scanning Wi-Fi networks.");
        try { await adapter.ScanAsync(); }
        catch (UnauthorizedAccessException)
        {
            lock (_lock)
            {
                _wifiAdapters.Clear();

                _access = NetworkAccessStatus.DeniedBySystem;
            }
            throw;
        }
        finally { Refresh(); }
    }

    internal static NetworkConnectivity MapConnectivity(NetworkConnectivityLevel value) => value switch
    {
        NetworkConnectivityLevel.InternetAccess => NetworkConnectivity.InternetAccess,
        NetworkConnectivityLevel.ConstrainedInternetAccess => NetworkConnectivity.CaptivePortal,
        NetworkConnectivityLevel.LocalAccess => NetworkConnectivity.LocalAccess,
        _ => NetworkConnectivity.None,
    };

    internal static NetworkCost MapCost(NetworkCostType value) => value switch
    {
        NetworkCostType.Unrestricted => NetworkCost.Unrestricted,
        NetworkCostType.Fixed => NetworkCost.Fixed,
        NetworkCostType.Variable => NetworkCost.Variable,
        _ => NetworkCost.Unknown,
    };

    // Windows exposes no public WinRT aggregate airplane-mode API. Read the system's persisted
    // RadioManagement flag when present; never infer it from individual radios (which can be overridden).
    internal static AirplaneModeState MapAirplaneMode(object? value) => value switch
    {
        0 => AirplaneModeState.Off,
        1 => AirplaneModeState.On,
        _ => AirplaneModeState.Unknown,
    };

    private static AirplaneModeState ReadAirplaneMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement");
            return MapAirplaneMode(key?.GetValue("SystemRadioState"));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return AirplaneModeState.Unknown;
        }
    }

    internal Task<NetworkSnapshot> ReadForTestAsync() => ReadAsync();
}
