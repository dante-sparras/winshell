using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Bluetooth;

namespace Winshell.Services.Tests;

public sealed class BluetoothServiceTests
{
    private static readonly Guid Container = Guid.Parse("71886561-73c1-4d67-a4a0-5077c90859d0");

    private static BluetoothAdapterInfo Adapter(string id = "WindowsAdapter") => new(id, "Bluetooth", "00:11:22:33:44:55", BluetoothAdapterState.On);

    private static BluetoothDeviceInfo Device(string id = "WindowsEndpoint", bool paired = true, bool present = true, double? battery = .7) =>
        new("stable-key", id, "00:11:22:33:44:66", "Headphones", true, paired, present, !paired, battery, Container, paired ? [id] : []);

    private static BluetoothSnapshot Snapshot(params BluetoothDeviceInfo[] devices) => new(true, Adapter(), devices);

    private static BluetoothSnapshot Visibility(bool discoverable, bool pairable) =>
        Snapshot() with { Adapter = Adapter() with { Discoverable = discoverable, Pairable = pairable } };

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    private static List<string> Failures(Action<Action<string>> subscribe)
    {
        var messages = new List<string>();
        subscribe(messages.Add);
        return messages;
    }

    private static BluetoothService Monitored(IBluetoothBackend backend, ManualEventLoop loop, Log? log = null) =>
        new(loop, log ?? new Log(), backend);

    [Fact]
    public async Task A_read_changes_only_the_properties_that_differ()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var changes = Watch(session.Device);

        await session.ShowAsync(Snapshot(Device() with { Icon = "audio-headset" }));

