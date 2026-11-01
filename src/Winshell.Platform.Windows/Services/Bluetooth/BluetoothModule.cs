using Winshell.Platform.Windows.Services;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Bluetooth;

public enum BluetoothAdapterState { Unknown, Off, On, Disabled }
public enum BluetoothDeviceState { Disconnected, Connected }
public enum BluetoothOperation { None, Connecting, Disconnecting, Pairing, Unpairing }

/// <summary>
/// The default Windows Bluetooth adapter. Setting <see cref="Enabled"/> asks Windows to change its radio.
/// Setting <see cref="Discoverable"/> or <see cref="Pairable"/> asks Windows to change discovery or incoming connections.
/// </summary>
[QmlSignal("operationFailed", "message")]
public sealed class BluetoothAdapter : QObject
{
    private readonly BluetoothService _service;
    private BluetoothAdapterInfo _info;
    private string _error = "";
    internal BluetoothAdapter(BluetoothService service, BluetoothAdapterInfo info)
    {
        _service = service;
        _info = info;
    }
    [Qml] public string Id => _info.Id;
    [Qml] public string Name { get { Track(); return _info.Name; } }
    [Qml] public string Address { get { Track(); return _info.Address; } }
    [Qml] public BluetoothAdapterState State { get { Track(); return _info.State; } }
    [Qml] public bool Enabled { get { Track(); return ReportsOn; } set => _ = _service.SetAdapterEnabledAsync(this, value); }
    [Qml] public bool Discoverable { get { Track(); return _info.Discoverable; } set => _ = _service.SetDiscoverableAsync(this, value); }
    [Qml] public bool Pairable { get { Track(); return _info.Pairable; } set => _ = _service.SetPairableAsync(this, value); }
    [Qml] public string Error { get { Track(); return _error; } }
    internal bool ReportsOn => _info.State == BluetoothAdapterState.On;
    internal void Report(string error)
    {
        SetField(ref _error, error, nameof(Error));
        if (error.Length > 0) Emit("operationFailed", error);
    }
    internal void Update(BluetoothAdapterInfo info)
    {
        var old = _info;
        _info = info;
        if (old.Name != info.Name) NotifyChanged("name");
        if (old.Address != info.Address) NotifyChanged("address");
        if (old.Discoverable != info.Discoverable) NotifyChanged("discoverable");
        if (old.Pairable != info.Pairable) NotifyChanged("pairable");
        if (old.State != info.State)
        {
            NotifyChanged("state");
            NotifyChanged("enabled");
        }
        if (old.State != info.State || old.Discoverable != info.Discoverable || old.Pairable != info.Pairable)
            SetField(ref _error, "", nameof(Error));
    }
}

[QmlSignal("operationFailed", "message")]
public sealed class BluetoothDevice : QObject
{
    private readonly BluetoothService _service;
    private BluetoothDeviceInfo _info;
    private BluetoothOperation _operation;
    private string _error = "";
    private readonly object _pairingLock = new();
    private CancellationTokenSource? _pairing;
    internal BluetoothDevice(BluetoothService service, BluetoothDeviceInfo info, BluetoothAdapter adapter)
    {
        _service = service;
        _info = info;
        Adapter = adapter;
    }
    [Qml] public string Id { get { Track(); return _info.Id; } }
    [Qml] public BluetoothAdapter Adapter { get; }
    [Qml] public string Address { get { Track(); return _info.Address; } }
    [Qml] public string Name { get { Track(); return _info.Name; } }
    /// <summary>A BlueZ device-type icon name. Empty when Windows reports no class, or a class BlueZ does not name.</summary>
    [Qml] public string Icon { get { Track(); return _info.Icon; } }
    [Qml] public bool Paired { get { Track(); return _info.Paired; } }
    [Qml] public bool Present { get { Track(); return _info.Present; } }
    [Qml] public bool CanPair { get { Track(); return !_info.Paired && _info.CanPair; } }
    [Qml] public bool Connected { get { Track(); return _info.Connected; } }
    [Qml] public BluetoothDeviceState State { get { Track(); return _info.Connected ? BluetoothDeviceState.Connected : BluetoothDeviceState.Disconnected; } }
    [Qml] public bool BatteryAvailable { get { Track(); return _info.Battery.HasValue; } }
    [Qml] public double Battery { get { Track(); return _info.Battery ?? 0; } }
    [Qml] public BluetoothOperation Operation { get { Track(); return _operation; } }
    [Qml] public bool Busy { get { Track(); return _operation != BluetoothOperation.None; } }
    [Qml] public bool Pairing { get { Track(); return _operation == BluetoothOperation.Pairing; } }
    [Qml] public string Error { get { Track(); return _error; } }

