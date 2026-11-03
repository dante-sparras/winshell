using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Winshell.Network;

/// <summary>A Windows connection profile reported by NetworkInformation, not a saved WLAN credential.</summary>
public sealed class NetworkProfile : ObservableObject
{
    private NetworkProfileInfo _info;

    internal NetworkProfile(NetworkProfileInfo info) => _info = info;

    /// <summary>The adapter's ID and the profile's name, joined by a colon.</summary>
    public string Id => _info.Id;

    /// <summary>The profile's name, as Windows shows the connection. For Wi-Fi it is usually the SSID.</summary>
    public string Name => _info.Name;

    /// <summary>The <see cref="NetworkDevice.Id"/> of the adapter the profile is on.</summary>
    public string AdapterId => _info.AdapterId.ToString();

    /// <summary>What this connection can reach.</summary>
    public NetworkConnectivity Connectivity => _info.Connectivity;

    /// <summary>How this connection's data is charged.</summary>
    public NetworkCost Cost => _info.Cost;

    /// <summary>True for a fixed or variable cost, roaming, or an exceeded data limit.</summary>
    public bool Metered => IsMetered(_info);

    /// <summary>Whether the connection is roaming, outside its provider's own network.</summary>
    public bool Roaming => _info.Roaming;

    /// <summary>Whether the connection has used more data than its plan's limit.</summary>
    public bool OverDataLimit => _info.OverDataLimit;

    /// <summary>Whether Windows reports a signal for this connection. False for a wired one.</summary>
    public bool SignalAvailable => _info.SignalStrength.HasValue;

    /// <summary>The signal strength, from 0 to 1. 0 when <see cref="SignalAvailable"/> is false.</summary>
    public double SignalStrength => _info.SignalStrength ?? 0;

    private static bool IsMetered(NetworkProfileInfo info) =>
        info.Cost is NetworkCost.Fixed or NetworkCost.Variable || info.Roaming || info.OverDataLimit;

    internal void Update(NetworkProfileInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.AdapterId != info.AdapterId, nameof(AdapterId));
        Changed(old.Connectivity != info.Connectivity, nameof(Connectivity));
        Changed(old.Cost != info.Cost, nameof(Cost));
        Changed(IsMetered(old) != IsMetered(info), nameof(Metered));
        Changed(old.Roaming != info.Roaming, nameof(Roaming));
        Changed(old.OverDataLimit != info.OverDataLimit, nameof(OverDataLimit));
        Changed(old.SignalStrength.HasValue != info.SignalStrength.HasValue, nameof(SignalAvailable));
        Changed((old.SignalStrength ?? 0) != (info.SignalStrength ?? 0), nameof(SignalStrength));
    }
}

/// <summary>A network of an adapter. Wi-Fi networks are <see cref="WifiNetwork"/> objects.</summary>
public class Network : ObservableObject
{
    private NetworkInfo _info;
    private bool _busy;
    private string _error = "";

    internal Network(NetworkDevice device, NetworkInfo info)
    {
        Device = device;
        _info = info;
    }

    /// <summary>An action failed, with the same text as <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    internal NetworkInfo Info => _info;

    internal bool Removed { get; private set; }

    /// <summary>The SSID of a Wi-Fi network. For a wired network, the connection profile's name, or the adapter's.</summary>
    public string Name => _info.Name;

    /// <summary>The adapter this network is on.</summary>
    public NetworkDevice Device { get; }

    /// <summary>Whether the adapter is connected to this network.</summary>
    public bool Connected => _info.State == ConnectionState.Connected;

    /// <summary>A saved profile matches this network. True for a wired network.</summary>
    public bool Saved => _info.Known;

    /// <summary>
    /// The adapter's connection state on this network. <see cref="ConnectionState.Disconnected"/> for every Wi-Fi
    /// network the adapter is not on.
    /// </summary>
    public ConnectionState State => _info.State;

    /// <summary>An action is running. Other calls are ignored until it ends.</summary>
    public bool Busy => _busy;

    /// <summary>Why the last action failed. Empty while an action runs and after one succeeds.</summary>
    public string Error => _error;

