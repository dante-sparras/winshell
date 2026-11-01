using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Network;

public enum NetworkConnectivity { Unknown, None, CaptivePortal, LocalAccess, InternetAccess }
public enum NetworkCost { Unknown, Unrestricted, Fixed, Variable }
public enum NetworkAccessStatus { Unspecified, Allowed, DeniedByUser, DeniedBySystem }
public enum WifiRadioState { Unknown, On, Off, Disabled }
public enum AirplaneModeState { Unknown, Off, On }
public enum ConnectionState { Unknown, Connecting, Connected, Disconnecting, Disconnected }
public enum ConnectionFailReason { Unknown, InvalidPassword, Timeout, NetworkUnavailable, UnsupportedAuthentication }
public enum DeviceType { None, Wifi, Wired }
public enum WifiSecurityType { Wpa3SuiteB192, Sae, Wpa2Eap, Wpa2Psk, WpaEap, WpaPsk, StaticWep, Owe, Open, Unknown }

/// <summary>The 802.11 mode of a <see cref="WifiDevice"/>. Windows never reports <see cref="Mesh"/> or <see cref="AdHoc"/>.</summary>
public enum WifiDeviceMode { Unknown, AccessPoint, Mesh, Station, AdHoc }

/// <summary>A Windows connection profile reported by NetworkInformation, not a saved WLAN credential.</summary>
public sealed class NetworkProfile : QObject
{
    private NetworkProfileInfo _info;
    internal NetworkProfile(NetworkProfileInfo info) => _info = info;
    [Qml] public string Id => _info.Id;
    [Qml] public string Name { get { Track(); return _info.Name; } }
    [Qml] public string AdapterId => _info.AdapterId.ToString();
    [Qml] public NetworkConnectivity Connectivity { get { Track(); return _info.Connectivity; } }
    [Qml] public NetworkCost Cost { get { Track(); return _info.Cost; } }
    [Qml] public bool Metered { get { Track(); return _info.Cost is NetworkCost.Fixed or NetworkCost.Variable || _info.Roaming || _info.OverDataLimit; } }
    [Qml] public bool Roaming { get { Track(); return _info.Roaming; } }
    [Qml] public bool OverDataLimit { get { Track(); return _info.OverDataLimit; } }
    [Qml] public bool SignalAvailable { get { Track(); return _info.SignalStrength.HasValue; } }
    [Qml] public double SignalStrength { get { Track(); return _info.SignalStrength ?? 0; } }
    internal void Update(NetworkProfileInfo info)
    {
        if (_info == info) return;
        _info = info;
        foreach (var name in new[] { "name", "connectivity", "cost", "metered", "roaming", "overDataLimit", "signalAvailable", "signalStrength" })
            NotifyChanged(name);
    }
}

