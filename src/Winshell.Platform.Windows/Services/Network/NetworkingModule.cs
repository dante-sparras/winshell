using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.Network;

public enum NetworkConnectivity
{
    Unknown = 0,
    None = 1,
    Portal = 2,
    Limited = 3,
    Full = 4,
}

/// <summary>Quickshell's backends, and <c>Windows</c> for this one.</summary>
public enum NetworkBackendType
{
    None = 0,
    NetworkManager = 1,
    Windows = 2,
}

public enum ConnectionState
{
    Unknown = 0,
    Connecting = 1,
    Connected = 2,
    Disconnecting = 3,
    Disconnected = 4,
}

public enum ConnectionFailReason
{
    Unknown = 0,
    NoSecrets = 1,
    WifiClientDisconnected = 2,
    WifiClientFailed = 3,
    WifiAuthTimeout = 4,
    WifiNetworkLost = 5,
}

public enum DeviceType
{
    None = 0,
    Wifi = 1,
    Wired = 2,
}

public enum WifiSecurityType
{
    Wpa3SuiteB192 = 0,
    Sae = 1,
    Wpa2Eap = 2,
    Wpa2Psk = 3,
    WpaEap = 4,
    WpaPsk = 5,
    StaticWep = 6,
    DynamicWep = 7,
    Leap = 8,
    Owe = 9,
    Open = 10,
    Unknown = 11,
}

public enum WifiDeviceMode
{
    AdHoc = 0,
    Station = 1,
    AccessPoint = 2,
    Mesh = 3,
    Unknown = 4,
}

internal static class NetLog
{
    public const string Category = "quickshell.network";
}

/// <summary>Quickshell's <c>NMSettings</c>. Windows has no NetworkManager profiles, so there are none of these.</summary>
public sealed class NMSettings : QObject
{
    [Qml] public string Id => "";

    [Qml] public string Uuid => "";
}

/// <summary>Quickshell's <c>Network</c>: a network a device can connect to.</summary>
[QmlSignal("connectionFailed", "reason")]
public class QmlNetwork : QObject
{
    private NetworkInfo _info;
    private ConnectionState? _pending;

    internal QmlNetwork(NetworkDevice device, NetworkInfo info)
    {
        Device = device;
        _info = info;
    }

    [Qml] public string Name { get { Track(); return _info.Name; } }

    [Qml] public NetworkDevice Device { get; }

    /// <summary>NetworkManager settings profiles; Windows has none.</summary>
    [Qml] public IReadOnlyList<NMSettings> NmSettings => [];

    [Qml] public bool Connected { get { Track(); return State == ConnectionState.Connected; } }

    [Qml] public bool Known { get { Track(); return _info.Known; } }

    [Qml] public ConnectionState State { get { Track(); return _pending ?? _info.State; } }

    [Qml] public bool StateChanging { get { Track(); return State is ConnectionState.Connecting or ConnectionState.Disconnecting; } }

    internal NetworkInfo Info => _info;

    internal virtual void Update(NetworkInfo info)
    {
        var oldState = State;
        var old = _info;
        _info = info;
        if (old.Name != info.Name)
            NotifyChanged("name");
        if (old.Known != info.Known)
            NotifyChanged("known");
        StateUpdated(oldState);
    }

    private void StateUpdated(ConnectionState oldState)
    {
        var state = State;
        if (state == oldState)
            return;
        NotifyChanged("state");
        if ((state == ConnectionState.Connected) != (oldState == ConnectionState.Connected))
            NotifyChanged("connected");
        if ((state is ConnectionState.Connecting or ConnectionState.Disconnecting) != (oldState is ConnectionState.Connecting or ConnectionState.Disconnecting))
            NotifyChanged("stateChanging");
    }

    internal void SetPending(ConnectionState? state)
    {
        var old = State;
        _pending = state;
        StateUpdated(old);
    }

