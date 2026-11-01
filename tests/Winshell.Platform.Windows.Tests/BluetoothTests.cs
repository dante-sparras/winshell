using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Winshell.Platform.Windows;
using Winshell.Platform.Windows.Services;
using Winshell.Platform.Windows.Services.Bluetooth;
using Winshell.Runtime;
using Winshell.Runtime.Shell;
using Xunit;
using BtDevice = Winshell.Platform.Windows.Services.Bluetooth.BluetoothDevice;

namespace Winshell.Platform.Windows.Tests;

public sealed class BluetoothTests(ITestOutputHelper output)
{
    private static readonly Guid Container = Guid.Parse("71886561-73c1-4d67-a4a0-5077c90859d0");
    private static BluetoothAdapterInfo Adapter(string id = "WindowsAdapter") => new(id, "Bluetooth", "00:11:22:33:44:55", BluetoothAdapterState.On);
    private static BluetoothDeviceInfo Device(string id = "WindowsEndpoint", bool paired = true, bool present = true, double? battery = .7) =>
        new("stable-key", id, "00:11:22:33:44:66", "Headphones", true, paired, present, !paired, battery, Container, paired ? [id] : []);
    private static BluetoothSnapshot Snapshot(params BluetoothDeviceInfo[] devices) => new(true, Adapter(), devices);
    private static BtDevice Item(BluetoothService service) => Assert.IsType<BtDevice>(Assert.Single(service.Devices.Rows));

    [Fact]
    public void Models_keep_device_identity_and_refresh_native_fields()
    {
        var service = new BluetoothService(new FakeBackend(), new ManualEventLoop());
        service.Apply(Snapshot(Device()));
        var adapter = service.DefaultAdapter;
        var item = Item(service);
        Assert.Same(item, Assert.Single(service.PairedDevices.Rows));
        Assert.Empty(service.NearbyDevices.Rows);
        service.Apply(Snapshot(Device("ChangedEndpoint", paired: false, battery: null) with { Name = "Nearby", Connected = false }));
        Assert.Same(item, Item(service));
        Assert.Same(adapter, service.DefaultAdapter);
        Assert.Equal("ChangedEndpoint", item.Id);
        Assert.Equal("Nearby", item.Name);
        Assert.False(item.Paired);
        Assert.True(item.Present);
        Assert.True(item.CanPair);
        Assert.False(item.Connected);
        Assert.Equal(BluetoothDeviceState.Disconnected, item.State);
        Assert.False(item.BatteryAvailable);
        Assert.Equal(0, item.Battery);
        Assert.Empty(service.PairedDevices.Rows);
        Assert.Same(item, Assert.Single(service.NearbyDevices.Rows));
        service.Apply(Snapshot());
        Assert.True(item.IsDestroyed);
        Assert.Empty(service.Devices.Rows);
        service.Destroy();
    }

    [Fact]
    public void Adapter_replacement_and_removal_destroy_old_objects_and_clear_all_models()
    {
        var backend = new FakeBackend();
        var service = new BluetoothService(backend, new ManualEventLoop());
        service.Apply(Snapshot(Device()));
        var adapter = service.DefaultAdapter!;
        var device = Item(service);
        service.Apply(Snapshot(Device()) with { Adapter = Adapter("Replacement") });
        Assert.True(adapter.IsDestroyed);
        Assert.True(device.IsDestroyed);
        Assert.NotSame(device, Item(service));
        Assert.Same(service.DefaultAdapter, Item(service).Adapter);
        adapter = service.DefaultAdapter!;
        device = Item(service);
        service.Apply(new(true, null, [Device()]));
        Assert.Null(service.DefaultAdapter);
        Assert.True(adapter.IsDestroyed);
        Assert.True(device.IsDestroyed);
        Assert.Empty(service.Adapters.Rows);
        Assert.Empty(service.Devices.Rows);
        Assert.Empty(service.PairedDevices.Rows);
        Assert.Empty(service.NearbyDevices.Rows);
        service.Destroy();
        Assert.True(backend.Disposed);
    }

    [Fact]
    public void Dual_mode_endpoints_merge_without_losing_pairing_connection_or_battery_identity()
    {
        var classic = new BluetoothEndpointInfo("classic", "aa:bb:cc:dd:ee:ff", "Headphones", false, true, false, false, Container);
        var le = new BluetoothEndpointInfo("le", "aa:bb:cc:dd:ee:ff", "", true, false, true, true, Container);
        var merged = Assert.Single(BluetoothBackend.MergeEndpoints([classic, le, le]));
        Assert.Equal("classic", merged.Id);
        Assert.Equal("AA:BB:CC:DD:EE:FF", merged.Address);
        Assert.Equal("Headphones", merged.Name);
        Assert.True(merged.Connected);
        Assert.True(merged.Paired);
        Assert.True(merged.Present);
        Assert.Equal(new[] { "classic" }, merged.PairedIds);
        Assert.Null(merged.Battery);
        var pairable = Assert.Single(BluetoothBackend.MergeEndpoints([classic with { Paired = false }, le]));
        Assert.Equal("le", pairable.Id);
    }

    [Fact]
    public void Missing_addresses_do_not_merge_unrelated_endpoints_and_address_comparison_ignores_case()
    {
        var endpoint = new BluetoothEndpointInfo("one", "", "", false, false, false, false, null);
        Assert.Equal(2, BluetoothBackend.MergeEndpoints([endpoint, endpoint with { Id = "two" }]).Count);
        Assert.Single(BluetoothBackend.MergeEndpoints([endpoint with { Address = "AA:BB" }, endpoint with { Id = "two", Address = "aa:bb" }]));
    }

