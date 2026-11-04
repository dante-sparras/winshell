using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Winshell.Bluetooth;

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
    // One radio change and one visibility change run at a time. Boxed, so ChangeAdapterAsync can hold either flag.
    private readonly StrongBox<int> _radioSet = new();
    private readonly StrongBox<int> _visibilitySet = new();
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
    private long _revision;
    private volatile bool _disposed;

    internal BluetoothService(IEventLoop loop, Log log, IBluetoothBackend? backend = null)
    {
        _loop = loop;
        _log = log;
        Backend = backend ?? new BluetoothBackend();
    }

    /// <summary>Starting or stopping discovery failed.</summary>
    public event Action<string>? OperationFailed;

    internal IBluetoothBackend Backend { get; }

    internal Task? MonitorTask => _monitorTask;

    /// <summary>The default adapter Windows returns, or null when there is none.</summary>
    public BluetoothAdapter? DefaultAdapter => _adapter;

    /// <summary>Only the <see cref="DefaultAdapter"/>, or empty when there is none.</summary>
    public ObservableCollection<BluetoothAdapter> Adapters { get; } = [];

    /// <summary>
    /// The paired devices and the nearby ones found during discovery: connected first, then paired, then by name.
    /// Empty when there is no adapter.
    /// </summary>
    public ObservableCollection<BluetoothDevice> Devices { get; } = [];

    /// <summary>The paired devices, in the order of <see cref="Devices"/>.</summary>
    public ObservableCollection<BluetoothDevice> PairedDevices { get; } = [];

    /// <summary>The present unpaired devices found during discovery.</summary>
    public ObservableCollection<BluetoothDevice> NearbyDevices { get; } = [];

    /// <summary>A read has completed. A completed read can have no adapter.</summary>
    public bool Ready => _snapshot.Ready;

    /// <summary>Whether Windows is looking for nearby devices, after <see cref="StartDiscovery"/>.</summary>
    public bool Discovering => _snapshot.Discovering;

    /// <summary>Why the last read, or starting or stopping discovery, failed. Empty when it succeeded.</summary>
    public string Error => _error;

    /// <summary>Reads adapter and device state until <see cref="StopMonitoring"/>. Nothing is read before this.</summary>
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
        await ChangeAdapterAsync(adapter, _radioSet, async () =>
        {
            var error = await Backend.SetRadioEnabledAsync(enabled).ConfigureAwait(false);
            return (error, error is null);
        }).ConfigureAwait(false);
    }

    // Windows accepts discovery only while incoming connections are on, and turning those off
    // requires discovery off first. The property changes when a later read reports it.
    internal async Task SetDiscoverableAsync(BluetoothAdapter adapter, bool discoverable)
    {
        if (_disposed || !ReferenceEquals(adapter, _adapter) || adapter.Discoverable == discoverable)
            return;
        var openConnections = discoverable && !adapter.Pairable;
        await ChangeAdapterAsync(adapter, _visibilitySet, async () =>
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
        await ChangeAdapterAsync(adapter, _visibilitySet, async () =>
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

    private async Task ChangeAdapterAsync(BluetoothAdapter adapter, StrongBox<int> busy, Func<Task<(string? Error, bool Changed)>> attempt)
    {
        if (Interlocked.CompareExchange(ref busy.Value, 1, 0) != 0)
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
                Volatile.Write(ref busy.Value, 0);
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

    /// <summary>Stops monitoring and discovery, and cancels a running pairing.</summary>
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
