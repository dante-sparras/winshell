using System.Collections.ObjectModel;

namespace Winshell.Bluetooth;

/// <summary>
/// The default Windows Bluetooth adapter. Setting <see cref="Enabled"/> asks Windows to change its radio.
/// Setting <see cref="Discoverable"/> or <see cref="Pairable"/> asks Windows to change discovery or incoming connections.
/// </summary>
public sealed class BluetoothAdapter : ObservableObject
{
    private readonly BluetoothService _service;
    private BluetoothAdapterInfo _info;
    private string _error = "";

    internal BluetoothAdapter(BluetoothService service, BluetoothAdapterInfo info)
    {
        _service = service;
        _info = info;
    }

    /// <summary>A radio, discoverable or incoming-connection change failed, with the same text as <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    public string Id => _info.Id;

    public string Name => _info.Name;

    public string Address => _info.Address;

    public BluetoothAdapterState State => _info.State;

    public bool Enabled
    {
        get => ReportsOn;
        set => _ = _service.SetAdapterEnabledAsync(this, value);
    }

    public bool Discoverable
    {
        get => _info.Discoverable;
        set => _ = _service.SetDiscoverableAsync(this, value);
    }

    public bool Pairable
    {
        get => _info.Pairable;
        set => _ = _service.SetPairableAsync(this, value);
    }

    public string Error => _error;

    internal bool ReportsOn => _info.State == BluetoothAdapterState.On;

    internal void Report(string error)
    {
        Set(ref _error, error, nameof(Error));
        if (error.Length > 0)
            OperationFailed?.Invoke(error);
    }

    internal void Update(BluetoothAdapterInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Address != info.Address, nameof(Address));
        Changed(old.Discoverable != info.Discoverable, nameof(Discoverable));
        Changed(old.Pairable != info.Pairable, nameof(Pairable));
        Changed(old.State != info.State, nameof(State));
        Changed((old.State == BluetoothAdapterState.On) != ReportsOn, nameof(Enabled));
        if (old.State != info.State || old.Discoverable != info.Discoverable || old.Pairable != info.Pairable)
            Set(ref _error, "", nameof(Error));
    }
}

/// <summary>A paired or nearby device. Endpoints that Windows groups in one device container are one device.</summary>
public sealed class BluetoothDevice : ObservableObject
{
    private readonly BluetoothService _service;
    private readonly object _pairingLock = new();
    private BluetoothDeviceInfo _info;
    private BluetoothOperation _operation;
    private string _error = "";
    private CancellationTokenSource? _pairing;
    private bool _removed;

    internal BluetoothDevice(BluetoothService service, BluetoothDeviceInfo info, BluetoothAdapter adapter)
    {
        _service = service;
        _info = info;
        Adapter = adapter;
    }

    /// <summary>An action failed, with the same text as <see cref="Error"/>.</summary>
    public event Action<string>? OperationFailed;

    internal string Key => _info.Key;

    public string Id => _info.Id;

    public BluetoothAdapter Adapter { get; }

    public string Address => _info.Address;

    public string Name => _info.Name;

    /// <summary>A BlueZ device-type icon name. Empty when Windows reports no class, or a class BlueZ does not name.</summary>
    public string Icon => _info.Icon;

    public bool Paired => _info.Paired;

    public bool Present => _info.Present;

    public bool CanPair => !_info.Paired && _info.CanPair;

    public bool Connected => _info.Connected;

    public BluetoothDeviceState State => _info.Connected ? BluetoothDeviceState.Connected : BluetoothDeviceState.Disconnected;

    public bool BatteryAvailable => _info.Battery.HasValue;

    /// <summary>The battery level, from 0 to 1. 0 when <see cref="BatteryAvailable"/> is false.</summary>
    public double Battery => _info.Battery ?? 0;

    public BluetoothOperation Operation => _operation;