    [Qml("connect")]
    public void ConnectNetwork()
    {
        if (Connected)
        {
            ServiceSupport.Warn(this, NetLog.Category, $"{this} is already connected.");
            return;
        }
        Device.ConnectNetwork(this, null);
    }

    [Qml]
    public void ConnectWithSettings(NMSettings? settings) =>
        ServiceSupport.Warn(this, NetLog.Category, "connectWithSettings is only valid for the NetworkManager backend");

    [Qml]
    public void Disconnect()
    {
        if (!Connected)
        {
            ServiceSupport.Warn(this, NetLog.Category, $"{this} is not currently connected");
            return;
        }
        Device.Disconnect();
    }

    [Qml]
    public void Forget() => Device.ForgetNetwork(this);

    public override string ToString() => $"{TypeName}({_info.Name})";
}

/// <summary>Quickshell's <c>WifiNetwork</c>.</summary>
public sealed class WifiNetwork : QmlNetwork
{
    internal WifiNetwork(NetworkDevice device, NetworkInfo info)
        : base(device, info)
    {
    }

    [Qml] public double SignalStrength { get { Track(); return Info.SignalStrength; } }

    [Qml] public WifiSecurityType Security { get { Track(); return Info.Security; } }

    internal override void Update(NetworkInfo info)
    {
        var old = Info;
        base.Update(info);
        if (old.SignalStrength != info.SignalStrength)
            NotifyChanged("signalStrength");
        if (old.Security != info.Security)
            NotifyChanged("security");
    }

    [Qml]
    public void ConnectWithPsk(string psk)
    {
        if (Connected)
        {
            ServiceSupport.Warn(this, NetLog.Category, $"{this} is already connected.");
            return;
        }
        if (Security is not (WifiSecurityType.WpaPsk or WifiSecurityType.Wpa2Psk or WifiSecurityType.Sae))
        {
            ServiceSupport.Warn(this, NetLog.Category, $"{this} has the wrong security type for a PSK.");
            return;
        }
        Device.ConnectNetwork(this, psk);
    }
}

/// <summary>Quickshell's <c>NetworkDevice</c>: a network adapter.</summary>
public abstract class NetworkDevice : QObject
{
    private readonly Dictionary<string, QmlNetwork> _networks = new(StringComparer.Ordinal);
    private NetworkDeviceInfo _info;

    internal NetworkDevice(NetworkDeviceInfo info, IEventLoop loop)
    {
        _info = info;
        Loop = loop;
        Networks.SetOwner(this);
    }

    internal IEventLoop Loop { get; }

    internal Guid Id => _info.Id;

    internal NetworkDeviceInfo Info => _info;

    [Qml] public DeviceType Type => _info.Type;

    [Qml] public string Name { get { Track(); return _info.Name; } }

    [Qml] public ObjectModel Networks { get; } = new();

    [Qml] public string Address { get { Track(); return _info.Address; } }

    [Qml] public bool Connected { get { Track(); return _info.State == ConnectionState.Connected; } }

    [Qml] public ConnectionState State { get { Track(); return _info.State; } }

    /// <summary>NetworkManager's managed flag. Windows manages every adapter.</summary>
    [Qml]
    public bool NmManaged
    {
        get => true;
        set => ServiceSupport.Warn(this, NetLog.Category, "nmManaged is only valid for the NetworkManager backend");
    }

    /// <summary>Windows connects to saved networks on its own; whether it may is per network, not per device.</summary>
    [Qml]
    public bool Autoconnect
    {
        get => true;
        set => ServiceSupport.Warn(this, NetLog.Category, "Windows cannot stop a device from connecting on its own");
    }

    [Qml]
    public virtual void Disconnect()
    {
        if (_info.State == ConnectionState.Disconnected)
        {
            ServiceSupport.Warn(this, NetLog.Category, $"Device {this} is already disconnected");
            return;
        }
        if (_info.State == ConnectionState.Disconnecting)
        {
            ServiceSupport.Warn(this, NetLog.Category, $"Device {this} is already disconnecting");
            return;
        }
        if (!NetworkBackend.Instance.Disconnect(_info.Id))
            ServiceSupport.Warn(this, NetLog.Category, $"Windows cannot disconnect {this}");
    }