    [Theory]
    [InlineData(null, BluetoothAdapterState.Unknown)]
    [InlineData(RadioState.Unknown, BluetoothAdapterState.Unknown)]
    [InlineData(RadioState.On, BluetoothAdapterState.On)]
    [InlineData(RadioState.Off, BluetoothAdapterState.Off)]
    [InlineData(RadioState.Disabled, BluetoothAdapterState.Disabled)]
    public void Radio_state_does_not_guess_when_unavailable(RadioState? state, BluetoothAdapterState expected) =>
        Assert.Equal(expected, BluetoothBackend.MapRadioState(state));

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0.0)]
    [InlineData(42, .42)]
    [InlineData(100, 1.0)]
    [InlineData(255, null)]
    [InlineData(-1, null)]
    [InlineData("80", null)]
    public void Battery_rejects_missing_invalid_and_unknown_values(object? value, double? expected) =>
        Assert.Equal(expected, BluetoothBackend.BatteryLevel(value));

    [Fact]
    public void Battery_accepts_the_windows_numeric_property_types()
    {
        Assert.Equal(.5, BluetoothBackend.BatteryLevel((byte)50));
        Assert.Equal(.5, BluetoothBackend.BatteryLevel((ushort)50));
        Assert.Equal(.5, BluetoothBackend.BatteryLevel(50u));
        Assert.Null(BluetoothBackend.BatteryLevel(255u));
    }

    [Theory]
    [InlineData(DevicePairingResultStatus.Paired, null)]
    [InlineData(DevicePairingResultStatus.AlreadyPaired, null)]
    [InlineData(DevicePairingResultStatus.AuthenticationFailure, "AuthenticationFailure")]
    [InlineData(DevicePairingResultStatus.RejectedByHandler, "RejectedByHandler")]
    [InlineData(DevicePairingResultStatus.OperationAlreadyInProgress, "OperationAlreadyInProgress")]
    public void Pairing_result_mapping(DevicePairingResultStatus status, string? error) => Assert.Equal(error, BluetoothBackend.PairingError(status));

    [Theory]
    [InlineData(DeviceUnpairingResultStatus.Unpaired, null)]
    [InlineData(DeviceUnpairingResultStatus.AlreadyUnpaired, null)]
    [InlineData(DeviceUnpairingResultStatus.AccessDenied, "AccessDenied")]
    [InlineData(DeviceUnpairingResultStatus.Failed, "Failed")]
    public void Unpairing_result_mapping(DeviceUnpairingResultStatus status, string? error) => Assert.Equal(error, BluetoothBackend.UnpairingError(status));

    [Fact]
    public async Task Explicit_actions_use_only_the_fake_and_never_optimistically_change_connection_state()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(Snapshot(Device()));
        var item = Item(service);
        Assert.Empty(backend.Calls);
        await item.SetConnectionAsync(false); loop.Process();
        Assert.True(item.Connected);
        Assert.Equal((Container, false), Assert.Single(backend.Connections));
        await item.SetConnectionAsync(true); loop.Process();
        Assert.Equal((Container, true), backend.Connections[1]);
        Assert.Equal("", item.Error);
        backend.Snapshot = Snapshot(Device(paired: false));
        service.Apply(backend.Snapshot);
        var callerThread = Environment.CurrentManagedThreadId;
        await item.PairAsync(); loop.Process();
        Assert.Equal(callerThread, backend.PairThread);
        Assert.False(item.Paired);
        Assert.Equal("WindowsEndpoint", Assert.Single(backend.Paired));
        backend.Snapshot = Snapshot(Device() with { PairedIds = ["classic", "le"] });
        service.Apply(backend.Snapshot);
        await item.UnpairAsync(); loop.Process();
        Assert.Equal(new[] { "classic", "le" }, backend.Unpaired);
        service.Destroy();
    }

    [Fact]
    public async Task Unsupported_connections_and_failed_actions_have_observable_errors()
    {
        var backend = new FakeBackend { AudioAccepted = false, Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(Snapshot(Device()));
        var item = Item(service);
        string? signal = null;
        item.Connect("operationFailed", args => signal = (string)args[0]!);
        await item.SetConnectionAsync(true); loop.Process();
        Assert.Contains("Other device types", item.Error);
        Assert.Equal(item.Error, signal);
        backend.Failure = new UnauthorizedAccessException("Access denied");
        await item.UnpairAsync(); loop.Process();
        Assert.Equal("Access denied", item.Error);
        Assert.False(item.Busy);
        backend.Snapshot = Snapshot(Device(paired: false) with { Container = null });
        service.Apply(backend.Snapshot);
        var calls = backend.Connections.Count;
        await item.SetConnectionAsync(true); loop.Process();
        Assert.Equal(calls, backend.Connections.Count);
        Assert.Contains("paired Bluetooth audio device", item.Error);
        backend.Failure = null;
        backend.PairError = "RejectedByHandler";
        await item.PairAsync(); loop.Process();
        Assert.Equal("RejectedByHandler", item.Error);
        service.Destroy();
    }

    [Fact]
    public async Task Pairing_is_coalesced_and_cancelled_explicitly_or_on_destruction()
    {
        var backend = new FakeBackend { WaitForPairCancellation = true, Snapshot = Snapshot(Device(paired: false)) };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(Snapshot(Device(paired: false)));
        var item = Item(service);
        var request = item.PairAsync();
        await item.PairAsync();
        Assert.True(item.Pairing);
        Assert.True(item.Busy);
        Assert.Single(backend.Paired);
        item.CancelPairing();
        await request; loop.Process();
        Assert.Equal("Cancelled", item.Error);
        Assert.False(item.Pairing);
        request = item.PairAsync();
        service.Destroy();
        await request; loop.Process();
        Assert.True(item.IsDestroyed);
        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task Discovery_is_explicit_and_stops_on_generation_destruction()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(Snapshot());
        Assert.Empty(backend.Calls);
        service.StartDiscovery(); loop.Process();
        Assert.Equal(1, backend.Starts);
        Assert.True(service.Discovering);
        service.StopDiscovery(); loop.Process();
        Assert.Equal(1, backend.Stops);
        Assert.False(service.Discovering);
        service.Apply(Snapshot() with { Adapter = Adapter() with { State = BluetoothAdapterState.Off } });
        service.StartDiscovery();
        Assert.Equal(1, backend.Starts);
        Assert.Contains("must be on", service.Error);
        service.Destroy();
        Assert.True(backend.Disposed);
        await service.RefreshAsync();
        Assert.Equal(2, backend.Reads);
    }

    [Fact]
    public async Task Reads_are_marshaled_to_the_loop_recover_from_failure_and_ignore_stale_completion()
    {
        var backend = new FakeBackend { ReadFailure = new UnauthorizedAccessException("Unavailable") };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(Snapshot(Device()));
        var removed = Item(service);
        await service.RefreshAsync();
        Assert.True(service.Ready);
        loop.Process();
        Assert.False(service.Ready);
        Assert.Equal("Unavailable", service.Error);
        Assert.True(removed.IsDestroyed);
        backend.ReadFailure = null;
        backend.Snapshot = Snapshot(Device());
        await service.RefreshAsync(); loop.Process();
        Assert.Equal("", service.Error);
        var completion = new TaskCompletionSource<BluetoothSnapshot>();
        backend.ReadTask = completion.Task;
        var read = service.RefreshAsync();
        service.Invalidate();
        completion.SetResult(Snapshot(Device() with { Name = "Stale" }));
        await read; loop.Process();
        Assert.Equal("Headphones", Item(service).Name);
        backend.ReadTask = Task.FromResult(Snapshot());
        await service.RefreshAsync();
        service.Destroy(); loop.Process();
        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task Native_singleton_constructor_starts_one_monitor_and_owns_its_backend()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var service = new BluetoothService(engine, backend);
        await backend.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.ComponentComplete();
        service.Destroy();
        loop.Process();
        Assert.Equal(1, backend.Reads);
        Assert.True(backend.Disposed);
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void Adapter_radio_is_writable_and_BlueZ_properties_are_gone()
    {
        var service = new BluetoothService(new FakeBackend(), new ManualEventLoop());
        service.Apply(Snapshot(Device()));
        var adapter = service.DefaultAdapter!;
        var item = Item(service);
        Assert.Equal("WindowsAdapter", adapter.Id);
        // enabled used to be read-only. Device properties stay read-only.
        Assert.True(adapter.Meta.Properties["enabled"].CanWrite);
        Assert.True(adapter.Meta.Properties["discoverable"].CanWrite);
        Assert.True(adapter.Meta.Properties["pairable"].CanWrite);
        Assert.True(adapter.Meta.Properties.ContainsKey("error"));
        Assert.False(item.Meta.Properties["connected"].CanWrite);
        Assert.False(item.Meta.Properties["name"].CanWrite);
        // Windows has no discoverable or pairable timeout. Those members stay absent.
        foreach (var name in new[] { "dbusPath", "adapterId", "pairableTimeout", "discoverableTimeout" }) Assert.False(adapter.HasMember(name));
        foreach (var name in new[] { "dbusPath", "bonded", "trusted", "blocked", "wakeAllowed", "deviceName" }) Assert.False(item.HasMember(name));
        Assert.True(item.HasMember("icon"));
        Assert.False(item.Meta.Properties["icon"].CanWrite);
        service.Destroy();
    }

    [Fact]
    public async Task Native_smoke_read_never_requests_access_discovers_pairs_or_connects()
    {
        using var backend = new BluetoothBackend();
        BluetoothSnapshot snapshot;
        try { snapshot = await backend.ReadAsync(); }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            output.WriteLine($"Native read unavailable: {e.Message}");
            return;
        }
        Assert.True(snapshot.Ready);
        Assert.False(snapshot.Discovering);
        Assert.All(snapshot.Devices, d => Assert.True(d.Paired));
        output.WriteLine($"Native read: adapter={snapshot.Adapter is not null}, state={snapshot.Adapter?.State}, paired={snapshot.Devices.Count}, batteries={snapshot.Devices.Count(d => d.Battery.HasValue)}");
    }

    [Fact]
    public void Qml_bindings_follow_device_battery_pairing_and_removal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-bluetooth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var loop = new ManualEventLoop();
        var service = new BluetoothService(new FakeBackend(), loop);
        try
        {
            var path = Path.Combine(directory, "shell.qml");
            File.WriteAllText(path, """
                import QtQml
                import Winshell.Bluetooth
                QtObject {
                    property int count: TestBluetooth.devices.values.length
                    property string name: count > 0 ? TestBluetooth.devices.values[0].name : ""
                    property real battery: count > 0 ? TestBluetooth.devices.values[0].battery : 0
                    property int paired: TestBluetooth.pairedDevices.values.length
                    property int nearby: TestBluetooth.nearbyDevices.values.length
                    property bool enabled: TestBluetooth.defaultAdapter !== null && TestBluetooth.defaultAdapter.state === BluetoothAdapterState.On
                }
                """);
            var registry = StandardModules.Create();
            BluetoothModule.Register(registry);
            registry.Module("Winshell.Bluetooth").Singleton(_ => service, "TestBluetooth");
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            var root = engine.CreateRoot(path);
            service.Apply(Snapshot(Device())); loop.Process();
            Assert.Equal("Headphones", root.ReadProperty("name"));
            Assert.Equal(.7, Convert.ToDouble(root.ReadProperty("battery")));
            Assert.True((bool)root.ReadProperty("enabled")!);
            service.Apply(Snapshot(Device(paired: false, battery: .2))); loop.Process();
            Assert.Equal(.2, Convert.ToDouble(root.ReadProperty("battery")));
            Assert.Equal(0, root.ReadProperty("paired"));
            Assert.Equal(1, root.ReadProperty("nearby"));
            service.Apply(new(true, null, [])); loop.Process();
            Assert.Equal(0, root.ReadProperty("count"));
            Assert.False((bool)root.ReadProperty("enabled")!);
        }
        finally
        {
            service.Destroy();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Monitoring_can_stop_and_an_unchanged_read_leaves_the_models_alone()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        var quiet = new BluetoothService(backend, loop);
        await quiet.RefreshAsync();
        loop.Process();
        var updates = 0;
        quiet.Devices.ModelChanged += () => updates++;
        quiet.Adapters.ModelChanged += () => updates++;
        quiet.PairedDevices.ModelChanged += () => updates++;
        quiet.NearbyDevices.ModelChanged += () => updates++;
        await quiet.RefreshAsync();
        loop.Process();
        Assert.Equal(0, updates);
        Assert.Equal("Headphones", Item(quiet).Name);
        quiet.Destroy();

        var watched = new FakeBackend { Snapshot = Snapshot(Device()) };
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var service = new BluetoothService(engine, watched);
        await watched.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        loop.Process();
        var reads = watched.Reads;
        service.StopMonitoring();
        service.StartMonitoring();
        var until = DateTime.UtcNow.AddSeconds(2);
        while (watched.Reads == reads && DateTime.UtcNow < until)
            await Task.Delay(10);
        Assert.True(watched.Reads > reads);
        service.Destroy();
        loop.Process();
    }

    [Fact]
    public async Task Setting_the_adapter_follows_a_read_and_reports_a_refusal()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(backend.Snapshot);
        var adapter = service.DefaultAdapter!;
        string? signal = null;
        adapter.Connect("operationFailed", args => signal = (string)args[0]!);

        await service.SetAdapterEnabledAsync(adapter, true);
        Assert.Empty(backend.Calls);
        Assert.True(adapter.Enabled);

        await service.SetAdapterEnabledAsync(adapter, false);
        Assert.True(adapter.Enabled);
        Assert.Equal("", adapter.Error);
        loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal(BluetoothAdapterState.Off, adapter.State);
        Assert.Null(signal);
        Assert.Equal("radio-off", Assert.Single(backend.Calls));

        backend.Calls.Clear();
        backend.RadioError = RadioSwitch.NoAdapterRadio;
        backend.RadioPresent = false;
        await service.SetAdapterEnabledAsync(adapter, true);
        loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal(RadioSwitch.NoAdapterRadio, adapter.Error);
        Assert.Equal(RadioSwitch.NoAdapterRadio, signal);

        signal = null;
        backend.RadioPresent = true;
        backend.RadioError = null;
        backend.KeepSnapshot = true;
        await service.SetAdapterEnabledAsync(adapter, true);
        loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal("", adapter.Error);
        Assert.Null(signal);

        backend.KeepSnapshot = false;
        backend.RadioException = new UnauthorizedAccessException("Access revoked");
        await service.SetAdapterEnabledAsync(adapter, true);
        loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal("Access revoked", adapter.Error);

        service.Apply(Snapshot(Device()));
        Assert.True(service.DefaultAdapter!.Enabled);
        Assert.Equal("", service.DefaultAdapter.Error);
        service.Destroy();
    }

    [Fact]
    public async Task An_adapter_request_in_flight_ignores_another_and_the_same_value_does_not_ask()
    {
        var gate = new TaskCompletionSource<string?>();
        var backend = new FakeBackend { Snapshot = new(true, Adapter() with { State = BluetoothAdapterState.Off }, []), RadioGate = gate };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(backend.Snapshot);
        var adapter = service.DefaultAdapter!;
        await service.SetAdapterEnabledAsync(adapter, false);
        Assert.Empty(backend.Calls);

        var first = service.SetAdapterEnabledAsync(adapter, true);
        await service.SetAdapterEnabledAsync(adapter, false);
        Assert.Equal("radio-on", Assert.Single(backend.Calls));
        adapter.Destroy();
        gate.SetResult(null);
        await first;
        loop.Process();
        Assert.Equal("", adapter.Error);
    }

    [Fact]
    public void Qml_can_assign_the_adapter_value_it_already_has()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        var directory = Path.Combine(Path.GetTempPath(), "winshell-radio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            service.Apply(Snapshot(Device()));
            File.WriteAllText(Path.Combine(directory, "shell.qml"), """
                import QtQml
                import Winshell.Bluetooth
                QtObject {
                    Component.onCompleted: TestBluetooth.defaultAdapter.enabled = true
                }
                """);
            var registry = StandardModules.Create();
            BluetoothModule.Register(registry);
            registry.Module("Winshell.Bluetooth").Singleton(_ => service, "TestBluetooth");
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            Assert.Empty(backend.Calls);
            Assert.True(service.DefaultAdapter!.Enabled);
        }
        finally
        {
            service.Destroy();
            Directory.Delete(directory, recursive: true);
        }
    }

    // Major and minor are the unshifted Windows codes: headphones is 6, not the raw class bits.
    [Theory]
    [InlineData(0x01, 0, null, "computer")]
    [InlineData(0x01, 5, 1 << 6, "computer")]
    [InlineData(0x02, 0x01, null, "phone")]
    [InlineData(0x02, 0x02, null, "phone")]
    [InlineData(0x02, 0x03, null, "phone")]
    [InlineData(0x02, 0x05, null, "phone")]
    [InlineData(0x02, 0x04, null, "modem")]
    [InlineData(0x02, 0x00, 1 << 6, "")]
    [InlineData(0x03, 0, null, "network-wireless")]
    [InlineData(0x03, 7, null, "network-wireless")]
    [InlineData(0x04, 0x01, null, "audio-headset")]
    [InlineData(0x04, 0x02, null, "audio-headset")]
    [InlineData(0x04, 0x06, null, "audio-headphones")]
    [InlineData(0x04, 0x0b, null, "camera-video")]
    [InlineData(0x04, 0x0c, null, "camera-video")]
    [InlineData(0x04, 0x0d, null, "camera-video")]
    [InlineData(0x04, 0x00, null, "audio-card")]
    [InlineData(0x04, 0x03, null, "audio-card")]
    [InlineData(0x04, 0x0e, null, "audio-card")]
    [InlineData(0x04, 0x0f, null, "audio-card")]
    [InlineData(0x04, 0x12, null, "audio-card")]
    [InlineData(0x04, 0x06, 1 << 6, "audio-headphones")]
    [InlineData(0x05, 0x01, null, "input-gaming")]
    [InlineData(0x05, 0x02, null, "input-gaming")]
    [InlineData(0x05, 0x10, null, "input-keyboard")]
    [InlineData(0x05, 0x20, null, "input-mouse")]
    [InlineData(0x05, 0x25, null, "input-tablet")]
    [InlineData(0x05, 0x22, null, "input-mouse")]
    [InlineData(0x05, 0x30, null, "")]
    [InlineData(0x05, 0x03, null, "")]
    [InlineData(0x05, 0x00, null, "")]
    [InlineData(0x06, 0x20, null, "printer")]
    [InlineData(0x06, 0x08, null, "camera-photo")]
    [InlineData(0x06, 0x28, null, "printer")]
    [InlineData(0x06, 0x04, null, "")]
    [InlineData(0x07, 0x01, 1 << 6, "")]
    [InlineData(0x08, 0, null, "")]
    [InlineData(0x09, 0, null, "")]
    [InlineData(0x00, 1, 1 << 6, "")]
    [InlineData(0x1f, 0, null, "")]
    [InlineData(0x00, 0, null, "")]
    [InlineData(0x00, 0, 1 << 6, "phone")]
    [InlineData(32, 0, 1 << 6, "")]
    [InlineData(0x04, 64, 1 << 6, "")]
    [InlineData(null, null, null, "")]
    [InlineData(null, null, 0, "")]
    [InlineData(null, null, 1, "unknown")]
    [InlineData(null, null, 1 << 6, "phone")]
    [InlineData(null, null, 2 << 6, "computer")]
    [InlineData(null, null, 5 << 6, "video-display")]
    [InlineData(null, null, 10 << 6, "multimedia-player")]
    [InlineData(null, null, 11 << 6, "scanner")]
    [InlineData(null, null, (0x0f << 6) | 1, "input-keyboard")]
    [InlineData(null, null, (0x0f << 6) | 2, "input-mouse")]
    [InlineData(null, null, (0x0f << 6) | 3, "input-gaming")]
    [InlineData(null, null, (0x0f << 6) | 4, "input-gaming")]
    [InlineData(null, null, (0x0f << 6) | 5, "input-tablet")]
    [InlineData(null, null, (0x0f << 6) | 8, "scanner")]
    [InlineData(null, null, 0x0f << 6, "")]
    [InlineData(null, null, 3 << 6, "")]
    [InlineData(null, null, 0x2a << 6, "")]
    public void Icon_names_follow_the_BlueZ_class_and_appearance(int? major, int? minor, int? appearance, string expected) =>
        Assert.Equal(expected, BluetoothIcon.Name(Code(major), Code(minor), Code(appearance)));

    [Fact]
    public void Merged_endpoints_prefer_a_classic_class_over_an_appearance()
    {
        var headphones = new BluetoothClassReport(0x04, 0x06, null);
        var phone = new BluetoothClassReport(null, null, 1 << 6);
        Assert.Equal("audio-headphones", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("le", paired: true, phone), ClassEndpoint("classic", paired: false, headphones)])).Icon);

        Assert.Equal("computer", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("headphones", paired: false, headphones),
            ClassEndpoint("paired-computer", paired: true, new BluetoothClassReport(0x01, 0, null))])).Icon);

        Assert.Equal("computer", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("b", paired: true, headphones),
            ClassEndpoint("a", paired: true, new BluetoothClassReport(0x01, 1, null))])).Icon);

        Assert.Equal("phone", Assert.Single(BluetoothBackend.MergeEndpoints([ClassEndpoint("only-le", paired: true, phone)])).Icon);
        Assert.Equal("", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("band", paired: true, new BluetoothClassReport(0x07, 0x01, null)),
            ClassEndpoint("band-le", paired: false, phone)])).Icon);
        Assert.Equal("", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("broken", paired: true, new BluetoothClassReport(null, null, null, ClassUnreadable: true)),
            ClassEndpoint("broken-le", paired: false, phone)])).Icon);
        Assert.Equal("phone", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("zero", paired: true, new BluetoothClassReport(0, 0, null)),
            ClassEndpoint("zero-le", paired: false, phone)])).Icon);
        Assert.Equal("audio-headset", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("zero-first", paired: true, new BluetoothClassReport(0, 0, null)),
            ClassEndpoint("headset", paired: false, new BluetoothClassReport(0x04, 0x01, null))])).Icon);
    }

    [Fact]
    public void A_class_property_of_the_wrong_type_is_empty_and_does_not_throw()
    {
        Assert.Equal("", IconProperties());
        Assert.Equal("video-display", IconProperties((BluetoothIcon.CategoryKey, (ushort)5)));
        Assert.Equal("input-keyboard", IconProperties(
            (BluetoothIcon.CategoryKey, (ushort)0x0f), (BluetoothIcon.SubcategoryKey, (ushort)1)));
        Assert.Equal("phone", IconProperties(
            (BluetoothIcon.AppearanceKey, (ushort)(1 << 6)), (BluetoothIcon.CategoryKey, (ushort)2)));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.AppearanceKey, (ushort)0), (BluetoothIcon.CategoryKey, (ushort)1)));
        Assert.Equal("unknown", IconProperties(
            (BluetoothIcon.CategoryKey, (ushort)0), (BluetoothIcon.SubcategoryKey, (ushort)1)));
        Assert.Equal("", IconProperties((BluetoothIcon.CategoryKey, (ushort)0x400)));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.CategoryKey, (ushort)5), (BluetoothIcon.SubcategoryKey, "no")));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.MajorKey, "4"), (BluetoothIcon.AppearanceKey, (ushort)(1 << 6))));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.MajorKey, (ushort)4), (BluetoothIcon.MinorKey, "6"),
            (BluetoothIcon.AppearanceKey, (ushort)(1 << 6))));
        Assert.Equal("audio-card", IconProperties((BluetoothIcon.MajorKey, (ushort)4)));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.MajorKey, 4u), (BluetoothIcon.AppearanceKey, (ushort)(1 << 6))));
        Assert.Equal("", BluetoothIcon.Name(null, null, (ushort)(1 << 6), classUnreadable: true));
        Assert.Equal("", BluetoothIcon.Name(null, null, (ushort)(1 << 6), appearanceUnreadable: true));
    }

    [Fact]
    public async Task The_device_icon_notifies_when_the_reported_name_changes()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        await service.RefreshAsync();
        loop.Process();
        var item = Item(service);
        Assert.Equal("", item.Icon);
        var changes = 0;
        item.Connect("iconChanged", _ => changes++);
        backend.Snapshot = Snapshot(Device() with { Icon = "audio-headset" });
        await service.RefreshAsync();
        loop.Process();
        Assert.Equal("audio-headset", item.Icon);
        Assert.Equal(1, changes);
        await service.RefreshAsync();
        loop.Process();
        Assert.Equal(1, changes);
        backend.Snapshot = Snapshot(Device());
        await service.RefreshAsync();
        loop.Process();
        Assert.Equal("", item.Icon);
        Assert.Equal(2, changes);
        service.Destroy();
    }

    [Fact]
    public void A_BlueZ_name_resolves_only_when_the_glyph_map_already_has_it()
    {
        var icons = IconProvider.CreateForTests(4);
        Assert.True(icons.HasIcon("audio-headset"));
        Assert.False(icons.HasIcon("camera-video"));
        Assert.False(icons.HasIcon("input-tablet"));
        Assert.False(icons.HasIcon("modem"));
        Assert.False(icons.HasIcon("scanner"));
        Assert.False(icons.HasIcon("unknown"));
    }

    private static ushort? Code(int? value) => value is int code ? (ushort)code : null;

    private static BluetoothEndpointInfo ClassEndpoint(string id, bool paired, BluetoothClassReport report) =>
        new(id, "aa:bb:cc:dd:ee:ff", "Device", false, paired, true, false, Container, report);

    private static string IconProperties(params (string Key, object Value)[] properties) =>
        BluetoothIcon.FromReports([BluetoothIcon.Read(properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal))]);

    [Fact]
    public async Task Discoverable_and_pairable_follow_a_read_and_windows_rules()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        var shown = Snapshot() with { Adapter = Adapter() with { Discoverable = false, Pairable = true } };
        backend.Snapshot = shown;
        service.Apply(shown);
        var adapter = service.DefaultAdapter!;
        string? signal = null;
        adapter.Connect("operationFailed", args => signal = (string)args[0]!);
        Assert.False(adapter.Discoverable);
        Assert.True(adapter.Pairable);

        await service.SetDiscoverableAsync(adapter, false);
        Assert.Empty(backend.Calls);

        await service.SetDiscoverableAsync(adapter, true);
        Assert.False(adapter.Discoverable);
        Assert.Equal("discoverable-on", Assert.Single(backend.Calls));
        loop.Process();
        Assert.True(adapter.Discoverable);
        Assert.True(adapter.Pairable);
        Assert.Equal("", adapter.Error);
        Assert.Null(signal);

        backend.Calls.Clear();
        await service.SetDiscoverableAsync(adapter, false);
        loop.Process();
        Assert.Equal("discoverable-off", Assert.Single(backend.Calls));
        Assert.False(adapter.Discoverable);
        Assert.True(adapter.Pairable);

        backend.Calls.Clear();
        backend.Snapshot = Snapshot() with { Adapter = Adapter() with { Discoverable = false, Pairable = false } };
        service.Apply(backend.Snapshot);
        await service.SetDiscoverableAsync(adapter, true);
        Assert.Equal(["pairable-on", "discoverable-on"], backend.Calls);
        Assert.False(adapter.Discoverable);
        Assert.False(adapter.Pairable);
        loop.Process();
        Assert.True(adapter.Discoverable);
        Assert.True(adapter.Pairable);

        backend.Calls.Clear();
        await service.SetPairableAsync(adapter, false);
        Assert.Equal(["discoverable-off", "pairable-off"], backend.Calls);
        Assert.True(adapter.Discoverable);
        loop.Process();
        Assert.False(adapter.Discoverable);
        Assert.False(adapter.Pairable);

        backend.Calls.Clear();
        await service.SetPairableAsync(adapter, true);
        loop.Process();
        Assert.Equal("pairable-on", Assert.Single(backend.Calls));
        Assert.False(adapter.Discoverable);
        Assert.True(adapter.Pairable);

        backend.Calls.Clear();
        backend.KeepSnapshot = true;
        await service.SetDiscoverableAsync(adapter, true);
        loop.Process();
        Assert.Equal("discoverable-on", Assert.Single(backend.Calls));
        Assert.False(adapter.Discoverable);
        Assert.Equal("", adapter.Error);
        Assert.Null(signal);
        service.Destroy();
    }

    [Fact]
    public async Task A_visibility_failure_is_reported_and_does_not_skip_ahead_of_windows()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        var shown = Snapshot() with { Adapter = Adapter() with { Discoverable = false, Pairable = false } };
        backend.Snapshot = shown;
        service.Apply(shown);
        var adapter = service.DefaultAdapter!;
        string? signal = null;
        adapter.Connect("operationFailed", args => signal = (string)args[0]!);

        backend.PairableError = "incoming refused";
        await service.SetDiscoverableAsync(adapter, true);
        loop.Process();
        Assert.Equal("pairable-on", Assert.Single(backend.Calls));
        Assert.False(adapter.Pairable);
        Assert.False(adapter.Discoverable);
        Assert.Equal("incoming refused", adapter.Error);
        Assert.Equal("incoming refused", signal);

        signal = null;
        backend.Calls.Clear();
        backend.PairableError = null;
        backend.DiscoverableError = "discovery refused";
        await service.SetDiscoverableAsync(adapter, true);
        loop.Process();
        Assert.Equal(["pairable-on", "discoverable-on"], backend.Calls);
        Assert.True(adapter.Pairable);
        Assert.False(adapter.Discoverable);
        Assert.Equal("discovery refused", adapter.Error);
        Assert.Equal("discovery refused", signal);

        service.Apply(Snapshot() with { Adapter = Adapter() with { Discoverable = true, Pairable = true } });
        Assert.True(adapter.Discoverable);
        Assert.Equal("", adapter.Error);

        signal = null;
        backend.Calls.Clear();
        backend.DiscoverableError = null;
        backend.Snapshot = Snapshot() with { Adapter = Adapter() with { Discoverable = false, Pairable = true } };
        service.Apply(backend.Snapshot);
        backend.VisibilityException = new UnauthorizedAccessException("Access revoked");
        await service.SetDiscoverableAsync(adapter, true);
        loop.Process();
        Assert.Equal("discoverable-on", Assert.Single(backend.Calls));
        Assert.False(adapter.Discoverable);
        Assert.Equal("Access revoked", adapter.Error);
        Assert.Equal("Access revoked", signal);
        service.Destroy();
    }

    [Fact]
    public async Task A_visibility_request_in_flight_ignores_another()
    {
        var gate = new TaskCompletionSource<string?>();
        var backend = new FakeBackend { VisibilityGate = gate, Snapshot = Snapshot() with { Adapter = Adapter() with { Pairable = true } } };
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        service.Apply(backend.Snapshot);
        var adapter = service.DefaultAdapter!;
        var first = service.SetDiscoverableAsync(adapter, true);
        await service.SetPairableAsync(adapter, false);
        Assert.Equal("discoverable-on", Assert.Single(backend.Calls));
        adapter.Destroy();
        gate.SetResult(null);
        await first;
        loop.Process();
        Assert.Equal("", adapter.Error);
        service.Destroy();
    }

    [Fact]
    public void Qml_can_assign_the_visibility_the_adapter_already_reports()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        var directory = Path.Combine(Path.GetTempPath(), "winshell-bluetooth-visibility-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            service.Apply(Snapshot());
            File.WriteAllText(Path.Combine(directory, "shell.qml"), """
                import QtQml
                import Winshell.Bluetooth
                QtObject {
                    Component.onCompleted: {
                        TestBluetooth.defaultAdapter.discoverable = false
                        TestBluetooth.defaultAdapter.pairable = false
                    }
                }
                """);
            var registry = StandardModules.Create();
            BluetoothModule.Register(registry);
            registry.Module("Winshell.Bluetooth").Singleton(_ => service, "TestBluetooth");
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            Assert.Empty(backend.Calls);
            Assert.False(service.DefaultAdapter!.Discoverable);
            Assert.False(service.DefaultAdapter.Pairable);
        }
        finally
        {
            service.Destroy();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeBackend : IBluetoothBackend
    {
        public BluetoothSnapshot Snapshot { get; set; } = Snapshot();
        public Task<BluetoothSnapshot>? ReadTask { get; set; }
        public Exception? Failure { get; set; }
        public Exception? ReadFailure { get; set; }
        public string? PairError { get; set; }
        public bool AudioAccepted { get; init; } = true;
        public bool WaitForPairCancellation { get; init; }
        public List<string> Calls { get; } = [];
        public List<(Guid, bool)> Connections { get; } = [];
        public List<string> Paired { get; } = [];
        public int PairThread { get; private set; }
        public List<string> Unpaired { get; } = [];
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public bool RadioPresent { get; set; } = true;
        public string? RadioError { get; set; }
        public Exception? RadioException { get; set; }
        public bool KeepSnapshot { get; set; }
        public TaskCompletionSource<string?>? RadioGate { get; init; }
        public string? DiscoverableError { get; set; }
        public string? PairableError { get; set; }
        public Exception? VisibilityException { get; set; }
        public TaskCompletionSource<string?>? VisibilityGate { get; init; }
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BluetoothSnapshot> ReadAsync()
        {
            Reads++;
            FirstRead.TrySetResult();
            return ReadFailure is { } e ? Task.FromException<BluetoothSnapshot>(e) : ReadTask ?? Task.FromResult(Snapshot);
        }
        public void StartDiscovery() { Calls.Add("start"); Starts++; Snapshot = Snapshot with { Discovering = true }; }
        public void StopDiscovery() { Calls.Add("stop"); Stops++; Snapshot = Snapshot with { Discovering = false }; }
        public async Task<string?> PairAsync(string id, CancellationToken cancellation)
        {
            PairThread = Environment.CurrentManagedThreadId;
            Calls.Add("pair"); Paired.Add(id);
            if (WaitForPairCancellation) await Task.Delay(Timeout.Infinite, cancellation);
            if (Failure is { } e) throw e;
            return PairError;
        }
        public Task<string?> UnpairAsync(string id)
        {
            Calls.Add("unpair"); Unpaired.Add(id);
            return Failure is { } e ? Task.FromException<string?>(e) : Task.FromResult<string?>(null);
        }
        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect)
        {
            Calls.Add(connect ? "connect" : "disconnect"); Connections.Add((container, connect));
            return Failure is { } e ? Task.FromException<bool>(e) : Task.FromResult(AudioAccepted);
        }
        public Task<string?> SetRadioEnabledAsync(bool enabled)
        {
            Calls.Add(enabled ? "radio-on" : "radio-off");
            if (!RadioPresent)
                return Task.FromResult<string?>(RadioSwitch.NoAdapterRadio);
            if (RadioException is { } exception)
                return Task.FromException<string?>(exception);
            if (RadioGate is { } gate)
                return FinishRadio(gate.Task, enabled);
            return Task.FromResult(ApplyRadio(enabled, RadioError));
        }
        private async Task<string?> FinishRadio(Task<string?> pending, bool enabled) => ApplyRadio(enabled, await pending.ConfigureAwait(false));
        private string? ApplyRadio(bool enabled, string? error)
        {
            if (error is null && !KeepSnapshot && Snapshot.Adapter is { } adapter)
                Snapshot = Snapshot with { Adapter = adapter with { State = enabled ? BluetoothAdapterState.On : BluetoothAdapterState.Off } };
            return error;
        }
        public Task<string?> SetDiscoverableAsync(bool discoverable) =>
            Visibility(discoverable ? "discoverable-on" : "discoverable-off", () =>
            {
                if (DiscoverableError is { } error) return error;
                if (!KeepSnapshot && Snapshot.Adapter is { } adapter)
                    Snapshot = Snapshot with { Adapter = adapter with { Discoverable = discoverable } };
                return null;
            });
        public Task<string?> SetPairableAsync(bool pairable) =>
            Visibility(pairable ? "pairable-on" : "pairable-off", () =>
            {
                if (PairableError is { } error) return error;
                if (!KeepSnapshot && Snapshot.Adapter is { } adapter)
                    Snapshot = Snapshot with { Adapter = adapter with { Pairable = pairable } };
                return null;
            });
        private Task<string?> Visibility(string call, Func<string?> apply)
        {
            Calls.Add(call);
            if (VisibilityException is { } exception)
                return Task.FromException<string?>(exception);
            if (VisibilityGate is { } gate)
                return FinishVisibility(gate.Task, apply);
            return Task.FromResult(apply());
        }
        private async Task<string?> FinishVisibility(Task<string?> pending, Func<string?> apply)
        {
            var error = await pending.ConfigureAwait(false);
            return error ?? apply();
        }
        public void Dispose() { Disposed = true; }
    }
}