    /// <summary>An action is running. Other actions are ignored until it ends.</summary>
    public bool Busy => _operation != BluetoothOperation.None;

    public bool Pairing => _operation == BluetoothOperation.Pairing;

    public string Error => _error;

    /// <summary>Asks Windows to connect a paired audio device. Fails for other device types, which reconnect through Windows.</summary>
    public void Connect() => _ = SetConnectionAsync(true);

    public void Disconnect() => _ = SetConnectionAsync(false);

    /// <summary>Pairs the device with basic Windows pairing, which may show consent or PIN UI.</summary>
    public void Pair() => _ = PairAsync();

    /// <summary>Cancels a running <see cref="Pair"/>. Its <see cref="Error"/> becomes <c>Cancelled</c>.</summary>
    public void CancelPairing()
    {
        lock (_pairingLock)
            _pairing?.Cancel();
    }

    /// <summary>Unpairs every paired endpoint of the device, stopping at the first failure.</summary>
    public void Unpair() => _ = UnpairAsync();

    internal Task SetConnectionAsync(bool connect) => RunAsync(connect ? BluetoothOperation.Connecting : BluetoothOperation.Disconnecting, async info =>
    {
        if (!info.Paired || info.Container is not { } container)
            return "Windows connection requests require a paired Bluetooth audio device with a device container.";
        return await _service.Backend.SetAudioConnectionAsync(container, connect).ConfigureAwait(false)
            ? null : "Windows accepted no Bluetooth audio connection request. Other device types reconnect through Windows.";
    });

    internal Task PairAsync() => RunAsync(BluetoothOperation.Pairing, async info =>
    {
        if (info.Paired)
            return null;
        if (!info.CanPair)
            return "Windows reports that this device cannot be paired.";
        var cancellation = new CancellationTokenSource();
        lock (_pairingLock)
            _pairing = cancellation;
        try
        {
            return await _service.Backend.PairAsync(info.Id, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_pairingLock)
            {
                _pairing = null;
                cancellation.Dispose();
            }
        }
    });

    internal Task UnpairAsync() => RunAsync(BluetoothOperation.Unpairing, async info =>
    {
        foreach (var id in info.PairedIds)
        {
            var error = await _service.Backend.UnpairAsync(id).ConfigureAwait(false);
            if (error is not null)
                return error;
        }
        return null;
    });

    private async Task RunAsync(BluetoothOperation operation, Func<BluetoothDeviceInfo, Task<string?>> action)
    {
        if (Busy || _removed)
            return;
        SetOperation(operation);
        Set(ref _error, "", nameof(Error));
        _service.Invalidate();
        string? error;
        try
        {
            error = await action(_info).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error = "Cancelled";
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            error = e.Message;
        }
        _service.Post(() =>
        {
            _service.Invalidate();
            if (!_removed)
            {
                SetOperation(BluetoothOperation.None);
                Set(ref _error, error ?? "", nameof(Error));
                if (error is not null)
                    OperationFailed?.Invoke(error);
            }
            _service.Refresh();
        });
    }

    private void SetOperation(BluetoothOperation operation)
    {
        var wasPairing = Pairing;
        var wasBusy = Busy;
        if (!Set(ref _operation, operation, nameof(Operation)))
            return;
        Changed(wasBusy != Busy, nameof(Busy));
        Changed(wasPairing != Pairing, nameof(Pairing));
    }

    internal void Update(BluetoothDeviceInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Id != info.Id, nameof(Id));
        Changed(old.Address != info.Address, nameof(Address));
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Icon != info.Icon, nameof(Icon));
        Changed(old.Paired != info.Paired, nameof(Paired));
        Changed((!old.Paired && old.CanPair) != CanPair, nameof(CanPair));
        Changed(old.Present != info.Present, nameof(Present));
        Changed(old.Connected != info.Connected, nameof(Connected));
        Changed(old.Connected != info.Connected, nameof(State));
        Changed(old.Battery.HasValue != info.Battery.HasValue, nameof(BatteryAvailable));
        Changed((old.Battery ?? 0) != (info.Battery ?? 0), nameof(Battery));
    }

    internal void Remove()
    {
        _removed = true;
        CancelPairing();
    }
}