    /// <summary>
    /// The saved Wi-Fi profiles for this network: those whose SSID is its name, and those that can't be read whose
    /// profile name is. Empty for a wired network.
    /// </summary>
    public ObservableCollection<WifiProfile> SavedProfiles { get; } = [];

    /// <summary>Connects to this Wi-Fi network with saved credentials. Fails for a wired network.</summary>
    public void Connect() => _ = ConnectAsync(null);

    /// <summary>Connects with a saved profile of this network. The next snapshot reports whether the adapter associated.</summary>
    public void ConnectWithProfile(WifiProfile? profile) => _ = ConnectProfileAsync(profile);

    /// <summary>
    /// Disconnects this network's Wi-Fi adapter. Fails for a wired network, and until
    /// <see cref="NetworkingService.RequestAccess"/> is allowed.
    /// </summary>
    public void Disconnect() => _ = DisconnectAsync();

    /// <summary>Deletes the saved Wi-Fi profile whose name is this SSID.</summary>
    public void Forget() => _ = ForgetAsync();

    // These two block until Windows answers, so they run off the calling thread.
    internal Task DisconnectAsync() => RunAsync(() => Task.Run(() =>
    {
        if (Device.Type != DeviceType.Wifi || !Device.Backend.Disconnect(Device.NativeId))
            throw new NotSupportedException("Windows cannot disconnect this adapter through WiFiAdapter.");
    }));

    internal Task ForgetAsync() => RunAsync(() => Task.Run(() =>
    {
        if (Device.Type != DeviceType.Wifi)
            throw new NotSupportedException("Only saved Wi-Fi profiles can be forgotten.");
        var error = Device.Backend.Forget(Device.NativeId, Name);
        if (error != 0)
            throw new Win32Exception((int)error);
    }));

    internal Task ConnectAsync(string? password) => RunAsync(async () =>
    {
        if (Device.Type != DeviceType.Wifi)
            throw new NotSupportedException("Windows manages wired connections automatically.");
        var failure = await Device.Backend.ConnectAsync(Device.NativeId, Name, password).ConfigureAwait(false);
        if (failure is { } reason)
            throw new InvalidOperationException(reason.ToString());
    });

    internal Task ConnectProfileAsync(WifiProfile? profile) => RunAsync(async () =>
    {
        if (profile is null || profile.Removed || !ReferenceEquals(profile.Network, this))
            throw new InvalidOperationException("That saved profile is not on this network.");
        if (Device.Type != DeviceType.Wifi)
            throw new NotSupportedException("Windows manages wired connections automatically.");
        var failure = await Device.Backend.ConnectProfileAsync(Device.NativeId, profile.Name, profile.Hidden).ConfigureAwait(false);
        if (failure is { } message)
            throw new InvalidOperationException(message);
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || Removed)
            return;
        Set(ref _busy, true, nameof(Busy));
        Set(ref _error, "", nameof(Error));
        var error = "";
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        Device.Loop.Post(() =>
        {
            if (Removed)
                return;
            Set(ref _busy, false, nameof(Busy));
            Set(ref _error, error, nameof(Error));
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
    }

    internal virtual void Update(NetworkInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Known != info.Known, nameof(Saved));
        Changed(old.State != info.State, nameof(State));
        Changed((old.State == ConnectionState.Connected) != Connected, nameof(Connected));
        SavedProfiles.Sync(info.SavedProfiles ?? [], p => p.Id, i => i.Id, i => new WifiProfile(this, i), (p, i) => p.Update(i), p => p.Remove());
    }

    internal void Remove()
    {
        Removed = true;
        foreach (var profile in SavedProfiles)
            profile.Remove();
    }
}

/// <summary>A Wi-Fi network in range.</summary>
public sealed class WifiNetwork : Network
{
    internal WifiNetwork(NetworkDevice device, NetworkInfo info)
        : base(device, info)
    {
    }

    /// <summary>The signal strength, from 0 to 1.</summary>
    public double SignalStrength => Info.SignalStrength;

    /// <summary>How the network is secured.</summary>
    public WifiSecurityType Security => Info.Security;

