using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Bluetooth;

public enum BluetoothAdapterState { Unknown, Off, On, Disabled }
public enum BluetoothDeviceState { Disconnected, Connected }
public enum BluetoothOperation { None, Connecting, Disconnecting, Pairing, Unpairing }

/// <summary>The default Windows Bluetooth adapter. Windows controls its radio settings.</summary>
public sealed class BluetoothAdapter : QObject
{
    private BluetoothAdapterInfo _info;
    internal BluetoothAdapter(BluetoothAdapterInfo info) => _info = info;
    [Qml] public string Id => _info.Id;
    [Qml] public string Name { get { Track(); return _info.Name; } }
    [Qml] public string Address { get { Track(); return _info.Address; } }
    [Qml] public BluetoothAdapterState State { get { Track(); return _info.State; } }
    [Qml] public bool Enabled { get { Track(); return _info.State == BluetoothAdapterState.On; } }
    internal void Update(BluetoothAdapterInfo info)
    {
        var old = _info;
        _info = info;
        if (old.Name != info.Name) NotifyChanged("name");
        if (old.Address != info.Address) NotifyChanged("address");
        if (old.State != info.State) { NotifyChanged("state"); NotifyChanged("enabled"); }
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
    private BluetoothSnapshot _snapshot = BluetoothSnapshot.Initial;
    private BluetoothAdapter? _adapter;
    private string _error = "";
    private int _refreshing;
    private int _monitoring;
    private int _monitorHolders;
    private bool _started;
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
        _ = Task.Run(async () =>
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
        });
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
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await RefreshAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    internal void Post(Action action) => _loop.Post(() => { if (!IsDestroyed) action(); });
    internal void Invalidate() => Interlocked.Increment(ref _revision);
    [Qml] public void Refresh() => _ = RefreshAsync();
    internal async Task RefreshAsync()
    {
        if (IsDestroyed || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        var revision = Interlocked.Read(ref _revision);
        BluetoothSnapshot snapshot;
        try { snapshot = await Backend.ReadAsync().ConfigureAwait(false); }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e)) { snapshot = BluetoothSnapshot.Initial with { Error = e.Message }; }
        Post(() =>
        {
            Interlocked.Exchange(ref _refreshing, 0);
            if (revision == Interlocked.Read(ref _revision)) Apply(snapshot);
        });
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
            _adapter = snapshot.Adapter is { } info ? new BluetoothAdapter(info) { Engine = Engine } : null;
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
        Backend.Dispose();
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