/// <summary>
/// The default adapter and the paired and nearby devices. Reading it never pairs, connects or changes a radio. While
/// monitoring is on, state is read again every three seconds.
/// </summary>
public sealed class BluetoothService : ObservableObject, IDisposable
{
    // A disposed service closes immediately when idle, or lets its in-flight read close the backend.
    private const int Idle = 0, Reading = 1, Closing = 2, Closed = 3;

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly object _monitorGate = new();
    // Ends with the service. Stopping the monitor leaves this alone, so Refresh still reads.
    private readonly CancellationTokenSource _reading = new();
    private CancellationTokenSource _stop = new();
    private Task? _monitorTask;
    private BluetoothSnapshot _snapshot = BluetoothSnapshot.Initial;
    private BluetoothAdapter? _adapter;
    private string _error = "";
    private int _reads;
    // Someone asked for a read while one ran. What they want to see may be newer than that read.
    private int _readAgain;
    private int _monitoring;
    private int _monitorHolders;
    private int _radioSet;
    private int _visibilitySet;
    private long _revision;
    private volatile bool _disposed;

    internal BluetoothService(IEventLoop loop, Log log, IBluetoothBackend? backend = null, bool monitor = true)
    {
        _loop = loop;
        _log = log;
        Backend = backend ?? new BluetoothBackend();
        if (monitor)
            StartMonitoring();
    }

    /// <summary>Starting or stopping discovery failed.</summary>
    public event Action<string>? OperationFailed;

    internal IBluetoothBackend Backend { get; }

    internal Task? MonitorTask => _monitorTask;

    /// <summary>The default adapter Windows returns, or null when there is none.</summary>
    public BluetoothAdapter? DefaultAdapter => _adapter;

    public ObservableCollection<BluetoothAdapter> Adapters { get; } = [];

    public ObservableCollection<BluetoothDevice> Devices { get; } = [];

    public ObservableCollection<BluetoothDevice> PairedDevices { get; } = [];

    /// <summary>The present unpaired devices found during discovery.</summary>
    public ObservableCollection<BluetoothDevice> NearbyDevices { get; } = [];

    /// <summary>A read has completed. A completed read can have no adapter.</summary>
    public bool Ready => _snapshot.Ready;

    public bool Discovering => _snapshot.Discovering;

    public string Error => _error;

