using System.Globalization;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Winshell.Platform.Windows.Services.Network;
using Winshell.Runtime;
using Winshell.Runtime.Shell;
using Xunit;

namespace Winshell.Platform.Windows.Tests;

public sealed class NetworkTests(ITestOutputHelper output)
{
    private static readonly Guid AdapterId = Guid.Parse("0b0f2811-87b5-48d6-9d7a-7a26ae12ff33");
    private static NetworkInfo Wifi(string name = "Example", double strength = .6) =>
        new(name, ConnectionState.Disconnected, true, strength, WifiSecurityType.Wpa2Psk);
    private static NetworkDeviceInfo Adapter(params NetworkInfo[] networks) =>
        new(AdapterId, DeviceType.Wifi, "Wi-Fi", "00:11:22:33:44:55", ConnectionState.Disconnected, false, 0, networks);
    private static NetworkSnapshot Snapshot(params NetworkInfo[] networks) =>
        new(true, [Adapter(networks)], NetworkConnectivity.InternetAccess, true, true,
            [new("profile", "Home", AdapterId, NetworkConnectivity.InternetAccess, NetworkCost.Fixed, false, false, .8)],
            NetworkAccessStatus.Allowed, AirplaneModeState.Off);

    [Fact]
    public void Snapshots_preserve_identity_update_models_and_destroy_removed_objects()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        service.Apply(Snapshot(Wifi()));
        var adapter = Assert.IsType<WifiDevice>(Assert.Single(service.Devices.Rows));
        var network = Assert.IsType<WifiNetwork>(Assert.Single(adapter.Networks.Rows));
        var profile = Assert.IsType<NetworkProfile>(Assert.Single(service.Profiles.Rows));
        Assert.True(profile.Metered);
        Assert.Equal(AdapterId.ToString(), profile.AdapterId);
        service.Apply(Snapshot(Wifi(strength: .9)));
        Assert.Same(adapter, Assert.Single(service.Devices.Rows));
        Assert.Same(network, Assert.Single(adapter.Networks.Rows));
        Assert.Same(profile, Assert.Single(service.Profiles.Rows));
        Assert.Equal(.9, network.SignalStrength);
        service.Apply(Snapshot());
        Assert.True(network.IsDestroyed);
        service.Apply(NetworkSnapshot.Initial);
        Assert.True(adapter.IsDestroyed);
        Assert.True(profile.IsDestroyed);
        Assert.Empty(service.Devices.Rows);
        Assert.Empty(service.Profiles.Rows);
        service.Destroy();
        Assert.True(backend.Unsubscribed);
    }

    [Fact]
    public void Updates_use_the_engine_loop_and_cannot_resurrect_a_destroyed_service()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        backend.Publish(Snapshot(Wifi()));
        Assert.Empty(service.Devices.Rows);
        loop.Process();
        Assert.Single(service.Devices.Rows);
        backend.Publish(Snapshot(Wifi("Too late")));
        service.Destroy();
        loop.Process();
        Assert.All(service.Devices.Rows, d => Assert.True(((NetworkDevice)d!).IsDestroyed));
    }

    [Fact]
    public async Task Construction_and_snapshot_reads_never_perform_actions_or_request_permission()
    {
        var backend = new FakeBackend();
        using var service = new ServiceLifetime(new NetworkingService(backend, new ManualEventLoop()));
        service.Value.Apply(Snapshot(Wifi()));
        Assert.Empty(backend.Calls);
        var snapshot = await NetworkBackend.Instance.ReadForTestAsync();
        Assert.True(Enum.IsDefined(snapshot.Connectivity));
        Assert.Equal(NetworkAccessStatus.Unspecified, snapshot.Access);
        output.WriteLine($"Native read: ready={snapshot.Ready}, adapters={snapshot.Devices.Count}, profiles={snapshot.Profiles?.Count ?? 0}, connectivity={snapshot.Connectivity}, airplaneMode={snapshot.AirplaneMode}, error={snapshot.Error}");
    }

    [Fact]
    public async Task Actions_only_call_the_injected_backend_and_report_failure()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        service.Apply(Snapshot(Wifi()));
        var device = Assert.IsType<WifiDevice>(Assert.Single(service.Devices.Rows));
        var network = Assert.IsType<WifiNetwork>(Assert.Single(device.Networks.Rows));
        await network.ConnectAsync("secret");
        loop.Process();
        Assert.Equal((AdapterId, "Example", "secret"), backend.Connection);
        Assert.Equal("", network.Error);
        network.Disconnect(); loop.Process();
        network.Forget(); loop.Process();
        await device.ScanAsync(); loop.Process();
        Assert.Equal(new[] { "connect", "disconnect", "forget", "scan" }, backend.Calls);
        backend.Failure = new UnauthorizedAccessException("Access revoked");
        await network.ConnectAsync(null); loop.Process();
        Assert.False(network.Busy);
        Assert.Equal("Access revoked", network.Error);
        await device.ScanAsync(); loop.Process();
        Assert.False(device.Scanning);
        Assert.Equal("Access revoked", device.Error);
        backend.Failure = null;
        backend.ConnectResult = ConnectionFailReason.InvalidPassword;
        await network.ConnectAsync("wrong"); loop.Process();
        Assert.Equal("InvalidPassword", network.Error);
        service.Destroy();
    }

    [Fact]
    public async Task Overlapping_actions_are_coalesced_and_late_completions_are_ignored()
    {
        var pending = new TaskCompletionSource<ConnectionFailReason?>();
        var backend = new FakeBackend { ConnectTask = pending.Task };
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        service.Apply(Snapshot(Wifi()));
        var device = Assert.IsType<WifiDevice>(Assert.Single(service.Devices.Rows));
        var network = Assert.IsType<WifiNetwork>(Assert.Single(device.Networks.Rows));
        var first = network.ConnectAsync(null);
        await network.ConnectAsync(null);
        Assert.True(network.Busy);
        Assert.Single(backend.Calls);
        network.Destroy();
        pending.SetResult(ConnectionFailReason.Timeout);
        await first; loop.Process();
        Assert.Equal("", network.Error);
        service.Destroy();
    }

    [Fact]
    public async Task Permission_is_explicit_coalesced_and_reports_denial_and_exceptions()
    {
        var completion = new TaskCompletionSource();
        var backend = new FakeBackend { AccessTask = completion.Task };
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        var request = service.RequestAccessAsync();
        await service.RequestAccessAsync();
        Assert.Single(backend.Calls);
        Assert.True(service.RequestingAccess);
        completion.SetResult();
        await request; loop.Process();
        Assert.False(service.RequestingAccess);
        backend.Failure = new UnauthorizedAccessException("Denied");
        await service.RequestAccessAsync(); loop.Process();
        Assert.Equal("Denied", service.Error);
        service.Destroy();
    }

    [Theory]
    [InlineData(NetworkCost.Unknown, false, false, false)]
    [InlineData(NetworkCost.Unrestricted, false, false, false)]
    [InlineData(NetworkCost.Fixed, false, false, true)]
    [InlineData(NetworkCost.Variable, false, false, true)]
    [InlineData(NetworkCost.Unrestricted, true, false, true)]
    [InlineData(NetworkCost.Unrestricted, false, true, true)]
    public void Metered_uses_cost_roaming_and_data_limit(NetworkCost cost, bool roaming, bool overLimit, bool expected)
    {
        var profile = new NetworkProfile(new("profile", "Home", AdapterId, NetworkConnectivity.LocalAccess, cost, roaming, overLimit, null));
        Assert.Equal(expected, profile.Metered);
        Assert.False(profile.SignalAvailable);
        Assert.Equal(0, profile.SignalStrength);
    }

    [Theory]
    [InlineData(NetworkConnectivityLevel.None, NetworkConnectivity.None)]
    [InlineData(NetworkConnectivityLevel.LocalAccess, NetworkConnectivity.LocalAccess)]
    [InlineData(NetworkConnectivityLevel.ConstrainedInternetAccess, NetworkConnectivity.CaptivePortal)]
    [InlineData(NetworkConnectivityLevel.InternetAccess, NetworkConnectivity.InternetAccess)]
    public void Connectivity_mapping(NetworkConnectivityLevel value, NetworkConnectivity expected) =>
        Assert.Equal(expected, NetworkBackend.MapConnectivity(value));

    [Theory]
    [InlineData(WiFiAccessStatus.Allowed, NetworkAccessStatus.Allowed)]
    [InlineData(WiFiAccessStatus.DeniedByUser, NetworkAccessStatus.DeniedByUser)]
    [InlineData(WiFiAccessStatus.DeniedBySystem, NetworkAccessStatus.DeniedBySystem)]
    [InlineData(WiFiAccessStatus.Unspecified, NetworkAccessStatus.Unspecified)]
    public void Access_mapping(WiFiAccessStatus value, NetworkAccessStatus expected) => Assert.Equal(expected, NetworkBackend.MapAccess(value));

    [Theory]
    [InlineData(null, AirplaneModeState.Unknown)]
    [InlineData(0, AirplaneModeState.Off)]
    [InlineData(1, AirplaneModeState.On)]
    [InlineData(42, AirplaneModeState.Unknown)]
    public void Airplane_mode_is_not_inferred_from_radios(object? value, AirplaneModeState expected) =>
        Assert.Equal(expected, NetworkBackend.MapAirplaneMode(value));

    [Fact]
    public void Wifi_radio_is_writable_and_other_state_stays_read_only()
    {
        var service = new NetworkingService(new FakeBackend(), new ManualEventLoop());
        service.Apply(Snapshot(Wifi()));
        var device = Assert.IsType<WifiDevice>(Assert.Single(service.Devices.Rows));
        var network = Assert.IsType<WifiNetwork>(Assert.Single(device.Networks.Rows));
        // wifiEnabled used to be read-only. The other state stays read-only.
        Assert.True(service.Meta.Properties["wifiEnabled"].CanWrite);
        foreach (var name in new[] { "airplaneMode", "accessStatus", "connectivity" })
            Assert.False(service.Meta.Properties[name].CanWrite);
        Assert.False(device.HasMember("nmManaged"));
        Assert.False(device.HasMember("autoconnect"));
        Assert.False(network.HasMember("nmSettings"));
        Assert.False(network.HasMember("connectWithSettings"));
        Assert.False(network.HasMember("connectWithPsk"));
        service.Destroy();
    }

    [Theory]
    [InlineData(0x00000004u, WifiDeviceMode.Station)]
    [InlineData(0x00000008u, WifiDeviceMode.AccessPoint)]
    [InlineData(0x00000000u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000001u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000002u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000010u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000020u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000040u, WifiDeviceMode.Unknown)]
    [InlineData(0x40000000u, WifiDeviceMode.Unknown)]
    [InlineData(0x80000000u, WifiDeviceMode.Unknown)]
    public void Operation_mode_maps_extensible_station_and_access_point(uint operationMode, WifiDeviceMode expected)
    {
        var mode = NetworkBackend.MapDeviceMode(operationMode);
        Assert.Equal(expected, mode);
        Assert.NotEqual(WifiDeviceMode.AdHoc, mode);
        Assert.NotEqual(WifiDeviceMode.Mesh, mode);
    }

    [Fact]
    public void A_failed_operation_mode_query_is_unknown()
    {
        var mode = NetworkBackend.MapDeviceMode(null);
        Assert.Equal(WifiDeviceMode.Unknown, mode);
        Assert.NotEqual(WifiDeviceMode.AdHoc, mode);
        Assert.NotEqual(WifiDeviceMode.Mesh, mode);
    }

    [Fact]
    public void Wifi_mode_is_read_only_and_signals_only_when_it_changes()
    {
        var service = new NetworkingService(new FakeBackend(), new ManualEventLoop());
        var wiredId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var wired = new NetworkDeviceInfo(wiredId, DeviceType.Wired, "Ethernet", "aa:bb:cc:dd:ee:ff",
            ConnectionState.Connected, true, 1000, []);
        var station = Adapter() with { Mode = WifiDeviceMode.Station };
        NetworkSnapshot Shot(NetworkDeviceInfo wifi) =>
            new(true, [wifi, wired], NetworkConnectivity.InternetAccess, true, true);
        service.Apply(Shot(station));
        var device = Assert.IsType<WifiDevice>(Assert.Single(service.Devices.Rows, d => d is WifiDevice));
        var ethernet = Assert.IsType<WiredDevice>(Assert.Single(service.Devices.Rows, d => d is WiredDevice));
        Assert.Equal(WifiDeviceMode.Station, device.Mode);
        Assert.False(device.Meta.Properties["mode"].CanWrite);
        Assert.False(ethernet.HasMember("mode"));
        var changes = 0;
        device.Connect("modeChanged", _ => changes++);
        service.Apply(Shot(station with { Name = "Wi-Fi 2" }));
        Assert.Equal(0, changes);
        Assert.Equal("Wi-Fi 2", device.Name);
        Assert.Equal(WifiDeviceMode.Station, device.Mode);
        service.Apply(Shot(station with { Mode = WifiDeviceMode.AccessPoint }));
        Assert.Equal(1, changes);
        Assert.Equal(WifiDeviceMode.AccessPoint, device.Mode);
        service.Apply(Shot(station with { Mode = WifiDeviceMode.AccessPoint, State = ConnectionState.Connected }));
        Assert.Equal(1, changes);
        service.Destroy();
    }

    [Fact]
    public void WifiDeviceMode_toString_returns_the_value_name()
    {
        var registry = StandardModules.Create();
        NetworkingModule.Register(registry);
        Assert.True(registry.TryGetModule("Winshell.Network", out var module));
        var toString = module.Types["WifiDeviceMode"].EnumToString;
        Assert.NotNull(toString);
        Assert.Equal("Unknown", toString((int)WifiDeviceMode.Unknown));
        Assert.Equal("AccessPoint", toString((int)WifiDeviceMode.AccessPoint));
        Assert.Equal("Mesh", toString((int)WifiDeviceMode.Mesh));
        Assert.Equal("Station", toString((int)WifiDeviceMode.Station));
        Assert.Equal("AdHoc", toString((int)WifiDeviceMode.AdHoc));
    }

    [Theory]
    [InlineData(NetworkCostType.Unknown, NetworkCost.Unknown)]
    [InlineData(NetworkCostType.Unrestricted, NetworkCost.Unrestricted)]
    [InlineData(NetworkCostType.Fixed, NetworkCost.Fixed)]
    [InlineData(NetworkCostType.Variable, NetworkCost.Variable)]
    public void Cost_mapping(NetworkCostType value, NetworkCost expected) => Assert.Equal(expected, NetworkBackend.MapCost(value));

    [Theory]
    [InlineData(WiFiConnectionStatus.Success, null)]
    [InlineData(WiFiConnectionStatus.InvalidCredential, ConnectionFailReason.InvalidPassword)]
    [InlineData(WiFiConnectionStatus.Timeout, ConnectionFailReason.Timeout)]
    [InlineData(WiFiConnectionStatus.NetworkNotAvailable, ConnectionFailReason.NetworkUnavailable)]
    [InlineData(WiFiConnectionStatus.UnsupportedAuthenticationProtocol, ConnectionFailReason.UnsupportedAuthentication)]
    [InlineData(WiFiConnectionStatus.UnspecifiedFailure, ConnectionFailReason.Unknown)]
    public void Native_connection_result_mapping(WiFiConnectionStatus value, ConnectionFailReason? expected) =>
        Assert.Equal(expected, NetworkBackend.MapConnectionResult(value));

    [Fact]
    public void Qml_bindings_follow_profiles_signal_and_removal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-network-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var loop = new ManualEventLoop();
        var service = new NetworkingService(new FakeBackend(), loop);
        try
        {
            var path = Path.Combine(directory, "shell.qml");
            File.WriteAllText(path, """
                import QtQml
                import Winshell.Network
                QtObject {
                    property int count: TestNetwork.profiles.values.length
                    property bool metered: count > 0 && TestNetwork.profiles.values[0].metered
                    property real strength: TestNetwork.devices.values.length > 0 && TestNetwork.devices.values[0].networks.values.length > 0
                        ? TestNetwork.devices.values[0].networks.values[0].signalStrength : 0
                    property bool internet: TestNetwork.connectivity === NetworkConnectivity.InternetAccess
                }
                """);
            var registry = StandardModules.Create();
            NetworkingModule.Register(registry);
            registry.Module("Winshell.Network").Singleton(_ => service, "TestNetwork");
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            var root = engine.CreateRoot(path);
            service.Apply(Snapshot(Wifi())); loop.Process();
            Assert.Equal(1, root.ReadProperty("count"));
            Assert.True((bool)root.ReadProperty("metered")!);
            Assert.True((bool)root.ReadProperty("internet")!);
            Assert.Equal(.6, Convert.ToDouble(root.ReadProperty("strength"), CultureInfo.InvariantCulture));
            service.Apply(Snapshot(Wifi(strength: .9))); loop.Process();
            Assert.Equal(.9, Convert.ToDouble(root.ReadProperty("strength"), CultureInfo.InvariantCulture));
            service.Apply(NetworkSnapshot.Initial); loop.Process();
            Assert.Equal(0, root.ReadProperty("count"));
            Assert.False((bool)root.ReadProperty("metered")!);
        }
        finally
        {
            service.Destroy();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Setting_wifi_follows_the_snapshot_and_a_refusal_stays_until_the_radios_change()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        var off = Snapshot() with { WifiEnabled = false, WifiState = WifiRadioState.Off };
        service.Apply(off);
        string? signal = null;
        service.Connect("operationFailed", args => signal = (string)args[0]!);

        await service.SetWifiEnabledAsync(false);
        Assert.Empty(backend.Calls);

        await service.SetWifiEnabledAsync(true);
        Assert.False(service.WifiEnabled);
        loop.Process();
        Assert.True(service.WifiEnabled);
        Assert.Equal("", service.Error);
        Assert.Null(signal);
        Assert.Equal("wifi-on", Assert.Single(backend.Calls));

        backend.Calls.Clear();
        backend.KeepRadioSnapshot = true;
        await service.SetWifiEnabledAsync(false);
        loop.Process();
        Assert.True(service.WifiEnabled);
        Assert.Equal("", service.Error);

        backend.RadioError = "Windows or its policy denied radio control.";
        await service.SetWifiEnabledAsync(false);
        loop.Process();
        Assert.True(service.WifiEnabled);
        Assert.Equal(backend.RadioError, service.Error);
        Assert.Equal(backend.RadioError, signal);

        backend.RadioError = null;
        backend.RadioException = new UnauthorizedAccessException("Access revoked");
        await service.SetWifiEnabledAsync(false);
        loop.Process();
        Assert.True(service.WifiEnabled);
        Assert.Equal("Access revoked", service.Error);

        service.Apply(off);
        Assert.False(service.WifiEnabled);
        Assert.Equal("", service.Error);
        service.Destroy();
    }

    [Fact]
    public async Task A_wifi_request_in_flight_ignores_another()
    {
        var gate = new TaskCompletionSource<string?>();
        var backend = new FakeBackend { RadioGate = gate };
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        service.Apply(Snapshot() with { WifiEnabled = false, WifiState = WifiRadioState.Off });
        var first = service.SetWifiEnabledAsync(true);
        await service.SetWifiEnabledAsync(false);
        Assert.Equal("wifi-on", Assert.Single(backend.Calls));
        service.Destroy();
        gate.SetResult("too late");
        await first;
        loop.Process();
    }

    [Fact]
    public void Qml_can_assign_the_wifi_value_the_radio_already_has()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new NetworkingService(backend, loop);
        var directory = Path.Combine(Path.GetTempPath(), "winshell-radio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            service.Apply(Snapshot() with { WifiEnabled = false, WifiState = WifiRadioState.Off });
            File.WriteAllText(Path.Combine(directory, "shell.qml"), """
                import QtQml
                import Winshell.Network
                QtObject {
                    Component.onCompleted: TestNetwork.wifiEnabled = false
                }
                """);
            var registry = StandardModules.Create();
            NetworkingModule.Register(registry);
            registry.Module("Winshell.Network").Singleton(_ => service, "TestNetwork");
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            Assert.Empty(backend.Calls);
            Assert.False(service.WifiEnabled);
        }
        finally
        {
            service.Destroy();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeBackend : INetworkBackend
    {
        public NetworkSnapshot Current { get; private set; } = NetworkSnapshot.Initial;
        private Action<NetworkSnapshot>? _listener;
        public List<string> Calls { get; } = [];
        public (Guid, string, string?) Connection { get; private set; }
        public Exception? Failure { get; set; }
        public ConnectionFailReason? ConnectResult { get; set; }
        public Task<ConnectionFailReason?>? ConnectTask { get; init; }
        public Task AccessTask { get; init; } = Task.CompletedTask;
        public string? RadioError { get; set; }
        public Exception? RadioException { get; set; }
        public bool KeepRadioSnapshot { get; set; }
        public TaskCompletionSource<string?>? RadioGate { get; init; }
        public bool Unsubscribed { get; private set; }
        public IDisposable Subscribe(Action<NetworkSnapshot> listener) { _listener = listener; return new Subscription(this); }
        public void Publish(NetworkSnapshot snapshot) { Current = snapshot; _listener?.Invoke(snapshot); }
        public void Refresh() => Calls.Add("refresh");
        public Task RequestAccessAsync() { Calls.Add("access"); return Failure is { } e ? Task.FromException(e) : AccessTask; }
        public Task<string?> SetWifiEnabledAsync(bool enabled)
        {
            Calls.Add(enabled ? "wifi-on" : "wifi-off");
            if (RadioGate is { } gate)
                return FinishRadio(gate.Task, enabled);
            if (RadioException is { } exception)
                return Task.FromException<string?>(exception);
            return Task.FromResult(ApplyRadio(enabled, RadioError));
        }
        private async Task<string?> FinishRadio(Task<string?> pending, bool enabled) => ApplyRadio(enabled, await pending.ConfigureAwait(false));
        private string? ApplyRadio(bool enabled, string? error)
        {
            if (error is null && !KeepRadioSnapshot)
                Current = Current with { WifiEnabled = enabled, WifiHardwareEnabled = true, WifiState = enabled ? WifiRadioState.On : WifiRadioState.Off };
            return error;
        }
        public Task ScanAsync(Guid device) { Calls.Add("scan"); Assert.Equal(AdapterId, device); return Failure is { } e ? Task.FromException(e) : Task.CompletedTask; }
        public Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? password)
        {
            Calls.Add("connect"); Connection = (device, ssid, password);
            return Failure is { } e ? Task.FromException<ConnectionFailReason?>(e) : ConnectTask ?? Task.FromResult(ConnectResult);
        }
        public bool Disconnect(Guid device) { Calls.Add("disconnect"); Assert.Equal(AdapterId, device); return true; }
        public uint Forget(Guid device, string profileName) { Calls.Add("forget"); Assert.Equal(AdapterId, device); Assert.Equal("Example", profileName); return 0; }
        public Task<string?> SetProfileAutoConnectAsync(Guid device, string profileName, bool autoConnect)
        {
            Calls.Add("profile-auto");
            return Task.FromResult<string?>(null);
        }
        public Task<string?> ClearProfilePasswordAsync(Guid device, string profileName)
        {
            Calls.Add("profile-clear");
            return Task.FromResult<string?>(null);
        }
        public Task<string?> ConnectProfileAsync(Guid device, string profileName, bool hidden)
        {
            Calls.Add("profile-connect");
            return Task.FromResult<string?>(null);
        }
        private sealed class Subscription(FakeBackend owner) : IDisposable
        {
            public void Dispose() { owner.Unsubscribed = true; owner._listener = null; }
        }
    }
    private sealed class ServiceLifetime(NetworkingService value) : IDisposable
    {
        public NetworkingService Value => value;
        public void Dispose() => value.Destroy();
    }
}