    internal virtual void Update(NetworkDeviceInfo info)
    {
        var old = _info;
        _info = info;
        if (old.Name != info.Name)
            NotifyChanged("name");
        if (old.Address != info.Address)
            NotifyChanged("address");
        if (old.State != info.State)
            NotifyChanged("state");
        if ((old.State == ConnectionState.Connected) != (info.State == ConnectionState.Connected))
            NotifyChanged("connected");

        var next = new List<object?>();
        foreach (var network in info.Networks)
        {
            if (_networks.TryGetValue(network.Name, out var existing))
            {
                existing.Update(network);
            }
            else
            {
                existing = _networks[network.Name] = Type == DeviceType.Wifi ? new WifiNetwork(this, network) : new QmlNetwork(this, network);
                existing.SetOwner(this);
            }
            next.Add(existing);
        }
        foreach (var gone in _networks.Where(kv => !next.Contains(kv.Value)).ToList())
        {
            _networks.Remove(gone.Key);
            gone.Value.Destroy();
        }
        Networks.Set(next);
        NetworksUpdated();
    }

    protected virtual void NetworksUpdated()
    {
    }

    internal void ConnectNetwork(QmlNetwork network, string? psk)
    {
        if (Type != DeviceType.Wifi || !NetworkBackend.Instance.CanScan(_info.Id))
        {
            ServiceSupport.Warn(this, NetLog.Category, $"Windows cannot connect {this} to {network.Name}");
            return;
        }
        network.SetPending(ConnectionState.Connecting);
        var ssid = network.Name;
        ServiceSupport.Run(NetLog.Category, async () =>
        {
            ConnectionFailReason? failure;
            try
            {
                failure = await NetworkBackend.Instance.ConnectAsync(_info.Id, ssid, psk).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Loop.Post(() => network.SetPending(null));
                throw;
            }
            Loop.Post(() =>
            {
                network.SetPending(null);
                if (failure is { } reason)
                    network.Emit("connectionFailed", reason);
            });
        });
    }

    internal void ForgetNetwork(QmlNetwork network)
    {
        if (Type != DeviceType.Wifi)
        {
            ServiceSupport.Warn(this, NetLog.Category, $"Windows cannot forget the wired network of {this}");
            return;
        }
        var error = NetworkBackend.Instance.Forget(_info.Id, network.Name);
        if (error != 0)
            ServiceSupport.Warn(this, NetLog.Category, $"Windows did not forget {network.Name} (error {error})");
    }

    public override string ToString() => $"{TypeName}({_info.Name})";
}

/// <summary>Quickshell's <c>WifiDevice</c>.</summary>
public sealed class WifiDevice : NetworkDevice
{
    private bool _scannerEnabled;

    internal WifiDevice(NetworkDeviceInfo info, IEventLoop loop)
        : base(info, loop)
    {
    }

    [Qml]
    public bool ScannerEnabled
    {
        get { Track(); return _scannerEnabled; }
        set
        {
            if (value == _scannerEnabled)
                return;
            if (value && !NetworkBackend.Instance.CanScan(Id))
            {
                ServiceSupport.Warn(this, NetLog.Category, "Windows gave no access to scan for Wi-Fi networks; turn on location for desktop apps");
                return;
            }
            _scannerEnabled = value;
            NetworkBackend.Instance.SetScanning(Id, value);
            NotifyChanged("scannerEnabled");
        }
    }

    /// <summary>Windows Wi-Fi adapters connect to access points as stations.</summary>
    [Qml] public WifiDeviceMode Mode => WifiDeviceMode.Station;

    protected override void OnDestroy()
    {
        if (_scannerEnabled)
            NetworkBackend.Instance.SetScanning(Id, false);
    }
}

