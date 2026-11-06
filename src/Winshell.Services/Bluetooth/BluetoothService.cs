using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Windows.Devices.Radios;

namespace Winshell.Bluetooth;

/// <summary>
/// The default adapter and the paired and nearby devices. Reading it never pairs, connects or changes a radio. While
/// monitoring is on, Windows' device and radio events read the state again, and so does a slow timer, because Windows
/// announces no change to discoverable and incoming-connection state, or to a battery level.
/// </summary>
public sealed class BluetoothService : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IEventLoop _loop;
    private readonly TimeProvider _time;
    private readonly IRadioSource _radios;

    private readonly MonitorWarning _warning;

    // Guards the timer and the read flags. Nothing awaits inside it.
    private readonly object _gate = new();

    // Ends with the service, so a read that Windows does not cancel ends at its next check.
    private readonly CancellationTokenSource _ended = new();

    // One radio change and one visibility change run at a time. Boxed, so ChangeAdapterAsync can hold either flag.
    private readonly StrongBox<int> _radioSet = new();
    private readonly StrongBox<int> _visibilitySet = new();
    private ITimer? _poll;
    private BluetoothSnapshot _snapshot = BluetoothSnapshot.Initial;
    private BluetoothAdapter? _adapter;

    private string _error = "";

    // Someone asked for a read while one ran. What they want to see may be newer than that read.
    private bool _reading, _readAgain;
    private long _revision;
    private volatile bool _disposed;

    internal BluetoothService(IEventLoop loop, Log log, IBluetoothBackend? backend = null, TimeProvider? time = null,
        IRadioSource? radios = null)
    {
        _loop = loop;
        _time = time ?? TimeProvider.System;
        _radios = radios ?? WindowsRadioSource.Instance;
        _warning = new MonitorWarning(() => log, "bluetooth");
        Backend = backend ?? new BluetoothBackend();
        Backend.Changed += Refresh;
    }

    /// <summary>Starting or stopping discovery failed.</summary>
    public event Action<string>? OperationFailed;

    internal IBluetoothBackend Backend { get; }

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

    /// <summary>Reads adapter and device state, and again at every change Windows reports, until <see cref="StopMonitoring"/>. Nothing is read before this.</summary>
    public void StartMonitoring()
    {
        if (_disposed)
            return;
        try
        {
            Backend.StartWatching();
        }
        catch (Exception e) when (BluetoothBackend.IsExpectedFailure(e))
        {
            // Without events the timer still reads, only later.
            _warning.Failed(e);
        }

        lock (_gate)
        {
            _poll ??= _time.CreateTimer(_ => Refresh(), null, PollInterval, PollInterval);
        }

        Refresh();
    }

    /// <summary>Stops watching Windows. A later <see cref="StartMonitoring"/> starts again.</summary>
    public void StopMonitoring()
    {
        ITimer? poll;
        lock (_gate)
        {
            poll = _poll;
            _poll = null;
        }

        poll?.Dispose();
        Backend.StopWatching();
    }

    internal void Post(Action action)
    {
        _loop.Post(() =>
        {
            if (!_disposed)
                action();
        });
    }

    internal void Invalidate()
    {
        Interlocked.Increment(ref _revision);
    }

    /// <summary>Reads the state now, rather than at the next change Windows reports.</summary>
    public void Refresh()
    {
        _ = RefreshAsync();
    }

    // One read runs at a time. A request during it reads again when it ends, so the result is never older than the request.
    internal async Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_reading)
            {
                _readAgain = true;
                return;
            }

            _reading = true;
        }

        bool again;
        do
        {
            lock (_gate)
            {
                _readAgain = false;
            }

            try
            {
                await ReadAsync().ConfigureAwait(false);
            }
            finally
            {
                bool close;
                lock (_gate)
                {
                    again = _readAgain && !_disposed;
                    _reading = again;
                    close = _disposed && !again;
                }

                if (close)
                    CloseBackend();
            }
        } while (again);
    }

    private async Task ReadAsync()
    {
        var revision = Interlocked.Read(ref _revision);
        BluetoothSnapshot snapshot;
        try
        {
            snapshot = await Backend.ReadAsync(_ended.Token).ConfigureAwait(false);
            _warning.Succeeded();
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
#pragma warning disable CA1031 // Nobody awaits a refresh, and one failed read must not stop the next.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _warning.Failed(e);
            return;
        }

        Post(() =>
        {
            if (revision == Interlocked.Read(ref _revision))
                Apply(snapshot);
        });
    }

    /// <summary>Starts looking for nearby devices. Needs an adapter that is on.</summary>
    public void StartDiscovery()
    {
        SetDiscovery(true);
    }

    /// <summary>Stops looking, and clears the unpaired devices it found.</summary>
    public void StopDiscovery()
    {
        SetDiscovery(false);
    }

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
            var radio = await Backend.GetRadioAsync().ConfigureAwait(false);
            if (radio is null || radio.Kind != RadioKind.Bluetooth)
                return (RadioSwitch.NoAdapterRadio, false);
            var error = await RadioSwitch.SetEnabledAsync(radio, enabled, _radios).ConfigureAwait(false);
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

    private async Task ChangeAdapterAsync(BluetoothAdapter adapter, StrongBox<int> busy,
        Func<Task<(string? Error, bool Changed)>> attempt)
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
            Set(ref _adapter, snapshot.Adapter is { } info ? new BluetoothAdapter(this, info) : null,
                nameof(DefaultAdapter));
            Adapters.Clear();
            if (_adapter is not null)
                Adapters.Add(_adapter);
        }

        if (snapshot.Adapter is { } adapterInfo)
            _adapter?.Update(adapterInfo);
        // Not null when a device is built: without an adapter, incoming is empty.
        var adapter = _adapter;
        var incoming = adapter is null ? [] : snapshot.Devices;
        Devices.Sync(incoming, d => d.Key, i => i.Key, i => new BluetoothDevice(this, i, adapter!),
            (d, i) => d.Update(i), d => d.Remove());
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

    private static void Mirror(ObservableCollection<BluetoothDevice> list, IEnumerable<BluetoothDevice> next)
    {
        list.Sync(next, d => d, d => d, d => d, (_, _) => { });
    }

    private static bool SameSnapshot(BluetoothSnapshot left, BluetoothSnapshot right)
    {
        if (left.Ready != right.Ready || left.Discovering != right.Discovering || left.Adapter != right.Adapter
            || !string.Equals(left.Error, right.Error, StringComparison.Ordinal) ||
            left.Devices.Count != right.Devices.Count)
            return false;
        for (var i = 0; i < left.Devices.Count; i++)
            if (left.Devices[i] != right.Devices[i])
                return false;
        return true;
    }

    /// <summary>Stops monitoring and discovery, and cancels a running pairing.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Backend.Changed -= Refresh;
        StopMonitoring();
        foreach (var device in Devices)
            device.Remove();
        _ended.Cancel();
        bool idle;
        lock (_gate)
        {
            idle = !_reading;
        }

        // A read in flight closes the backend when it ends.
        if (idle)
            CloseBackend();
    }

    private void CloseBackend()
    {
        try
        {
            Backend.Dispose();
        }
        finally
        {
            _ended.Dispose();
        }
    }
}
