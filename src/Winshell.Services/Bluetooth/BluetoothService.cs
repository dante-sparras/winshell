using System.Collections.ObjectModel;
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
    private readonly IRadioAccess _radioAccess;

    private readonly MonitorWarning _warning;

    private readonly object _gate = new();

    private readonly StateReader<BluetoothSnapshot> _reader;

    private readonly DeviceAction _radioChange;
    private readonly DeviceAction _visibilityChange;
    private IDisposable? _poll;
    private BluetoothSnapshot _snapshot = BluetoothSnapshot.Initial;
    private BluetoothAdapter? _adapter;

    private string _error = "";
    private volatile bool _disposed;

    internal BluetoothService(IEventLoop loop, Log log, IBluetoothBackend? backend = null,
        IRadioAccess? radioAccess = null)
    {
        _loop = loop;
        _radioAccess = radioAccess ?? WindowsRadioAccess.Instance;
        _warning = new MonitorWarning(log, "bluetooth");
        _radioChange = new DeviceAction(Post);
        _visibilityChange = new DeviceAction(Post);
        Backend = backend ?? new BluetoothBackend(loop.Time);
        _reader = new StateReader<BluetoothSnapshot>(ReadAsync, Apply, loop, _warning);
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
        catch (Exception e) when (WindowsFailure.IsExpected(e))
        {
            _warning.Failed(e);
        }

        lock (_gate)
        {
            _poll ??= SchedulePoll();
        }

        Refresh();
    }

    // Called under the gate. A tick of a timer that was stopped, or replaced, does nothing.
    private IDisposable SchedulePoll()
    {
        IDisposable? timer = null;
        timer = _loop.Schedule(PollInterval, () =>
        {
            lock (_gate)
            {
                if (_poll != timer)
                    return;
                _poll = SchedulePoll();
            }

            Refresh();
        });
        return timer;
    }

    /// <summary>Stops watching Windows. A later <see cref="StartMonitoring"/> starts again.</summary>
    public void StopMonitoring()
    {
        IDisposable? poll;
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
        _reader.Invalidate();
    }

    /// <summary>Reads the state now, rather than at the next change Windows reports.</summary>
    public void Refresh()
    {
        _reader.Request();
    }

    internal Task RefreshAsync()
    {
        return _reader.ReadAsync();
    }

    private async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
    {
        try
        {
            return await Backend.ReadAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (WindowsFailure.IsExpected(e))
        {
            return BluetoothSnapshot.Initial with { Error = e.Message };
        }
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
        catch (Exception e) when (WindowsFailure.IsExpected(e))
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
        var radios = _snapshot.Adapter!.Radio.Radios;
        await ChangeAdapterAsync(adapter, _radioChange, async () =>
        {
            var error = await RadioSwitch.SetEnabledAsync(RadioKind.Bluetooth, radios, enabled, _radioAccess)
                .ConfigureAwait(false);
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
        await ChangeAdapterAsync(adapter, _visibilityChange, async () =>
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
        await ChangeAdapterAsync(adapter, _visibilityChange, async () =>
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

    private Task ChangeAdapterAsync(BluetoothAdapter adapter, DeviceAction action,
        Func<Task<(string? Error, bool Changed)>> attempt)
    {
        return action.RunAsync(Invalidate, async () =>
        {
            var result = await attempt().ConfigureAwait(false);
            if (result.Changed)
                await RefreshAsync().ConfigureAwait(false);
            return result.Error;
        }, error =>
        {
            if (ReferenceEquals(adapter, _adapter))
                adapter.Report(error);
        });
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
        _reader.Stop(Backend.Dispose);
    }
}