[QmlSignal("operationFailed", "message")]
public class QmlNetwork : QObject
{
    private NetworkInfo _info;
    private bool _busy;
    private string _error = "";
    internal QmlNetwork(NetworkDevice device, NetworkInfo info) { Device = device; _info = info; }
    internal NetworkInfo Info => _info;
    [Qml] public string Name { get { Track(); return _info.Name; } }
    [Qml] public NetworkDevice Device { get; }
    [Qml] public bool Connected { get { Track(); return _info.State == ConnectionState.Connected; } }
    [Qml] public bool Saved { get { Track(); return _info.Known; } }
    [Qml] public ConnectionState State { get { Track(); return _info.State; } }
    [Qml] public bool Busy { get { Track(); return _busy; } }
    [Qml] public string Error { get { Track(); return _error; } }
    internal void Update(NetworkInfo info)
    {
        if (_info == info) return;
        _info = info;
        foreach (var name in new[] { "name", "connected", "saved", "state", "signalStrength", "security" }) NotifyChanged(name);
    }
    [Qml("connect")] public void ConnectNetwork() => _ = ConnectAsync(null);
    internal Task ConnectAsync(string? password) => RunAsync(async () =>
    {
        if (Device.Type != DeviceType.Wifi) throw new NotSupportedException("Windows manages wired connections automatically.");
        var failure = await Device.Backend.ConnectAsync(Device.NativeId, Name, password).ConfigureAwait(false);
        if (failure is { } reason) throw new InvalidOperationException(reason.ToString());
    });
    [Qml] public void Disconnect() => _ = RunAsync(() =>
    {
        if (Device.Type != DeviceType.Wifi || !Device.Backend.Disconnect(Device.NativeId))
            throw new NotSupportedException("Windows cannot disconnect this adapter through WiFiAdapter.");
        return Task.CompletedTask;
    });
    [Qml] public void Forget() => _ = RunAsync(() =>
    {
        if (Device.Type != DeviceType.Wifi) throw new NotSupportedException("Only saved Wi-Fi profiles can be forgotten.");
        var error = Device.Backend.Forget(Device.NativeId, Name);
        if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
        return Task.CompletedTask;
    });
    internal async Task RunAsync(Func<Task> action)
    {
        if (_busy || IsDestroyed) return;
        SetField(ref _busy, true, nameof(Busy));
        SetField(ref _error, "", nameof(Error));
        string error = "";
        try { await action().ConfigureAwait(false); }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e)) { error = e.Message; }
        Device.Loop.Post(() =>
        {
            if (IsDestroyed) return;
            SetField(ref _busy, false, nameof(Busy));
            SetField(ref _error, error, nameof(Error));
            if (error.Length > 0) Emit("operationFailed", error);
        });
    }
}

public sealed class WifiNetwork : QmlNetwork
{
    internal WifiNetwork(NetworkDevice device, NetworkInfo info) : base(device, info) { }
    [Qml] public double SignalStrength { get { Track(); return Info.SignalStrength; } }
    [Qml] public WifiSecurityType Security { get { Track(); return Info.Security; } }
    [Qml] public void ConnectWithPassword(string password) => _ = ConnectAsync(password);
}

public abstract class NetworkDevice : QObject
{
    private readonly Dictionary<string, QmlNetwork> _networks = new(StringComparer.Ordinal);
    private NetworkDeviceInfo _info;
    internal NetworkDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
    {
        _info = info; Loop = loop; Backend = backend;
        Networks.SetOwner(this);
    }
    internal IEventLoop Loop { get; }
    internal INetworkBackend Backend { get; }
    internal Guid NativeId => _info.Id;
    internal NetworkDeviceInfo Info => _info;
    [Qml] public string Id => _info.Id.ToString();
    [Qml] public DeviceType Type => _info.Type;
    [Qml] public string Name { get { Track(); return _info.Name; } }
    [Qml] public ObjectModel Networks { get; } = new();
    [Qml] public string Address { get { Track(); return _info.Address; } }
    [Qml] public bool Connected { get { Track(); return _info.State == ConnectionState.Connected; } }
    [Qml] public ConnectionState State { get { Track(); return _info.State; } }
    internal void Update(NetworkDeviceInfo info)
    {
        var old = _info; _info = info;
        if (old.Name != info.Name) NotifyChanged("name");
        if (old.Address != info.Address) NotifyChanged("address");
        if (old.State != info.State) { NotifyChanged("state"); NotifyChanged("connected"); }
        if (Type == DeviceType.Wifi && old.Mode != info.Mode) NotifyChanged("mode");
        foreach (var gone in _networks.Keys.Except(info.Networks.Select(n => n.Name)).ToArray())
        {
            _networks[gone].Destroy(); _networks.Remove(gone);
        }
        foreach (var network in info.Networks)
        {
            if (!_networks.TryGetValue(network.Name, out var item))
            {
                item = Type == DeviceType.Wifi ? new WifiNetwork(this, network) : new QmlNetwork(this, network);
                item.Engine = Engine; item.SetOwner(this); _networks.Add(network.Name, item);
            }
            item.Update(network);
        }
        Networks.Engine = Engine;
        Networks.Set(info.Networks.Select(n => _networks[n.Name]).ToArray());
        NotifyChanged("linkSpeed"); NotifyChanged("hasLink");
    }
}