    internal void Update(BluetoothDeviceInfo info)
    {
        var old = _info;
        _info = info;
        if (old.Id != info.Id) NotifyChanged("id");
        if (old.Address != info.Address) NotifyChanged("address");
        if (old.Name != info.Name) NotifyChanged("name");
        if (!string.Equals(old.Icon, info.Icon, StringComparison.Ordinal)) NotifyChanged("icon");
        if (old.Paired != info.Paired) NotifyChanged("paired");
        if (old.Paired != info.Paired || old.CanPair != info.CanPair) NotifyChanged("canPair");
        if (old.Present != info.Present) NotifyChanged("present");
        if (old.Connected != info.Connected) { NotifyChanged("connected"); NotifyChanged("state"); }
        if (old.Battery.HasValue != info.Battery.HasValue) NotifyChanged("batteryAvailable");
        if (old.Battery != info.Battery) NotifyChanged("battery");
    }

    [Qml("connect")] public void ConnectDevice() => _ = SetConnectionAsync(true);
    [Qml] public void Disconnect() => _ = SetConnectionAsync(false);
    internal Task SetConnectionAsync(bool connect) => RunAsync(connect ? BluetoothOperation.Connecting : BluetoothOperation.Disconnecting, async info =>
    {
        if (!info.Paired || info.Container is not { } container)
            return "Windows connection requests require a paired Bluetooth audio device with a device container.";
        return await _service.Backend.SetAudioConnectionAsync(container, connect).ConfigureAwait(false)
            ? null : "Windows accepted no Bluetooth audio connection request. Other device types reconnect through Windows.";
    });

    [Qml] public void Pair() => _ = PairAsync();
    internal Task PairAsync() => RunAsync(BluetoothOperation.Pairing, async info =>
    {
        if (info.Paired) return null;
        if (!info.CanPair) return "Windows reports that this device cannot be paired.";
        var cancellation = new CancellationTokenSource();
        lock (_pairingLock) _pairing = cancellation;
        try { return await _service.Backend.PairAsync(info.Id, cancellation.Token).ConfigureAwait(false); }
        finally
        {
            lock (_pairingLock)
            {
                _pairing = null;
                cancellation.Dispose();
            }
        }
    });
    [Qml] public void CancelPairing()
    {
        lock (_pairingLock) _pairing?.Cancel();
    }
    [Qml] public void Unpair() => _ = UnpairAsync();
    internal Task UnpairAsync() => RunAsync(BluetoothOperation.Unpairing, async info =>
    {
        foreach (var id in info.PairedIds)
        {
            var error = await _service.Backend.UnpairAsync(id).ConfigureAwait(false);
            if (error is not null) return error;
        }
        return null;
    });

    private async Task RunAsync(BluetoothOperation operation, Func<BluetoothDeviceInfo, Task<string?>> action)
    {
        if (Busy || IsDestroyed) return;
        SetOperation(operation);
        SetField(ref _error, "", nameof(Error));
        _service.Invalidate();
        string? error;
        try { error = await action(_info).ConfigureAwait(false); }
        catch (OperationCanceledException) { error = "Cancelled"; }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e)) { error = e.Message; }
        _service.Post(() =>
        {
            _service.Invalidate();
            if (!IsDestroyed)
            {
                SetOperation(BluetoothOperation.None);
                SetField(ref _error, error ?? "", nameof(Error));
                if (error is not null) Emit("operationFailed", error);
            }
            _service.Refresh();
        });
    }

    private void SetOperation(BluetoothOperation operation)
    {
        if (SetField(ref _operation, operation, nameof(Operation)))
        {
            NotifyChanged("busy");
            NotifyChanged("pairing");
        }
    }
    protected override void OnDestroy() => CancelPairing();
}