    /// <summary>Connects with a password, for a secured network that isn't saved. The password is not kept.</summary>
    public void ConnectWithPassword(string password) => _ = ConnectAsync(password);

    internal override void Update(NetworkInfo info)
    {
        var old = Info;
        base.Update(info);
        Changed(old.SignalStrength != info.SignalStrength, nameof(SignalStrength));
        Changed(old.Security != info.Security, nameof(Security));
    }
}

/// <summary>A saved WLAN profile for one network. It is not a Windows connection profile, and it never holds a key.</summary>
public sealed class WifiProfile : ObservableObject
{
    private readonly Network _network;
    private WifiProfileInfo _info;
    private string _actionError = "";
    private bool _writing;

    internal WifiProfile(Network network, WifiProfileInfo info)
    {
        _network = network;
        _info = info;
    }

    /// <summary>Setting <see cref="AutoConnect"/>, <see cref="ClearPassword"/> or <see cref="Forget"/> failed.</summary>
    public event Action<string>? OperationFailed;

    internal Network Network => _network;

    internal bool Hidden => _info.Hidden;

    internal bool Removed { get; private set; }

    /// <summary>The adapter's ID and the profile's name, joined by a colon.</summary>
    public string Id => _info.Id;

    /// <summary>The profile's name in Windows. Usually the SSID, but it can differ.</summary>
    public string Name => _info.Name;

    /// <summary>The SSID the profile connects to. Empty when <see cref="Loaded"/> is false.</summary>
    public string Ssid => _info.Ssid;

    /// <summary>
    /// Whether the connection mode is automatic. Setting it changes only that element, and the value stays what the
    /// last read reported until a later read shows the change.
    /// </summary>
    public bool AutoConnect
    {
        get => _info.AutoConnect;
        set
        {
            if (Removed || _writing || value == _info.AutoConnect)
                return;
            _ = SetAutoConnectAsync(value);
        }
    }

    /// <summary>
    /// How the profile's network is secured. <see cref="WifiSecurityType.Unknown"/> when <see cref="Loaded"/> is
    /// false.
    /// </summary>
    public WifiSecurityType Security => _info.Security;

    /// <summary>Whether the profile has a key element. The key text is not read.</summary>
    public bool HasPassword => _info.HasPassword;

    /// <summary>
    /// Whether the profile could be read. When false, only <see cref="Id"/> and <see cref="Name"/> are set.
    /// </summary>
    public bool Loaded => _info.Loaded;

    /// <summary>Why the last change failed, or else why the profile can't be read. Empty when neither did.</summary>
    public string Error => ShownError;

    private string ShownError => _actionError.Length > 0 ? _actionError : _info.Error;

    /// <summary>The profile's fields. There is no key in the result.</summary>
    public Dictionary<string, object?> Read() => new()
    {
        ["id"] = _info.Id,
        ["name"] = _info.Name,
        ["ssid"] = _info.Ssid,
        ["autoConnect"] = _info.AutoConnect,
        ["security"] = _info.Security.ToString(),
        ["hasPassword"] = _info.HasPassword,
        ["loaded"] = _info.Loaded,
    };

    /// <summary>Removes every key element and writes the profile back. The key text is not read.</summary>
    public void ClearPassword() => _ = ClearPasswordAsync();

    /// <summary>Deletes this saved profile by its profile name.</summary>
    public void Forget() => _ = ForgetAsync();

    internal Task SetAutoConnectAsync(bool value) =>
        WriteAsync(() => _network.Device.Backend.SetProfileAutoConnectAsync(_network.Device.NativeId, _info.Name, value));

    internal Task ClearPasswordAsync() =>
        WriteAsync(() => _network.Device.Backend.ClearProfilePasswordAsync(_network.Device.NativeId, _info.Name));

    internal Task ForgetAsync() => WriteAsync(() => Task.Run(() =>
    {
        var error = _network.Device.Backend.Forget(_network.Device.NativeId, _info.Name);
        return error == 0 ? null : new Win32Exception((int)error).Message;
    }));