[QmlSignal("operationFailed", "message")]
public sealed class WifiDevice : NetworkDevice
{
    private bool _scanning;
    private string _error = "";
    internal WifiDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend) : base(info, loop, backend) { }
    [Qml] public WifiDeviceMode Mode { get { Track(); return Info.Mode; } }
    [Qml] public bool Scanning { get { Track(); return _scanning; } }
    [Qml] public string Error { get { Track(); return _error; } }
    [Qml] public void Scan() => _ = ScanAsync();
    internal async Task ScanAsync()
    {
        if (_scanning || IsDestroyed) return;
        SetField(ref _scanning, true, nameof(Scanning));
        SetField(ref _error, "", nameof(Error));
        string error = "";
        try { await Backend.ScanAsync(NativeId).ConfigureAwait(false); }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e)) { error = e.Message; }
        Loop.Post(() =>
        {
            if (IsDestroyed) return;
            SetField(ref _scanning, false, nameof(Scanning));
            SetField(ref _error, error, nameof(Error));
            if (error.Length > 0) Emit("operationFailed", error);
        });
    }
}

public sealed class WiredDevice : NetworkDevice
{
    internal WiredDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend) : base(info, loop, backend) { }
    [Qml] public int LinkSpeed { get { Track(); return (int)Math.Min(int.MaxValue, Info.LinkSpeed); } }
    [Qml] public bool HasLink { get { Track(); return Info.HasLink; } }
}