[QmlSignal("operationFailed", "message")]
public sealed class BluetoothService : QObject
{
    private readonly IEventLoop _loop;
    private readonly object _monitorGate = new();
    private readonly Dictionary<string, BluetoothDevice> _devices = new(StringComparer.Ordinal);
    private readonly bool _monitor;
    private CancellationTokenSource _stop = new();
    // Ends with the service. Stopping the monitor leaves this alone, so Refresh still reads.
    private readonly CancellationTokenSource _reading = new();
    private Task? _monitorTask;
    private BluetoothSnapshot _snapshot = BluetoothSnapshot.Initial;
    private BluetoothAdapter? _adapter;
    private string _error = "";
    // A destroyed service closes immediately when idle, or lets its in-flight read close the backend.
    private const int Idle = 0, Reading = 1, Closing = 2, Closed = 3;
    private int _reads;
    // Someone asked for a read while one ran. What they want to see may be newer than that read.
    private int _readAgain;
    private int _monitoring;
    private int _monitorHolders;
    private bool _started;
    private int _radioSet;
    private int _visibilitySet;
    private long _revision;

    internal BluetoothService(QmlEngine engine, IBluetoothBackend? backend = null)
        : this(backend ?? new BluetoothBackend(), engine.Loop, monitor: true)
    {
        Engine = engine;
        // Native singletons are not completed by ObjectBuilder, so start this generation here.
        ComponentComplete();
    }
    internal BluetoothService(IBluetoothBackend backend, IEventLoop loop, bool monitor = false)
    {
        Backend = backend;
        _loop = loop;
        _monitor = monitor;
        foreach (var model in new[] { Adapters, Devices, PairedDevices, NearbyDevices }) model.SetOwner(this);
    }
    internal IBluetoothBackend Backend { get; }
    internal Task? MonitorTask => _monitorTask;
    [Qml] public BluetoothAdapter? DefaultAdapter { get { Track(); return _adapter; } }
    [Qml] public ObjectModel Adapters { get; } = new();
    [Qml] public ObjectModel Devices { get; } = new();
    [Qml] public ObjectModel PairedDevices { get; } = new();
    [Qml] public ObjectModel NearbyDevices { get; } = new();
    [Qml] public bool Ready { get { Track(); return _snapshot.Ready; } }
    [Qml] public bool Discovering { get { Track(); return _snapshot.Discovering; } }
    [Qml] public string Error { get { Track(); return _error; } }

    public override void ComponentComplete()
    {
        if (!_monitor || _started || IsDestroyed) return;
        _started = true;
        StartMonitoring();
    }