    internal void Update(WifiProfileInfo info)
    {
        var old = _info;
        var oldError = ShownError;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Ssid != info.Ssid, nameof(Ssid));
        Changed(old.AutoConnect != info.AutoConnect, nameof(AutoConnect));
        Changed(old.Security != info.Security, nameof(Security));
        Changed(old.HasPassword != info.HasPassword, nameof(HasPassword));
        Changed(old.Loaded != info.Loaded, nameof(Loaded));
        Changed(oldError != ShownError, nameof(Error));
    }

    internal void Remove() => Removed = true;

    private void SetActionError(string error)
    {
        var old = ShownError;
        _actionError = error;
        Changed(old != ShownError, nameof(Error));
    }

    private async Task WriteAsync(Func<Task<string?>> action)
    {
        if (_writing || Removed)
            return;
        _writing = true;
        SetActionError("");
        var error = "";
        try
        {
            error = await action().ConfigureAwait(false) ?? "";
        }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        var message = error;
        _network.Device.Loop.Post(() =>
        {
            _writing = false;
            if (Removed)
                return;
            SetActionError(message);
            if (message.Length > 0)
                OperationFailed?.Invoke(message);
        });
    }
}

/// <summary>One network adapter. Each one is a <see cref="WifiDevice"/> or a <see cref="WiredDevice"/>.</summary>
public abstract class NetworkDevice : ObservableObject
{
    private NetworkDeviceInfo _info;

    internal NetworkDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
    {
        _info = info;
        Loop = loop;
        Backend = backend;
    }

    internal IEventLoop Loop { get; }

    internal INetworkBackend Backend { get; }

    internal Guid NativeId => _info.Id;

    internal NetworkDeviceInfo Info => _info;

    internal bool Removed { get; private set; }

    /// <summary>The adapter's interface GUID. <see cref="NetworkProfile.AdapterId"/> matches it.</summary>
    public string Id => _info.Id.ToString();

    /// <summary>Whether the adapter is Wi-Fi or wired.</summary>
    public DeviceType Type => _info.Type;

    /// <summary>The adapter's connection name, such as <c>Wi-Fi</c> or <c>Ethernet 2</c>.</summary>
    public string Name => _info.Name;

    /// <summary>For Wi-Fi, the networks of the last scan and the connected network, connected first, then strongest first.</summary>
    public ObservableCollection<Network> Networks { get; } = [];

    /// <summary>The MAC address, as six uppercase hex pairs joined by colons.</summary>
    public string Address => _info.Address;

    /// <summary>Whether the adapter is connected.</summary>
    public bool Connected => _info.State == ConnectionState.Connected;

    /// <summary>
    /// The adapter's connection state. A wired adapter is only <see cref="ConnectionState.Connected"/> or
    /// <see cref="ConnectionState.Disconnected"/>.
    /// </summary>
    public ConnectionState State => _info.State;

    internal virtual void Update(NetworkDeviceInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Address != info.Address, nameof(Address));
        Changed(old.State != info.State, nameof(State));
        Changed((old.State == ConnectionState.Connected) != Connected, nameof(Connected));
        Networks.Sync(info.Networks, n => n.Name, i => i.Name,
            i => Type == DeviceType.Wifi ? new WifiNetwork(this, i) : new Network(this, i), (n, i) => n.Update(i), n => n.Remove());
    }

    internal void Remove()
    {
        Removed = true;
        foreach (var network in Networks)
            network.Remove();
    }
}

/// <summary>A Wi-Fi adapter.</summary>
public sealed class WifiDevice : NetworkDevice
{
    private bool _scanning;
    private string _error = "";

    internal WifiDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
        : base(info, loop, backend)
    {
    }

    /// <summary>A scan failed.</summary>
    public event Action<string>? OperationFailed;

    /// <summary>The adapter's 802.11 mode.</summary>
    public WifiDeviceMode Mode => Info.Mode;

    /// <summary>Whether a scan is running. <see cref="Scan"/> does nothing until it ends.</summary>
    public bool Scanning => _scanning;