/// <summary>Quickshell's <c>WiredDevice</c>.</summary>
public sealed class WiredDevice : NetworkDevice
{
    private QmlNetwork? _network;

    internal WiredDevice(NetworkDeviceInfo info, IEventLoop loop)
        : base(info, loop)
    {
    }

    [Qml] public QmlNetwork? Network { get { Track(); return _network; } }

    [Qml] public int LinkSpeed { get { Track(); return (int)Math.Min(int.MaxValue, Info.LinkSpeed); } }

    [Qml] public bool HasLink { get { Track(); return Info.HasLink; } }

    internal override void Update(NetworkDeviceInfo info)
    {
        var old = Info;
        base.Update(info);
        if (old.LinkSpeed != info.LinkSpeed)
            NotifyChanged("linkSpeed");
        if (old.HasLink != info.HasLink)
            NotifyChanged("hasLink");
    }

    protected override void NetworksUpdated() =>
        SetField(ref _network, Networks.Rows.OfType<QmlNetwork>().FirstOrDefault(), nameof(Network));

    [Qml]
    public override void Disconnect() =>
        ServiceSupport.Warn(this, NetLog.Category, $"Windows cannot disconnect the wired device {this} without disabling the adapter");
}

/// <summary>Quickshell's <c>Networking</c> singleton.</summary>
public sealed class NetworkingService : QObject
{
    private readonly IEventLoop _loop;
    private readonly IDisposable _subscription;
    private readonly Dictionary<Guid, NetworkDevice> _devices = [];
    private NetworkSnapshot _snapshot = NetworkSnapshot.Initial;
    private bool _connectivityCheckEnabled = true;

    internal NetworkingService(QmlEngine engine)
    {
        _loop = engine.Loop;
        Devices.SetOwner(this);
        Apply(NetworkBackend.Instance.Current);
        _subscription = NetworkBackend.Instance.Subscribe(s => _loop.Post(() => Apply(s)));
    }

    [Qml] public ObjectModel Devices { get; } = new();

    [Qml] public NetworkBackendType Backend => NetworkBackendType.Windows;

    [Qml]
    public bool WifiEnabled
    {
        get { Track(); return _snapshot.WifiEnabled; }
        set
        {
            if (value == _snapshot.WifiEnabled)
                return;
            ServiceSupport.Run(NetLog.Category, async () =>
            {
                if (!await NetworkBackend.Instance.SetWifiEnabledAsync(value).ConfigureAwait(false))
                    _loop.Post(() => ServiceSupport.Warn(this, NetLog.Category, "Windows did not let the shell turn Wi-Fi " + (value ? "on" : "off")));
            });
        }
    }

    [Qml] public bool WifiHardwareEnabled { get { Track(); return _snapshot.WifiHardwareEnabled; } }

    /// <summary>Windows checks connectivity itself (NCSI).</summary>
    [Qml] public bool CanCheckConnectivity => true;

    /// <summary>When off, connectivity reads Unknown, as with checks turned off in NetworkManager.</summary>
    [Qml]
    public bool ConnectivityCheckEnabled
    {
        get { Track(); return _connectivityCheckEnabled; }
        set
        {
            if (SetField(ref _connectivityCheckEnabled, value))
                NotifyChanged("connectivity");
        }
    }

    [Qml]
    public NetworkConnectivity Connectivity
    {
        get { Track(); return _connectivityCheckEnabled && _snapshot.Ready ? _snapshot.Connectivity : NetworkConnectivity.Unknown; }
    }

    [Qml]
    public void CheckConnectivity() => NetworkBackend.Instance.Refresh();

