using System.Collections.ObjectModel;
using Windows.Devices.Radios;

namespace Winshell.Network;

/// <summary>
/// The PC's adapters, connection profiles, radios and connectivity. Reading it never asks for permission and never
/// changes a connection or a radio.
/// </summary>
public sealed class NetworkingService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly INetworkBackend _backend;
    private readonly IRadioAccess _radioAccess;
    private readonly IDisposable _subscription;
    private readonly DeviceAction _access;
    private readonly DeviceAction _wifiSwitch;
    private NetworkSnapshot _snapshot = NetworkSnapshot.Initial;
    private string _error = "";
    private string _radioError = "";
    private bool _disposed;

    internal NetworkingService(IEventLoop loop, INetworkBackend? backend = null, IRadioAccess? radioAccess = null)
    {
        _loop = loop;
        _backend = backend ?? NetworkBackend.Instance;
        _radioAccess = radioAccess ?? WindowsRadioAccess.Instance;
        _access = new DeviceAction(loop.Post);
        _wifiSwitch = new DeviceAction(loop.Post);
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
    /// Whether at least one Wi-Fi radio is on. Setting it asks Windows to turn on or off every Wi-Fi radio the last read
    /// found, and the value stays what Windows last reported until a later read shows the radios changed.
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
    public bool RequestingAccess => _access.Running;

    /// <summary>Reads the adapters, profiles, radios and connectivity again soon.</summary>
    public void Refresh()
    {
        _backend.Refresh();
    }

    /// <summary>Asks Windows for Wi-Fi access, which may show consent UI. Call it from a user action.</summary>
    public void RequestAccess()
    {
        _ = RequestAccessAsync();
    }

    internal Task RequestAccessAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        return _access.RunAsync(() => Changed(nameof(RequestingAccess)), async () =>
        {
            await _backend.RequestAccessAsync().ConfigureAwait(false);
            return null;
        }, error =>
        {
            if (_disposed)
                return;
            _radioError = "";
            Changed(nameof(RequestingAccess));
            Apply(_backend.Current);
            Set(ref _error, error, nameof(Error));
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
    }

    // Returns before Windows answers. The property changes only when a snapshot reports the radios did.
    internal Task SetWifiEnabledAsync(bool enabled)
    {
        if (_disposed || _snapshot.WifiEnabled == enabled)
            return Task.CompletedTask;
        var radios = _snapshot.Wifi.Radios;
        return _wifiSwitch.RunAsync(static () => { }, async () =>
        {
            var error = await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, radios, enabled, _radioAccess)
                .ConfigureAwait(false);
            if (error is null)
                await _backend.RefreshNowAsync().ConfigureAwait(false);
            return error;
        }, error =>
        {
            if (_disposed)
                return;
            _radioError = error;
            Apply(_backend.Current);
            if (error.Length > 0)
                OperationFailed?.Invoke(error);
        });
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
        Devices.Sync(snapshot.Devices, d => d.NativeId, i => i.Id, CreateDevice, (d, i) => d.Update(i),
            d => d.Remove());
        Profiles.Sync(snapshot.Profiles ?? [], p => p.Id, i => i.Id, i => new NetworkProfile(i), (p, i) => p.Update(i));
    }

    private NetworkDevice CreateDevice(NetworkDeviceInfo info)
    {
        return info.Type == DeviceType.Wifi
            ? new WifiDevice(info, _loop, _backend)
            : new WiredDevice(info, _loop, _backend);
    }

    /// <summary>Stops following network changes. Its devices, networks and profiles then ignore actions.</summary>
    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
        foreach (var device in Devices)
            device.Remove();
    }
}