    /// <summary>Reads adapter and device state until <see cref="StopMonitoring"/>. The service starts this when it is created.</summary>
    public void StartMonitoring()
    {
        if (_disposed)
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
        var warning = new MonitorWarning(() => _log, "bluetooth");
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(false);
                warning.Succeeded();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
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
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal void Post(Action action) => _loop.Post(() =>
    {
        if (!_disposed)
            action();
    });

    internal void Invalidate() => Interlocked.Increment(ref _revision);

    /// <summary>Reads the state now, rather than at the next three-second read.</summary>
    public void Refresh() => _ = RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_disposed)
            return;
        if (Interlocked.CompareExchange(ref _reads, Reading, Idle) != Idle)
        {
            Volatile.Write(ref _readAgain, 1);
            // The read may have ended before it could see that, so try once more.
            if (Interlocked.CompareExchange(ref _reads, Reading, Idle) != Idle)
                return;
        }
        Volatile.Write(ref _readAgain, 0);
        var revision = Interlocked.Read(ref _revision);
        try
        {
            BluetoothSnapshot snapshot;
            try
            {
                snapshot = await Backend.ReadAsync(_reading.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
            {
                snapshot = BluetoothSnapshot.Initial with { Error = e.Message };
            }
            Post(() =>
            {
                if (revision == Interlocked.Read(ref _revision))
                    Apply(snapshot);
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

    /// <summary>Starts looking for nearby devices. Needs an adapter that is on.</summary>
    public void StartDiscovery() => SetDiscovery(true);

    /// <summary>Stops looking, and clears the unpaired devices it found.</summary>
    public void StopDiscovery() => SetDiscovery(false);

    private void SetDiscovery(bool enabled)
    {
        if (_disposed)
            return;
        Invalidate();
        try
        {
            if (enabled)
            {
                if (_adapter?.State != BluetoothAdapterState.On)
                    throw new InvalidOperationException("Bluetooth must be on before discovery can start.");
                Backend.StartDiscovery();
            }
            else
            {
                Backend.StopDiscovery();
            }
            Set(ref _error, "", nameof(Error));
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            Set(ref _error, e.Message, nameof(Error));
            OperationFailed?.Invoke(e.Message);
            return;
        }
        Refresh();
    }

    // Returns before Windows answers. Enabled changes only when a later read reports the radio did.
    internal async Task SetAdapterEnabledAsync(BluetoothAdapter adapter, bool enabled)
    {
        if (_disposed || !ReferenceEquals(adapter, _adapter) || adapter.ReportsOn == enabled)
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
                if (shouldReport && ReferenceEquals(adapter, _adapter))
                    adapter.Report(message);
            });
        }
    }

    // Windows accepts discovery only while incoming connections are on, and turning those off
    // requires discovery off first. The property changes when a later read reports it.
    internal async Task SetDiscoverableAsync(BluetoothAdapter adapter, bool discoverable)
    {
        if (_disposed || !ReferenceEquals(adapter, _adapter) || adapter.Discoverable == discoverable)
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
        if (_disposed || !ReferenceEquals(adapter, _adapter) || adapter.Pairable == pairable)
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
                if (shouldReport && ReferenceEquals(adapter, _adapter))
                    adapter.Report(message);
            });
        }
    }

    private void Apply(BluetoothSnapshot snapshot)
    {
        if (_disposed || SameSnapshot(_snapshot, snapshot))
            return;
        var old = _snapshot;
        _snapshot = snapshot;
        Changed(old.Ready != snapshot.Ready, nameof(Ready));
        Changed(old.Discovering != snapshot.Discovering, nameof(Discovering));
        Set(ref _error, snapshot.Error, nameof(Error));
        if (_adapter?.Id != snapshot.Adapter?.Id)
        {
            RemoveDevices();
            Set(ref _adapter, snapshot.Adapter is { } info ? new BluetoothAdapter(this, info) : null, nameof(DefaultAdapter));
            Adapters.Clear();
            if (_adapter is not null)
                Adapters.Add(_adapter);
        }
        if (snapshot.Adapter is { } adapterInfo)
            _adapter?.Update(adapterInfo);
        var adapter = _adapter;
        var incoming = adapter is null ? [] : snapshot.Devices;
        Devices.Sync(incoming, d => d.Key, i => i.Key, i => new BluetoothDevice(this, i, adapter!), (d, i) => d.Update(i), d => d.Remove());
        Mirror(PairedDevices, Devices.Where(d => d.Paired));
        Mirror(NearbyDevices, Devices.Where(d => !d.Paired && d.Present));
    }

    private void RemoveDevices()
    {
        foreach (var device in Devices)
            device.Remove();
        Devices.Clear();
        PairedDevices.Clear();
        NearbyDevices.Clear();
    }

    private static void Mirror(ObservableCollection<BluetoothDevice> list, IEnumerable<BluetoothDevice> next) =>
        list.Sync(next, d => d, d => d, d => d, (_, _) => { });

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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopMonitoring();
        foreach (var device in Devices)
            device.Remove();
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
        try
        {
            Backend.Dispose();
        }
        finally
        {
            _reading.Dispose();
            ServiceSupport.DisposeAfterMonitor(_stop, _monitorTask);
        }
    }
}