    private void Apply(NetworkSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        var old = _snapshot;
        _snapshot = snapshot;
        if (old.WifiEnabled != snapshot.WifiEnabled)
            NotifyChanged("wifiEnabled");
        if (old.WifiHardwareEnabled != snapshot.WifiHardwareEnabled)
            NotifyChanged("wifiHardwareEnabled");
        if (old.Connectivity != snapshot.Connectivity || old.Ready != snapshot.Ready)
            NotifyChanged("connectivity");

        var next = new List<object?>();
        foreach (var info in snapshot.Devices)
        {
            if (!_devices.TryGetValue(info.Id, out var device))
            {
                device = _devices[info.Id] = info.Type == DeviceType.Wifi ? new WifiDevice(info, _loop) : new WiredDevice(info, _loop);
                device.SetOwner(this);
            }
            device.Update(info);
            next.Add(device);
        }
        foreach (var gone in _devices.Where(kv => !next.Contains(kv.Value)).ToList())
        {
            _devices.Remove(gone.Key);
            gone.Value.Destroy();
        }
        Devices.Set(next);
    }

    protected override void OnDestroy() => _subscription.Dispose();
}

internal static class NetworkingModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Quickshell.Networking")
            .Singleton(e => new NetworkingService(e), "Networking")
            .Uncreatable<NetworkDevice>("NetworkDevice", "Devices can only be acquired through Network")
            .Uncreatable<WifiDevice>("WifiDevice")
            .Uncreatable<WiredDevice>("WiredDevice")
            .Uncreatable<QmlNetwork>("Network", "Network can only be aqcuired through networking devices")
            .Uncreatable<WifiNetwork>("WifiNetwork", "WifiNetwork can only be acquired through WifiDevice")
            .Uncreatable<NMSettings>("NMSettings")
            .Enum<NetworkConnectivity>(c => c switch
            {
                NetworkConnectivity.None => "Not connected to a network",
                NetworkConnectivity.Portal => "Connection intercepted by a captive portal",
                NetworkConnectivity.Limited => "Partial internet connectivity",
                NetworkConnectivity.Full => "Full internet connectivity",
                _ => "Unknown",
            })
            .Enum<NetworkBackendType>(t => Enum.IsDefined(t) ? t.ToString() : "Unknown")
            .Enum<ConnectionState>(s => Enum.IsDefined(s) ? s.ToString() : "Unknown")
            .Enum<ConnectionFailReason>(r => r switch
            {
                ConnectionFailReason.NoSecrets => "Secrets were required but not provided",
                ConnectionFailReason.WifiClientDisconnected => "Wi-Fi supplicant disconnected",
                ConnectionFailReason.WifiClientFailed => "Wi-Fi supplicant failed",
                ConnectionFailReason.WifiAuthTimeout => "Wi-Fi connection took too long to authenticate",
                ConnectionFailReason.WifiNetworkLost => "Wi-Fi network could not be found",
                _ => "Unknown",
            })
            .Enum<DeviceType>(t => Enum.IsDefined(t) ? t.ToString() : "Unknown")
            .Enum<WifiSecurityType>(t => t switch
            {
                WifiSecurityType.Wpa3SuiteB192 => "WPA3 Suite B 192-bit",
                WifiSecurityType.Sae => "WPA3",
                WifiSecurityType.Wpa2Eap => "WPA2 Enterprise",
                WifiSecurityType.Wpa2Psk => "WPA2",
                WifiSecurityType.WpaEap => "WPA Enterprise",
                WifiSecurityType.WpaPsk => "WPA",
                WifiSecurityType.StaticWep => "WEP",
                WifiSecurityType.DynamicWep => "Dynamic WEP",
                WifiSecurityType.Leap => "LEAP",
                WifiSecurityType.Owe => "OWE",
                WifiSecurityType.Open => "Open",
                _ => "Unknown",
            })
            .Enum<WifiDeviceMode>(m => m switch
            {
                WifiDeviceMode.AdHoc => "Ad-Hoc",
                WifiDeviceMode.Station => "Station",
                WifiDeviceMode.AccessPoint => "Access Point",
                WifiDeviceMode.Mesh => "Mesh",
                _ => "Unknown",
            });
    }
}