    /// <summary>Why the last scan failed. Empty while a scan runs and after one succeeds.</summary>
    public string Error => _error;

    /// <summary>Scans once and updates <see cref="NetworkDevice.Networks"/>. Needs <see cref="NetworkingService.RequestAccess"/> first.</summary>
    public void Scan() => _ = ScanAsync();

    internal async Task ScanAsync()
    {
        if (_scanning || Removed)
            return;
        Set(ref _scanning, true, nameof(Scanning));
        Set(ref _error, "", nameof(Error));
        var error = "";
        try
        {
            await Backend.ScanAsync(NativeId).ConfigureAwait(false);
        }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        Loop.Post(() =>
        {
            if (Removed)
                return;
            Set(ref _scanning, false, nameof(Scanning));
            Set(ref _error, error, nameof(Error));
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
    }

    internal override void Update(NetworkDeviceInfo info)
    {
        var old = Info;
        base.Update(info);
        Changed(old.Mode != info.Mode, nameof(Mode));
    }
}

/// <summary>An Ethernet adapter. Windows manages wired connections itself.</summary>
public sealed class WiredDevice : NetworkDevice
{
    internal WiredDevice(NetworkDeviceInfo info, IEventLoop loop, INetworkBackend backend)
        : base(info, loop, backend)
    {
    }

    /// <summary>The link speed in Mbps. 0 without a link.</summary>
    public int LinkSpeed => (int)Math.Min(int.MaxValue, Info.LinkSpeed);

    /// <summary>Whether the adapter has a link, such as a plugged-in cable.</summary>
    public bool HasLink => Info.HasLink;

    internal override void Update(NetworkDeviceInfo info)
    {
        var old = Info;
        base.Update(info);
        Changed(old.LinkSpeed != info.LinkSpeed, nameof(LinkSpeed));
        Changed(old.HasLink != info.HasLink, nameof(HasLink));
    }
}

/// <summary>
/// The PC's adapters, connection profiles, radios and connectivity. Reading it never asks for permission and never
/// changes a connection or a radio.
/// </summary>
public sealed class NetworkingService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly INetworkBackend _backend;
    private readonly IDisposable _subscription;
    private NetworkSnapshot _snapshot = NetworkSnapshot.Initial;
    private string _error = "";
    private string _radioError = "";
    private bool _requesting;
    private bool _settingWifi;
    private bool _disposed;

    internal NetworkingService(IEventLoop loop, INetworkBackend? backend = null)
    {
        _loop = loop;
        _backend = backend ?? NetworkBackend.Instance;
        Apply(_backend.Current);
        _subscription = _backend.Subscribe(s => _loop.Post(() => Apply(s)));
    }

    /// <summary>An access request or a radio change failed.</summary>
    public event Action<string>? OperationFailed;

    /// <summary>The PC's Wi-Fi and Ethernet adapters. Virtual adapters, VPNs and Bluetooth are left out.</summary>
    public ObservableCollection<NetworkDevice> Devices { get; } = [];

    /// <summary>The Windows connection profiles that are on an adapter now.</summary>
    public ObservableCollection<NetworkProfile> Profiles { get; } = [];

    /// <summary>
    /// Whether the first read has finished. False again while the last read failed, and <see cref="Error"/> says why.
    /// </summary>
    public bool Ready => _snapshot.Ready;

    /// <summary>
    /// Whether at least one Wi-Fi radio is on. Setting it asks Windows to turn every Wi-Fi radio on or off, and the
    /// value stays what Windows last reported until a later read shows the radios changed.
    /// </summary>
    public bool WifiEnabled
    {
        get => _snapshot.WifiEnabled;
        set => _ = SetWifiEnabledAsync(value);
    }

    /// <summary>The state of the Wi-Fi radios, taken together.</summary>
    public WifiRadioState WifiState => _snapshot.WifiState;

    /// <summary>Whether at least one Wi-Fi radio is on or off, not disabled, so software can turn it on.</summary>
    public bool WifiHardwareEnabled => _snapshot.WifiHardwareEnabled;

    /// <summary>
    /// Whether Windows lets Winshell scan and connect Wi-Fi. <see cref="NetworkAccessStatus.Unspecified"/> until
    /// <see cref="RequestAccess"/> has an answer.
    /// </summary>
    public NetworkAccessStatus AccessStatus => _snapshot.Access;

    /// <summary>Airplane mode, read from the registry on a best-effort basis.</summary>
    public AirplaneModeState AirplaneMode => _snapshot.AirplaneMode;

    /// <summary>
    /// What the connection Windows uses for the internet can reach. <see cref="NetworkConnectivity.None"/> without
    /// such a connection.
    /// </summary>
    public NetworkConnectivity Connectivity => _snapshot.Connectivity;

    /// <summary>Why the last read, access request or Wi-Fi radio change failed. Empty when none did.</summary>
    public string Error => _error;

    /// <summary>
    /// Whether an access request is waiting for Windows. <see cref="RequestAccess"/> does nothing until it ends.
    /// </summary>
    public bool RequestingAccess => _requesting;

    /// <summary>Reads the adapters, profiles, radios and connectivity again soon.</summary>
    public void Refresh() => _backend.Refresh();

    /// <summary>Asks Windows for Wi-Fi access, which may show consent UI. Call it from a user action.</summary>
    public void RequestAccess() => _ = RequestAccessAsync();

    internal async Task RequestAccessAsync()
    {
        if (_requesting || _disposed)
            return;
        Set(ref _requesting, true, nameof(RequestingAccess));
        var error = "";
        try
        {
            await _backend.RequestAccessAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (NetworkBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        _loop.Post(() =>
        {
            if (_disposed)
                return;
            _radioError = "";
            Set(ref _requesting, false, nameof(RequestingAccess));
            Apply(_backend.Current);
            Set(ref _error, error, nameof(Error));
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
    }

    // Returns before Windows answers. The property changes only when a snapshot reports the radios did.
    internal async Task SetWifiEnabledAsync(bool enabled)
    {
        if (_settingWifi || _disposed || _snapshot.WifiEnabled == enabled)
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
                if (!shouldReport || _disposed)
                    return;
                _radioError = message;
                Apply(_backend.Current);
                if (message.Length > 0)
                    OperationFailed?.Invoke(message);
            });
        }
    }

    private void Apply(NetworkSnapshot snapshot)
    {
        if (_disposed)
            return;
        // A radio refusal stays until the radios change or a read fails. Reaching the new state clears it.
        if (snapshot.Error.Length > 0 || _snapshot.WifiEnabled != snapshot.WifiEnabled)
            _radioError = "";
        var old = _snapshot;
        _snapshot = snapshot;
        Set(ref _error, snapshot.Error.Length > 0 ? snapshot.Error : _radioError, nameof(Error));
        Changed(old.Ready != snapshot.Ready, nameof(Ready));
        Changed(old.WifiEnabled != snapshot.WifiEnabled, nameof(WifiEnabled));
        Changed(old.WifiHardwareEnabled != snapshot.WifiHardwareEnabled, nameof(WifiHardwareEnabled));
        Changed(old.WifiState != snapshot.WifiState, nameof(WifiState));
        Changed(old.Connectivity != snapshot.Connectivity, nameof(Connectivity));
        Changed(old.Access != snapshot.Access, nameof(AccessStatus));
        Changed(old.AirplaneMode != snapshot.AirplaneMode, nameof(AirplaneMode));
        Devices.Sync(snapshot.Devices, d => d.NativeId, i => i.Id, CreateDevice, (d, i) => d.Update(i), d => d.Remove());
        Profiles.Sync(snapshot.Profiles ?? [], p => p.Id, i => i.Id, i => new NetworkProfile(i), (p, i) => p.Update(i));
    }

    private NetworkDevice CreateDevice(NetworkDeviceInfo info) =>
        info.Type == DeviceType.Wifi ? new WifiDevice(info, _loop, _backend) : new WiredDevice(info, _loop, _backend);

    /// <summary>Stops following network changes. Its devices, networks and profiles then ignore actions.</summary>
    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
        foreach (var device in Devices)
            device.Remove();
    }
}