        Assert.Equal([nameof(BluetoothDevice.Icon)], changes);
        Assert.Equal("audio-headset", session.Device.Icon);
    }

    [Fact]
    public async Task A_device_that_stays_is_the_same_object_and_moves_between_the_lists()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var service = session.Service;
        var adapter = service.DefaultAdapter;
        var device = session.Device;
        Assert.Same(device, Assert.Single(service.PairedDevices));
        Assert.Empty(service.NearbyDevices);

        await session.ShowAsync(Snapshot(Device("ChangedEndpoint", paired: false, battery: null) with { Name = "Nearby", Connected = false }));

        Assert.Same(device, session.Device);
        Assert.Same(adapter, service.DefaultAdapter);
        Assert.Same(device.Adapter, service.DefaultAdapter);
        Assert.Equal("ChangedEndpoint", device.Id);
        Assert.Equal("Nearby", device.Name);
        Assert.False(device.Paired);
        Assert.True(device.CanPair);
        Assert.Equal(BluetoothDeviceState.Disconnected, device.State);
        Assert.False(device.BatteryAvailable);
        Assert.Equal(0, device.Battery);
        Assert.Empty(service.PairedDevices);
        Assert.Same(device, Assert.Single(service.NearbyDevices));

        await session.ShowAsync(Snapshot());
        Assert.Empty(service.Devices);
        Assert.Empty(service.NearbyDevices);
    }

    [Fact]
    public async Task A_new_adapter_replaces_every_object_and_no_adapter_clears_every_list()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var service = session.Service;
        var adapter = service.DefaultAdapter;
        var device = session.Device;

        await session.ShowAsync(Snapshot(Device()) with { Adapter = Adapter("Replacement") });

        Assert.NotSame(adapter, service.DefaultAdapter);
        Assert.NotSame(device, session.Device);
        Assert.Same(service.DefaultAdapter, session.Device.Adapter);
        Assert.Same(service.DefaultAdapter, Assert.Single(service.Adapters));

        await session.ShowAsync(new(true, null, [Device()]));

        Assert.Null(service.DefaultAdapter);
        Assert.Empty(service.Adapters);
        Assert.Empty(service.Devices);
        Assert.Empty(service.PairedDevices);
        Assert.Empty(service.NearbyDevices);
    }

    [Fact]
    public async Task An_unchanged_read_leaves_the_lists_alone()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var updates = 0;
        void Count(object? sender, NotifyCollectionChangedEventArgs e) => updates++;
        session.Service.Devices.CollectionChanged += Count;
        session.Service.Adapters.CollectionChanged += Count;
        session.Service.PairedDevices.CollectionChanged += Count;
        session.Service.NearbyDevices.CollectionChanged += Count;

        await session.ShowAsync(Snapshot(Device()) with { Discovering = true });
        await session.ShowAsync(Snapshot(Device()) with { Discovering = true });

        Assert.Equal(0, updates);
    }

    [Fact]
    public async Task Actions_use_the_backend_and_never_change_state_ahead_of_a_read()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var backend = session.Backend;
        var device = session.Device;
        Assert.Empty(backend.Calls);

        await device.SetConnectionAsync(false);
        session.Loop.Process();
        Assert.True(device.Connected);
        Assert.Equal((Container, false), Assert.Single(backend.Connections));
        await device.SetConnectionAsync(true);
        session.Loop.Process();
        Assert.Equal((Container, true), backend.Connections[1]);
        Assert.Equal("", device.Error);

        await session.ShowAsync(Snapshot(Device(paired: false)));
        await device.PairAsync();
        session.Loop.Process();
        Assert.False(device.Paired);
        Assert.Equal("WindowsEndpoint", Assert.Single(backend.Paired));

        await session.ShowAsync(Snapshot(Device() with { PairedIds = ["classic", "le"] }));
        await device.UnpairAsync();
        session.Loop.Process();
        Assert.Equal(["classic", "le"], backend.Unpaired);
    }

    [Fact]
    public async Task A_failed_action_sets_the_error_and_raises_the_event()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var backend = session.Backend;
        var device = session.Device;
        var failed = Failures(h => device.OperationFailed += h);

        backend.AudioAccepted = false;
        await device.SetConnectionAsync(true);
        session.Loop.Process();
        Assert.Contains("Other device types", device.Error, StringComparison.Ordinal);
        Assert.Equal([device.Error], failed);

        backend.Failure = new UnauthorizedAccessException("Access denied");
        await device.UnpairAsync();
        session.Loop.Process();
        Assert.Equal("Access denied", device.Error);
        Assert.False(device.Busy);

        await session.ShowAsync(Snapshot(Device(paired: false) with { Container = null }));
        var calls = backend.Connections.Count;
        await device.SetConnectionAsync(true);
        session.Loop.Process();
        Assert.Equal(calls, backend.Connections.Count);
        Assert.Contains("paired Bluetooth audio device", device.Error, StringComparison.Ordinal);

        backend.Failure = null;
        backend.PairError = "RejectedByHandler";
        await device.PairAsync();
        session.Loop.Process();
        Assert.Equal("RejectedByHandler", device.Error);
    }

    [Fact]
    public async Task A_pairing_ignores_another_action_and_ends_when_cancelled_or_when_the_service_goes()
    {
        var session = await Session.OpenAsync(Snapshot(Device(paired: false)));
        session.Backend.WaitForPairCancellation = true;
        var device = session.Device;

        var request = device.PairAsync();
        await device.PairAsync();
        Assert.True(device.Pairing);
        Assert.True(device.Busy);
        Assert.Single(session.Backend.Paired);

        device.CancelPairing();
        await request;
        session.Loop.Process();
        Assert.Equal("Cancelled", device.Error);
        Assert.False(device.Pairing);

        request = device.PairAsync();
        session.Dispose();
        await request.WaitAsync(TestContext.Current.CancellationToken);
        session.Loop.Process();
        Assert.True(session.Backend.Disposed);
    }

    [Fact]
    public async Task A_device_that_leaves_stops_its_pairing()
    {
        using var session = await Session.OpenAsync(Snapshot(Device(paired: false)));
        session.Backend.WaitForPairCancellation = true;
        var device = session.Device;
        var failed = Failures(h => device.OperationFailed += h);

        var request = device.PairAsync();
        await session.ShowAsync(Snapshot());
        await request.WaitAsync(TestContext.Current.CancellationToken);
        session.Loop.Process();

        Assert.Empty(failed);
        Assert.Single(session.Backend.Paired);
    }

    [Fact]
    public async Task Discovery_is_explicit_and_needs_an_adapter_that_is_on()
    {
        using var session = await Session.OpenAsync(Snapshot());
        var service = session.Service;
        var failed = Failures(h => service.OperationFailed += h);

        service.StartDiscovery();
        session.Loop.Process();
        Assert.True(service.Discovering);
        service.StopDiscovery();
        session.Loop.Process();
        Assert.False(service.Discovering);
        Assert.Equal(["start", "stop"], session.Backend.Calls);

        await session.ShowAsync(Snapshot() with { Adapter = Adapter() with { State = BluetoothAdapterState.Off } });
        service.StartDiscovery();
        Assert.Equal(2, session.Backend.Calls.Count);
        Assert.Contains("must be on", service.Error, StringComparison.Ordinal);
        Assert.Equal([service.Error], failed);
    }

    [Fact]
    public async Task A_read_reaches_the_loop_recovers_from_failure_and_a_stale_one_is_dropped()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var service = session.Service;
        var backend = session.Backend;

        backend.ReadFailure = new UnauthorizedAccessException("Unavailable");
        await service.RefreshAsync();
        Assert.True(service.Ready);
        session.Loop.Process();
        Assert.False(service.Ready);
        Assert.Equal("Unavailable", service.Error);
        Assert.Empty(service.Devices);

        backend.ReadFailure = null;
        await session.ShowAsync(Snapshot(Device()));
        Assert.Equal("", service.Error);

        var completion = new TaskCompletionSource<BluetoothSnapshot>();
        backend.ReadTask = completion.Task;
        var read = service.RefreshAsync();
        service.Invalidate();
        completion.SetResult(Snapshot(Device() with { Name = "Stale" }));
        await read;
        session.Loop.Process();
        Assert.Equal("Headphones", session.Device.Name);
    }

    [Fact]
    public async Task Setting_the_radio_follows_a_read_and_reports_a_refusal()
    {
        using var session = await Session.OpenAsync(Snapshot(Device()));
        var service = session.Service;
        var backend = session.Backend;
        var adapter = service.DefaultAdapter!;
        var failed = Failures(h => adapter.OperationFailed += h);

        await service.SetAdapterEnabledAsync(adapter, true);
        Assert.Empty(backend.Calls);

        await service.SetAdapterEnabledAsync(adapter, false);
        Assert.True(adapter.Enabled);
        session.Loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal(BluetoothAdapterState.Off, adapter.State);
        Assert.Empty(failed);
        Assert.Equal("radio-off", Assert.Single(backend.Calls));

        backend.RadioError = RadioSwitch.NoAdapterRadio;
        await service.SetAdapterEnabledAsync(adapter, true);
        session.Loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal(RadioSwitch.NoAdapterRadio, adapter.Error);
        Assert.Equal([RadioSwitch.NoAdapterRadio], failed);

        backend.RadioError = null;
        backend.RadioException = new UnauthorizedAccessException("Access revoked");
        await service.SetAdapterEnabledAsync(adapter, true);
        session.Loop.Process();
        Assert.False(adapter.Enabled);
        Assert.Equal("Access revoked", adapter.Error);

        await session.ShowAsync(Snapshot(Device()));
        Assert.True(adapter.Enabled);
        Assert.Equal("", adapter.Error);
    }

    [Fact]
    public async Task A_radio_request_in_flight_ignores_another_and_a_replaced_adapter_hears_nothing()
    {
        using var session = await Session.OpenAsync(new(true, Adapter() with { State = BluetoothAdapterState.Off }, []));
        var service = session.Service;
        var adapter = service.DefaultAdapter!;
        var gate = new TaskCompletionSource<string?>();
        session.Backend.RadioGate = gate;

        var first = service.SetAdapterEnabledAsync(adapter, true);
        await service.SetAdapterEnabledAsync(adapter, true);
        Assert.Equal("radio-on", Assert.Single(session.Backend.Calls));

        await session.ShowAsync(new(true, Adapter("Replacement") with { State = BluetoothAdapterState.Off }, []));
        gate.SetResult("refused");
        await first;
        session.Loop.Process();
        Assert.Equal("", adapter.Error);
        Assert.Equal("", service.DefaultAdapter!.Error);
    }

    [Fact]
    public async Task Discoverable_and_pairable_follow_a_read_and_the_order_Windows_needs()
    {
        using var session = await Session.OpenAsync(Visibility(discoverable: false, pairable: true));
        var service = session.Service;
        var backend = session.Backend;
        var adapter = service.DefaultAdapter!;

        await service.SetDiscoverableAsync(adapter, false);
        Assert.Empty(backend.Calls);

        await service.SetDiscoverableAsync(adapter, true);
        Assert.False(adapter.Discoverable);
        Assert.Equal("discoverable-on", Assert.Single(backend.Calls));
        session.Loop.Process();
        Assert.True(adapter.Discoverable);
        Assert.True(adapter.Pairable);

        await session.ShowAsync(Visibility(discoverable: false, pairable: false));
        backend.Calls.Clear();
        await service.SetDiscoverableAsync(adapter, true);
        Assert.Equal(["pairable-on", "discoverable-on"], backend.Calls);
        session.Loop.Process();
        Assert.True(adapter.Discoverable);
        Assert.True(adapter.Pairable);

        backend.Calls.Clear();
        await service.SetPairableAsync(adapter, false);
        Assert.Equal(["discoverable-off", "pairable-off"], backend.Calls);
        Assert.True(adapter.Discoverable);
        session.Loop.Process();
        Assert.False(adapter.Discoverable);
        Assert.False(adapter.Pairable);
    }

    [Fact]
    public async Task A_visibility_failure_is_reported_and_does_not_skip_ahead_of_windows()
    {
        using var session = await Session.OpenAsync(Visibility(discoverable: false, pairable: false));
        var service = session.Service;
        var backend = session.Backend;
        var adapter = service.DefaultAdapter!;
        var failed = Failures(h => adapter.OperationFailed += h);

        backend.PairableError = "incoming refused";
        await service.SetDiscoverableAsync(adapter, true);
        session.Loop.Process();
        Assert.Equal("pairable-on", Assert.Single(backend.Calls));
        Assert.False(adapter.Pairable);
        Assert.Equal("incoming refused", adapter.Error);
        Assert.Equal(["incoming refused"], failed);

        backend.Calls.Clear();
        backend.PairableError = null;
        backend.DiscoverableError = "discovery refused";
        await service.SetDiscoverableAsync(adapter, true);
        session.Loop.Process();
        Assert.Equal(["pairable-on", "discoverable-on"], backend.Calls);
        Assert.True(adapter.Pairable);
        Assert.False(adapter.Discoverable);
        Assert.Equal("discovery refused", adapter.Error);

        await session.ShowAsync(Visibility(discoverable: true, pairable: true));
        Assert.Equal("", adapter.Error);
    }

    [Fact]
    public async Task A_visibility_request_in_flight_ignores_another()
    {
        using var session = await Session.OpenAsync(Visibility(discoverable: false, pairable: true));
        var service = session.Service;
        var adapter = service.DefaultAdapter!;
        var gate = new TaskCompletionSource<string?>();
        session.Backend.VisibilityGate = gate;

        var first = service.SetDiscoverableAsync(adapter, true);
        await service.SetPairableAsync(adapter, false);
        Assert.Equal("discoverable-on", Assert.Single(session.Backend.Calls));

        gate.SetResult(null);
        await first;
        session.Loop.Process();
        Assert.True(adapter.Discoverable);
    }

    [Fact]
    public async Task The_service_starts_one_monitor_and_closes_its_backend()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        var service = Monitored(backend, loop);
        var monitor = service.MonitorTask!;
        await backend.FirstRead.Task.WaitAsync(TestContext.Current.CancellationToken);

        service.StartMonitoring();
        Assert.Same(monitor, service.MonitorTask);

        service.Dispose();
        service.Dispose();
        await monitor.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(backend.Disposed);
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public async Task Monitoring_can_stop_and_start_again()
    {
        var backend = new FakeBackend { Snapshot = Snapshot(Device()) };
        var loop = new ManualEventLoop();
        using var service = Monitored(backend, loop);
        await backend.FirstRead.Task.WaitAsync(TestContext.Current.CancellationToken);
        var first = service.MonitorTask!;

        service.StopMonitoring();
        await first.WaitAsync(TestContext.Current.CancellationToken);
        var reads = backend.Reads;
        service.StartMonitoring();

        await Wait.UntilAsync(() => backend.Reads != reads, "the monitor to read again");
        Assert.False(service.MonitorTask!.IsCompleted);
    }

    [Fact]
    public async Task A_throwing_read_is_logged_and_does_not_stop_the_monitor()
    {
        var backend = new FlakyBackend();
        var loop = new ManualEventLoop();
        var messages = new ConcurrentQueue<string>();
        var log = new Log { Sink = (_, category, message) => messages.Enqueue($"{category}: {message}") };
        var service = Monitored(backend, loop, log);
        await backend.First.Task.WaitAsync(TestContext.Current.CancellationToken);

        await service.RefreshAsync();
        // The monitor logs the failure after its read ends, and a read counts before its result is posted.
        await Wait.UntilAsync(() =>
        {
            loop.Process();
            return service.Ready && messages.Contains("bluetooth: radio broke");
        }, "the failure and a good read");

        Assert.False(service.MonitorTask!.IsCompleted);
        service.Dispose();
        await service.MonitorTask.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(backend.Disposed);
        Assert.True(service.MonitorTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_refresh_still_reads_after_the_monitor_stops()
    {
        var backend = new TokenBackend();
        using var service = Monitored(backend, new ManualEventLoop());
        await backend.First.Task.WaitAsync(TestContext.Current.CancellationToken);
        service.StopMonitoring();
        var before = backend.Finished;

        await Wait.UntilAsync(() => backend.Finished != before, "a refresh to finish", service.RefreshAsync);
    }

    [Fact]
    public async Task A_refresh_during_a_read_reads_again_when_it_ends()
    {
        var backend = new GatedBackend();
        using var service = new BluetoothService(new ManualEventLoop(), new Log(), backend, monitor: false);
        var first = service.RefreshAsync();
        await backend.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await service.RefreshAsync();
        Assert.Equal(1, backend.Reads);
        // Resumes the read on this thread, so the refresh finishes without a pool thread.
        backend.Gate.SetResult();
        await first.WaitAsync(TestContext.Current.CancellationToken);

        await Wait.UntilAsync(() => backend.Reads >= 2, "the queued read");
        Assert.Equal(2, backend.Reads);
    }

    [Fact]
    public async Task Disposing_does_not_wait_for_a_read_and_the_backend_closes_after_it()
    {
        var backend = new StubbornBackend();
        var service = Monitored(backend, new ManualEventLoop());
        await backend.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        service.Dispose();

        Assert.False(backend.Disposed);
        backend.Finish.TrySetResult(Snapshot());
        await service.MonitorTask!.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(backend.Disposed);
        Assert.False(backend.DisposedDuringRead);
    }

    [Fact]
    public async Task A_manual_refresh_also_finishes_before_the_backend_closes()
    {
        var backend = new StubbornBackend();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(loop, new Log(), backend, monitor: false);
        var read = service.RefreshAsync();
        await backend.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        service.Dispose();
        Assert.False(backend.Disposed);
        backend.Finish.TrySetResult(Snapshot());
        await read.WaitAsync(TestContext.Current.CancellationToken);
        loop.Process();

        Assert.True(backend.Disposed);
        Assert.False(backend.DisposedDuringRead);
        Assert.False(service.Ready);
    }

    private sealed class Session : IDisposable
    {
        private Session()
        {
            Service = new BluetoothService(Loop, new Log(), Backend, monitor: false);
        }

        public FakeBackend Backend { get; } = new();

        public ManualEventLoop Loop { get; } = new();

        public BluetoothService Service { get; }

        public BluetoothDevice Device => Assert.Single(Service.Devices);

        public static async Task<Session> OpenAsync(BluetoothSnapshot snapshot)
        {
            var session = new Session();
            await session.ShowAsync(snapshot);
            return session;
        }

        public async Task ShowAsync(BluetoothSnapshot snapshot)
        {
            Backend.Snapshot = snapshot;
            await Service.RefreshAsync();
            Loop.Process();
        }

        public void Dispose() => Service.Dispose();
    }

    /// <summary>A backend that does nothing but read, for the tests of the read loop.</summary>
    private abstract class ReadBackend : IBluetoothBackend
    {
        public bool Disposed { get; private set; }

        public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);

        public abstract Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation);

        public void StartDiscovery()
        {
        }

        public void StopDiscovery()
        {
        }

        public Task<string?> PairAsync(string id, CancellationToken cancellation) => Task.FromResult<string?>(null);

        public Task<string?> UnpairAsync(string id) => Task.FromResult<string?>(null);

        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => Task.FromResult(true);

        public virtual void Dispose() => Disposed = true;
    }

    private sealed class FlakyBackend : ReadBackend
    {
        private int _reads;

        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                First.TrySetResult();
                return Task.FromException<BluetoothSnapshot>(new InvalidCastException("radio broke"));
            }
            return Task.FromResult(Snapshot());
        }
    }

    private sealed class TokenBackend : ReadBackend
    {
        private int _finished;

        public int Finished => Volatile.Read(ref _finished);

        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            First.TrySetResult();
            cancellation.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _finished);
            return Task.FromResult(Snapshot());
        }
    }

    private sealed class GatedBackend : ReadBackend
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Completing the gate resumes the read on this thread. Queuing that resume on the thread pool
        // left the refresh unfinished when the pool was busy.
        public TaskCompletionSource Gate { get; } = new();

        public override async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            Started.TrySetResult();
            await Gate.Task.WaitAsync(cancellation).ConfigureAwait(false);
            return Snapshot();
        }
    }

    /// <summary>A Windows call that does not honor cancellation.</summary>
    private sealed class StubbornBackend : ReadBackend
    {
        private volatile bool _reading;

        public bool DisposedDuringRead { get; private set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<BluetoothSnapshot> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            _reading = true;
            Started.TrySetResult();
            var snapshot = await Finish.Task.ConfigureAwait(false);
            _reading = false;
            return snapshot;
        }

        public override void Dispose()
        {
            DisposedDuringRead |= _reading;
            base.Dispose();
        }
    }

    private sealed class FakeBackend : IBluetoothBackend
    {
        private int _reads;

        public BluetoothSnapshot Snapshot { get; set; } = Snapshot();

        public Task<BluetoothSnapshot>? ReadTask { get; set; }

        public Exception? Failure { get; set; }

        public Exception? ReadFailure { get; set; }

        public string? PairError { get; set; }

        public bool AudioAccepted { get; set; } = true;

        public bool WaitForPairCancellation { get; set; }

        public List<string> Calls { get; } = [];

        public List<(Guid, bool)> Connections { get; } = [];

        public List<string> Paired { get; } = [];

        public List<string> Unpaired { get; } = [];

        public int Reads => Volatile.Read(ref _reads);

        public bool Disposed { get; private set; }

        public string? RadioError { get; set; }

        public Exception? RadioException { get; set; }

        public TaskCompletionSource<string?>? RadioGate { get; set; }

        public string? DiscoverableError { get; set; }

        public string? PairableError { get; set; }

        public TaskCompletionSource<string?>? VisibilityGate { get; set; }

        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BluetoothSnapshot> ReadAsync()
        {
            Interlocked.Increment(ref _reads);
            FirstRead.TrySetResult();
            return ReadFailure is { } e ? Task.FromException<BluetoothSnapshot>(e) : ReadTask ?? Task.FromResult(Snapshot);
        }

        public void StartDiscovery()
        {
            Calls.Add("start");
            Snapshot = Snapshot with { Discovering = true };
        }

        public void StopDiscovery()
        {
            Calls.Add("stop");
            Snapshot = Snapshot with { Discovering = false };
        }

        public async Task<string?> PairAsync(string id, CancellationToken cancellation)
        {
            Calls.Add("pair");
            Paired.Add(id);
            if (WaitForPairCancellation)
                await new TaskCompletionSource().Task.WaitAsync(cancellation);
            if (Failure is { } e)
                throw e;
            return PairError;
        }

        public Task<string?> UnpairAsync(string id)
        {
            Calls.Add("unpair");
            Unpaired.Add(id);
            return Failure is { } e ? Task.FromException<string?>(e) : Task.FromResult<string?>(null);
        }

        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect)
        {
            Calls.Add(connect ? "connect" : "disconnect");
            Connections.Add((container, connect));
            return Failure is { } e ? Task.FromException<bool>(e) : Task.FromResult(AudioAccepted);
        }

        public Task<string?> SetRadioEnabledAsync(bool enabled)
        {
            Calls.Add(enabled ? "radio-on" : "radio-off");
            if (RadioException is { } exception)
                return Task.FromException<string?>(exception);
            if (RadioGate is { } gate)
                return FinishRadio(gate.Task, enabled);
            return Task.FromResult(ApplyRadio(enabled, RadioError));
        }

        private async Task<string?> FinishRadio(Task<string?> pending, bool enabled) => ApplyRadio(enabled, await pending.ConfigureAwait(false));

        private string? ApplyRadio(bool enabled, string? error)
        {
            if (error is null && Snapshot.Adapter is { } adapter)
                Snapshot = Snapshot with { Adapter = adapter with { State = enabled ? BluetoothAdapterState.On : BluetoothAdapterState.Off } };
            return error;
        }

        public Task<string?> SetDiscoverableAsync(bool discoverable) =>
            Visibility(discoverable ? "discoverable-on" : "discoverable-off", () =>
            {
                if (DiscoverableError is { } error)
                    return error;
                if (Snapshot.Adapter is { } adapter)
                    Snapshot = Snapshot with { Adapter = adapter with { Discoverable = discoverable } };
                return null;
            });

        public Task<string?> SetPairableAsync(bool pairable) =>
            Visibility(pairable ? "pairable-on" : "pairable-off", () =>
            {
                if (PairableError is { } error)
                    return error;
                if (Snapshot.Adapter is { } adapter)
                    Snapshot = Snapshot with { Adapter = adapter with { Pairable = pairable } };
                return null;
            });

        private Task<string?> Visibility(string call, Func<string?> apply)
        {
            Calls.Add(call);
            if (VisibilityGate is { } gate)
                return FinishVisibility(gate.Task, apply);
            return Task.FromResult(apply());
        }

        private static async Task<string?> FinishVisibility(Task<string?> pending, Func<string?> apply)
        {
            var error = await pending.ConfigureAwait(false);
            return error ?? apply();
        }

        public void Dispose() => Disposed = true;
    }
}