[QmlSignal("operationFailed", "message")]
public sealed class NetworkingService : QObject
{
    private readonly IEventLoop _loop;
    private readonly INetworkBackend _backend;
    private readonly IDisposable _subscription;
    private readonly Dictionary<Guid, NetworkDevice> _devices = [];
    private readonly Dictionary<string, NetworkProfile> _profiles = new(StringComparer.Ordinal);
    private NetworkSnapshot _snapshot = NetworkSnapshot.Initial;
    private string _error = "";
    private string _radioError = "";
    private bool _requesting;
    private bool _settingWifi;
    internal NetworkingService(QmlEngine engine) : this(NetworkBackend.Instance, engine.Loop) { Engine = engine; }
    internal NetworkingService(INetworkBackend backend, IEventLoop loop)
    {
        _loop = loop; _backend = backend;
        Devices.SetOwner(this); Profiles.SetOwner(this);
        Apply(backend.Current);
        _subscription = backend.Subscribe(s => _loop.Post(() => Apply(s)));
    }
    [Qml] public ObjectModel Devices { get; } = new();
    [Qml] public ObjectModel Profiles { get; } = new();
    [Qml] public bool Ready { get { Track(); return _snapshot.Ready; } }
    [Qml] public bool WifiEnabled { get { Track(); return _snapshot.WifiEnabled; } set => _ = SetWifiEnabledAsync(value); }
    [Qml] public WifiRadioState WifiState { get { Track(); return _snapshot.WifiState; } }
    [Qml] public bool WifiHardwareEnabled { get { Track(); return _snapshot.WifiHardwareEnabled; } }
    [Qml] public NetworkAccessStatus AccessStatus { get { Track(); return _snapshot.Access; } }
    [Qml] public AirplaneModeState AirplaneMode { get { Track(); return _snapshot.AirplaneMode; } }
    [Qml] public NetworkConnectivity Connectivity { get { Track(); return _snapshot.Connectivity; } }
    [Qml] public string Error { get { Track(); return _error; } }
    [Qml] public bool RequestingAccess { get { Track(); return _requesting; } }
    [Qml] public void Refresh() => _backend.Refresh();
    [Qml] public void RequestAccess() => _ = RequestAccessAsync();
    internal async Task RequestAccessAsync()
    {
        if (_requesting || IsDestroyed) return;
        SetField(ref _requesting, true, nameof(RequestingAccess));
        string error = "";
        try { await _backend.RequestAccessAsync().ConfigureAwait(false); }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e)) { error = e.Message; }
        _loop.Post(() =>
        {
            if (IsDestroyed) return;
            _radioError = "";
            SetField(ref _requesting, false, nameof(RequestingAccess));
            Apply(_backend.Current);
            SetField(ref _error, error, nameof(Error));
            if (error.Length > 0) Emit("operationFailed", error);
        });
    }

    // Returns before Windows answers. The property changes only when a snapshot reports the radios did.
    internal async Task SetWifiEnabledAsync(bool enabled)
    {
        if (_settingWifi || IsDestroyed || _snapshot.WifiEnabled == enabled)
            return;
        _settingWifi = true;
        var error = "";
        var report = false;
        try
        {
            error = await _backend.SetWifiEnabledAsync(enabled).ConfigureAwait(false) ?? "";
            report = true;
        }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e))
        {
            error = e.Message;
            report = true;
        }
        finally
        {
            var message = error;
            var shouldReport = report;
            _loop.Post(() =>
            {
                _settingWifi = false;
                if (!shouldReport || IsDestroyed)
                    return;
                _radioError = message;
                Apply(_backend.Current);
                if (message.Length > 0)
                    Emit("operationFailed", message);
            });
        }
    }
    internal void Apply(NetworkSnapshot snapshot)
    {
        if (IsDestroyed) return;
        // A radio refusal stays until the radios change or a read fails. Reaching the new state clears it.
        if (snapshot.Error.Length > 0 || _snapshot.WifiEnabled != snapshot.WifiEnabled)
            _radioError = "";
        _snapshot = snapshot;
        SetField(ref _error, snapshot.Error.Length > 0 ? snapshot.Error : _radioError, nameof(Error));
        foreach (var property in new[] { "ready", "wifiEnabled", "wifiHardwareEnabled", "wifiState", "connectivity", "accessStatus", "airplaneMode" }) NotifyChanged(property);
        Devices.Engine = Profiles.Engine = Engine;
        foreach (var gone in _devices.Keys.Except(snapshot.Devices.Select(d => d.Id)).ToArray())
        {
            _devices[gone].Destroy(); _devices.Remove(gone);
        }
        foreach (var info in snapshot.Devices)
        {
            if (!_devices.TryGetValue(info.Id, out var device))
            {
                device = info.Type == DeviceType.Wifi ? new WifiDevice(info, _loop, _backend) : new WiredDevice(info, _loop, _backend);
                device.Engine = Engine; device.SetOwner(this); _devices.Add(info.Id, device);
            }
            device.Update(info);
        }
        Devices.Set(snapshot.Devices.Select(d => _devices[d.Id]).ToArray());
        var profiles = snapshot.Profiles ?? [];
        foreach (var gone in _profiles.Keys.Except(profiles.Select(p => p.Id)).ToArray())
        {
            _profiles[gone].Destroy(); _profiles.Remove(gone);
        }
        foreach (var info in profiles)
        {
            if (!_profiles.TryGetValue(info.Id, out var profile))
            {
                profile = new NetworkProfile(info) { Engine = Engine }; profile.SetOwner(this); _profiles.Add(info.Id, profile);
            }
            profile.Update(info);
        }
        Profiles.Set(profiles.Select(p => _profiles[p.Id]).ToArray());
    }
    protected override void OnDestroy() => _subscription.Dispose();
}

internal static class NetworkingModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Network")
            .Singleton(e => new NetworkingService(e), "Networking")
            .Uncreatable<NetworkDevice>("NetworkDevice")
            .Uncreatable<WifiDevice>("WifiDevice")
            .Uncreatable<WiredDevice>("WiredDevice")
            .Uncreatable<QmlNetwork>("Network")
            .Uncreatable<WifiNetwork>("WifiNetwork")
            .Uncreatable<NetworkProfile>("NetworkProfile")
            .Enum<NetworkConnectivity>(s => s.ToString())
            .Enum<NetworkCost>(s => s.ToString())
            .Enum<NetworkAccessStatus>(s => s.ToString())
            .Enum<AirplaneModeState>(s => s.ToString())
            .Enum<WifiRadioState>(s => s.ToString())
            .Enum<ConnectionState>(s => s.ToString())
            .Enum<ConnectionFailReason>(s => s.ToString())
            .Enum<DeviceType>(s => s.ToString())
            .Enum<WifiSecurityType>(s => s.ToString())
            .Enum<WifiDeviceMode>(s => s.ToString());
    }
}