    /// <summary>Reads adapter and device state until <see cref="StopMonitoring"/>.</summary>
    [Qml]
    public void StartMonitoring()
    {
        if (IsDestroyed)
            return;
        CancellationToken cancellation;
        CancellationTokenSource stop;
        lock (_monitorGate)
        {
            if (_monitoring != 0)
                return;
            if (_stop.IsCancellationRequested)
            {
                var retired = _stop;
                _stop = new CancellationTokenSource();
                // The previous loop disposes its source when it exits. Dispose here only when that loop is gone.
                if (_monitorHolders == 0)
                    retired.Dispose();
            }
            _monitoring = 1;
            _monitorHolders++;
            stop = _stop;
            cancellation = stop.Token;
        }
        // A long-running thread, not the pool. A busy pool never started the read, so the monitor had not begun.
        _monitorTask = Task.Factory.StartNew(
            async () =>
            {
                try
                {
                    await MonitorAsync(cancellation).ConfigureAwait(false);
                }
                finally
                {
                    lock (_monitorGate)
                    {
                        _monitorHolders--;
                        if (!ReferenceEquals(_stop, stop))
                            stop.Dispose();
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>Stops the three-second read. A later <see cref="StartMonitoring"/> starts it again.</summary>
    [Qml]
    public void StopMonitoring()
    {
        CancellationTokenSource stop;
        lock (_monitorGate)
        {
            if (_monitoring == 0)
                return;
            _monitoring = 0;
            stop = _stop;
        }
        stop.Cancel();
    }
    private async Task MonitorAsync(CancellationToken cancellation)
    {
        var warning = new MonitorWarning(this, "bluetooth");
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(false);
                warning.Succeeded();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) { return; }
#pragma warning disable CA1031 // One failed read must not stop the rest of the poll.
            catch (Exception e)
#pragma warning restore CA1031
            {
                warning.Failed(e);
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        }
    }
    internal void Post(Action action) => _loop.Post(() => { if (!IsDestroyed) action(); });
    internal void Invalidate() => Interlocked.Increment(ref _revision);
    [Qml] public void Refresh() => _ = RefreshAsync();
    internal async Task RefreshAsync()
    {
        if (IsDestroyed) return;
        if (Interlocked.CompareExchange(ref _reads, Reading, Idle) != Idle)
        {
            Volatile.Write(ref _readAgain, 1);
            // The read may have ended before it could see that, so try once more.
            if (Interlocked.CompareExchange(ref _reads, Reading, Idle) != Idle) return;
        }
        Volatile.Write(ref _readAgain, 0);
        var revision = Interlocked.Read(ref _revision);
        try
        {
            BluetoothSnapshot snapshot;
            try { snapshot = await Backend.ReadAsync(_reading.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e)) { snapshot = BluetoothSnapshot.Initial with { Error = e.Message }; }
            Post(() =>
            {
                if (revision == Interlocked.Read(ref _revision)) Apply(snapshot);
            });
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _reads, Idle, Reading) == Closing)
            {
                Volatile.Write(ref _reads, Closed);
                CloseBackend();
            }
            else if (Interlocked.Exchange(ref _readAgain, 0) != 0)
            {
                Refresh();
            }
        }
    }

    [Qml] public void StartDiscovery() => SetDiscovery(true);
    [Qml] public void StopDiscovery() => SetDiscovery(false);
    private void SetDiscovery(bool enabled)
    {
        if (IsDestroyed) return;
        Invalidate();
        try
        {
            if (enabled)
            {
                if (_adapter?.State != BluetoothAdapterState.On) throw new InvalidOperationException("Bluetooth must be on before discovery can start.");
                Backend.StartDiscovery();
            }
            else Backend.StopDiscovery();
            SetField(ref _error, "", nameof(Error));
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            SetField(ref _error, e.Message, nameof(Error));
            Emit("operationFailed", e.Message);
            return;
        }
        Refresh();
    }

    // Returns before Windows answers. enabled changes only when a later read reports the radio did.
    internal async Task SetAdapterEnabledAsync(BluetoothAdapter adapter, bool enabled)
    {
        if (IsDestroyed || adapter.IsDestroyed || !ReferenceEquals(adapter, _adapter) || adapter.ReportsOn == enabled)
            return;
        if (Interlocked.CompareExchange(ref _radioSet, 1, 0) != 0)
            return;
        var error = "";
        var report = false;
        try
        {
            Invalidate();
            error = await Backend.SetRadioEnabledAsync(enabled).ConfigureAwait(false) ?? "";
            report = true;
            if (error.Length == 0)
                await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            error = e.Message;
            report = true;
        }
        finally
        {
            var message = error;
            var shouldReport = report;
            Post(() =>
            {
                Volatile.Write(ref _radioSet, 0);
                if (shouldReport && !adapter.IsDestroyed)
                    adapter.Report(message);
            });
        }
    }

    // Windows accepts discovery only while incoming connections are on, and turning those off
    // requires discovery off first. The property changes when a later read reports it.
    internal async Task SetDiscoverableAsync(BluetoothAdapter adapter, bool discoverable)
    {
        if (IsDestroyed || adapter.IsDestroyed || !ReferenceEquals(adapter, _adapter) || adapter.Discoverable == discoverable)
            return;
        var openConnections = discoverable && !adapter.Pairable;
        await RunVisibilityAsync(adapter, async () =>
        {
            var changed = false;
            if (openConnections)
            {
                var incoming = await Backend.SetPairableAsync(true).ConfigureAwait(false);
                if (incoming is not null)
                    return (incoming, false);
                changed = true;
            }
            var error = await Backend.SetDiscoverableAsync(discoverable).ConfigureAwait(false);
            return (error, changed || error is null);
        }).ConfigureAwait(false);
    }

    internal async Task SetPairableAsync(BluetoothAdapter adapter, bool pairable)
    {
        if (IsDestroyed || adapter.IsDestroyed || !ReferenceEquals(adapter, _adapter) || adapter.Pairable == pairable)
            return;
        var closeDiscovery = !pairable && adapter.Discoverable;
        await RunVisibilityAsync(adapter, async () =>
        {
            var changed = false;
            if (closeDiscovery)
            {
                var discovery = await Backend.SetDiscoverableAsync(false).ConfigureAwait(false);
                if (discovery is not null)
                    return (discovery, false);
                changed = true;
            }
            var error = await Backend.SetPairableAsync(pairable).ConfigureAwait(false);
            return (error, changed || error is null);
        }).ConfigureAwait(false);
    }

    private async Task RunVisibilityAsync(BluetoothAdapter adapter, Func<Task<(string? Error, bool Changed)>> attempt)
    {
        if (Interlocked.CompareExchange(ref _visibilitySet, 1, 0) != 0)
            return;
        var error = "";
        var report = false;
        try
        {
            Invalidate();
            var result = await attempt().ConfigureAwait(false);
            error = result.Error ?? "";
            report = true;
            if (result.Changed)
                await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            error = e.Message;
            report = true;
        }
        finally
        {
            var message = error;
            var shouldReport = report;
            Post(() =>
            {
                Volatile.Write(ref _visibilitySet, 0);
                if (shouldReport && !adapter.IsDestroyed)
                    adapter.Report(message);
            });
        }
    }

    internal void Apply(BluetoothSnapshot snapshot)
    {
        if (IsDestroyed) return;
        if (SameSnapshot(_snapshot, snapshot))
            return;
        _snapshot = snapshot;
        NotifyChanged("ready");
        NotifyChanged("discovering");
        SetField(ref _error, snapshot.Error, nameof(Error));
        foreach (var model in new[] { Adapters, Devices, PairedDevices, NearbyDevices }) model.Engine = Engine;
        if (_adapter?.Id != snapshot.Adapter?.Id)
        {
            foreach (var device in _devices.Values) device.Destroy();
            _devices.Clear();
            _adapter?.Destroy();
            _adapter = snapshot.Adapter is { } info ? new BluetoothAdapter(this, info) { Engine = Engine } : null;
            _adapter?.SetOwner(this);
            NotifyChanged("defaultAdapter");
        }
        if (_adapter is not null && snapshot.Adapter is { } adapterInfo) _adapter.Update(adapterInfo);
        Adapters.Set(_adapter is null ? [] : [_adapter]);
        var incoming = _adapter is null ? [] : snapshot.Devices;
        foreach (var gone in _devices.Keys.Except(incoming.Select(d => d.Key)).ToArray())
        {
            _devices[gone].Destroy();
            _devices.Remove(gone);
        }
        foreach (var info in incoming)
        {
            if (!_devices.TryGetValue(info.Key, out var device))
            {
                device = new BluetoothDevice(this, info, _adapter!) { Engine = Engine };
                device.SetOwner(this);
                _devices.Add(info.Key, device);
            }
            device.Update(info);
        }
        Devices.Set(incoming.Select(d => _devices[d.Key]).ToArray());
        PairedDevices.Set(incoming.Where(d => d.Paired).Select(d => _devices[d.Key]).ToArray());
        NearbyDevices.Set(incoming.Where(d => !d.Paired && d.Present).Select(d => _devices[d.Key]).ToArray());
    }

    private static bool SameSnapshot(BluetoothSnapshot left, BluetoothSnapshot right)
    {
        if (left.Ready != right.Ready || left.Discovering != right.Discovering || left.Adapter != right.Adapter
            || !string.Equals(left.Error, right.Error, StringComparison.Ordinal) || left.Devices.Count != right.Devices.Count)
            return false;
        for (var i = 0; i < left.Devices.Count; i++)
        {
            if (left.Devices[i] != right.Devices[i])
                return false;
        }
        return true;
    }
    protected override void OnDestroy()
    {
        StopMonitoring();
        foreach (var device in _devices.Values) device.CancelPairing();
        _reading.Cancel();
        while (true)
        {
            var reads = Volatile.Read(ref _reads);
            if (reads == Reading && Interlocked.CompareExchange(ref _reads, Closing, Reading) == Reading)
                return;
            if (reads == Idle && Interlocked.CompareExchange(ref _reads, Closed, Idle) == Idle)
            {
                CloseBackend();
                return;
            }
            if (reads is Closing or Closed)
                return;
        }
    }
    private void CloseBackend()
    {
        try { Backend.Dispose(); }
        finally
        {
            _reading.Dispose();
            ServiceSupport.DisposeAfterMonitor(_stop, _monitorTask);
        }
    }
}

internal static class BluetoothModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Bluetooth")
            .Singleton(e => new BluetoothService(e), "Bluetooth")
            .Uncreatable<BluetoothAdapter>("BluetoothAdapter")
            .Uncreatable<BluetoothDevice>("BluetoothDevice")
            .Enum<BluetoothAdapterState>(s => s.ToString())
            .Enum<BluetoothDeviceState>(s => s.ToString())
            .Enum<BluetoothOperation>(s => s.ToString());
    }
}
