using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using WinAdapter = Windows.Devices.Bluetooth.BluetoothAdapter;
using WinClassicDevice = Windows.Devices.Bluetooth.BluetoothDevice;
using WinLeDevice = Windows.Devices.Bluetooth.BluetoothLEDevice;

namespace Winshell.Bluetooth;

internal sealed record BluetoothAdapterInfo(string Id, string Name, string Address, BluetoothAdapterState State,
    bool Discoverable = false, bool Pairable = false);
internal sealed record BluetoothEndpointInfo(string Id, string Address, string Name, bool Connected, bool Paired,
    bool Present, bool CanPair, Guid? Container, BluetoothClassReport Class = default);
internal sealed record BluetoothDeviceInfo(string Key, string Id, string Address, string Name, bool Connected,
    bool Paired, bool Present, bool CanPair, double? Battery, Guid? Container, IReadOnlyList<string> PairedIds, string Icon = "");
internal sealed record BluetoothSnapshot(bool Ready, BluetoothAdapterInfo? Adapter, IReadOnlyList<BluetoothDeviceInfo> Devices,
    bool Discovering = false, string Error = "")
{
    public static readonly BluetoothSnapshot Initial = new(false, null, []);
}

/// <summary>
/// Reads only Windows adapter and enumeration properties. In particular, reading state never opens
/// BluetoothDevice/BluetoothLEDevice or GATT handles, which can prompt or affect a device connection.
/// Discoverable and incoming-connection state come from BluetoothIsDiscoverable and BluetoothIsConnectable.
/// The device icon is the class and appearance on that same enumeration.
/// Each shell generation owns and stops its own nearby-device watchers.
/// </summary>
internal sealed partial class BluetoothBackend : IBluetoothBackend
{
    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string PairedKey = "System.Devices.Aep.IsPaired";
    private const string PresentKey = "System.Devices.Aep.IsPresent";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly string[] EndpointProperties = [
        AddressKey, ConnectedKey, PairedKey, PresentKey, ContainerKey,
        BluetoothIcon.MajorKey, BluetoothIcon.MinorKey, BluetoothIcon.AppearanceKey, BluetoothIcon.CategoryKey, BluetoothIcon.SubcategoryKey,
    ];
    private readonly object _lock = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<string, BluetoothEndpointInfo> _nearby = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceInformation> _pairingEndpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<DeviceInformation, List<DeviceInformationUpdate>> _pairingUpdates = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly Dictionary<Guid, (double? Level, DateTimeOffset Read)> _batteries = [];
    private string? _adapterId;
    private string _discoveryError = "";
    private bool _hasRadio;
    private bool _disposed;

    public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);

    public async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
    {
        await _readGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (_lock) ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.ThrowIfCancellationRequested();
            var adapter = await WinAdapter.GetDefaultAsync().AsTask(cancellation).ConfigureAwait(false);
            if (adapter is null)
            {
                StopDiscovery();
                lock (_lock) _hasRadio = false;
                return new(true, null, []);
            }
            var name = (await DeviceInformation.CreateFromIdAsync(adapter.DeviceId).AsTask(cancellation).ConfigureAwait(false))?.Name ?? "Bluetooth";
            Radio? radio = null;
            try { radio = await adapter.GetRadioAsync().AsTask(cancellation).ConfigureAwait(false); }
            catch (Exception e) when (IsExpectedFailure(e)) { }
            lock (_lock) _hasRadio = radio is not null;
            cancellation.ThrowIfCancellationRequested();
            // These queries can block. Their own thread keeps them off the QML thread and off the pool.
            var visibility = await OffUi(() => (
                Discoverable: BluetoothIsDiscoverable(0),
                Pairable: BluetoothIsConnectable(0))).ConfigureAwait(false);
            var adapterInfo = new BluetoothAdapterInfo(adapter.DeviceId, name,
                ServiceSupport.FormatMac(adapter.BluetoothAddress), MapRadioState(radio?.State),
                visibility.Discoverable, visibility.Pairable);
            if ((_adapterId is not null && _adapterId != adapter.DeviceId) ||
                adapterInfo.State is BluetoothAdapterState.Off or BluetoothAdapterState.Disabled)
                StopDiscovery();
            _adapterId = adapter.DeviceId;
            List<BluetoothEndpointInfo> endpoints = [];
            foreach (var selector in PairedSelectors())
            {
                cancellation.ThrowIfCancellationRequested();
                var devices = await DeviceInformation.FindAllAsync(selector, EndpointProperties, DeviceInformationKind.AssociationEndpoint).AsTask(cancellation).ConfigureAwait(false);
                endpoints.AddRange(devices.Select(ReadEndpoint));
            }
            bool discovering;
            string discoveryError;
            lock (_lock)
            {
                discovering = _watchers.Any(w => w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted);
                discoveryError = _discoveryError;
                endpoints.AddRange(_nearby.Values.Where(e => e.Present));
            }
            var devicesByKey = MergeEndpoints(endpoints);
            var result = new List<BluetoothDeviceInfo>();
            foreach (var device in devicesByKey)
            {
                // Windows' cached device property only: do not query the remote Battery GATT service.
                var battery = device.Connected && device.Container is { } container
                    ? await ReadBatteryAsync(container, cancellation).ConfigureAwait(false) : null;
                result.Add(device with { Battery = battery });
            }
            return new(true, adapterInfo, result, discovering, discoveryError);
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            StopDiscovery();
            throw;
        }
        finally { _readGate.Release(); }
    }

    private static string[] PairedSelectors() =>
        [WinClassicDevice.GetDeviceSelectorFromPairingState(true), WinLeDevice.GetDeviceSelectorFromPairingState(true)];

    private static BluetoothEndpointInfo ReadEndpoint(DeviceInformation info) =>
        new(info.Id, info.Properties.GetValueOrDefault(AddressKey) as string ?? "", info.Name,
            info.Properties.GetValueOrDefault(ConnectedKey) is true,
            info.Properties.GetValueOrDefault(PairedKey) is true || info.Pairing.IsPaired,
            info.Properties.GetValueOrDefault(PresentKey) is true, info.Pairing.CanPair,
            info.Properties.GetValueOrDefault(ContainerKey) is Guid container && container != Guid.Empty ? container : null,
            BluetoothIcon.Read(info.Properties));

    internal static BluetoothAdapterState MapRadioState(RadioState? state) => state switch
    {
        RadioState.On => BluetoothAdapterState.On,
        RadioState.Off => BluetoothAdapterState.Off,
        RadioState.Disabled => BluetoothAdapterState.Disabled,
        _ => BluetoothAdapterState.Unknown,
    };

    internal static IReadOnlyList<BluetoothDeviceInfo> MergeEndpoints(IEnumerable<BluetoothEndpointInfo> endpoints)
    {
        // Prefer the fresh paired snapshot to a stale discovery event for the same endpoint.
        var unique = endpoints.DistinctBy(e => e.Id).ToArray();
        return unique.GroupBy(e => e.Container is { } container ? "container:" + container :
                e.Address.Length > 0 ? "address:" + e.Address.ToUpperInvariant() : "endpoint:" + e.Id,
                StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(e => e.Paired).ThenByDescending(e => e.CanPair)
                    .ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
                var first = ordered[0];
                var name = group.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? first.Address;
                return new BluetoothDeviceInfo(group.Key, first.Id, first.Address.ToUpperInvariant(), name,
                    group.Any(e => e.Connected), group.Any(e => e.Paired), group.Any(e => e.Present),
                    group.Any(e => e.CanPair), null, first.Container,
                    group.Where(e => e.Paired).Select(e => e.Id).ToArray(),
                    BluetoothIcon.FromReports(ordered.Select(e => e.Class)));
            }).OrderByDescending(d => d.Connected).ThenByDescending(d => d.Paired)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static double? BatteryLevel(object? value) => value switch
    {
        byte b when b <= 100 => b / 100.0,
        ushort n when n <= 100 => n / 100.0,
        uint n when n <= 100 => n / 100.0,
        int n when n is >= 0 and <= 100 => n / 100.0,
        _ => null,
    };

    private async Task<double?> ReadBatteryAsync(Guid container, CancellationToken cancellation)
    {
        if (_batteries.TryGetValue(container, out var cached) && TimeProvider.System.GetUtcNow() - cached.Read < TimeSpan.FromSeconds(30))
            return cached.Level;
        double? level = null;
        try
        {
            var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"", [BatteryKey], DeviceInformationKind.Device).AsTask(cancellation).ConfigureAwait(false);
            level = nodes.Select(n => BatteryLevel(n.Properties.GetValueOrDefault(BatteryKey))).FirstOrDefault(n => n.HasValue);
        }
        catch (Exception e) when (IsExpectedFailure(e)) { }
        _batteries[container] = (level, TimeProvider.System.GetUtcNow());
        return level;
    }

    /// <summary>
    /// Asks Windows to set the Bluetooth radios. Null when they report <paramref name="enabled"/>.
    /// An adapter whose own radio is missing fails before any request, so another radio is left alone.
    /// Tests use a fake backend and do not call this.
    /// </summary>
    public async Task<string?> SetRadioEnabledAsync(bool enabled)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_hasRadio)
                return RadioSwitch.NoAdapterRadio;
        }
        return await RadioSwitch.SetEnabledAsync(RadioKind.Bluetooth, enabled, WindowsRadioSource.Instance).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks Windows to change discovery. Null when a radio already reports <paramref name="discoverable"/> or the call changes one.
    /// The call can block, so it runs on its own thread. Tests use a fake and do not call this.
    /// </summary>
    public Task<string?> SetDiscoverableAsync(bool discoverable)
    {
        lock (_lock) ObjectDisposedException.ThrowIf(_disposed, this);
        return OffUi(() =>
        {
            if (BluetoothIsDiscoverable(0) == discoverable)
                return null;
            return BluetoothEnableDiscovery(0, discoverable) ? null : DiscoveryUnchanged;
        });
    }

    /// <summary>
    /// Asks Windows to change incoming connections, which Winshell exposes as pairable.
    /// Null when a radio already reports <paramref name="pairable"/> or the call changes one.
    /// The call can block, so it runs on its own thread. Tests use a fake and do not call this.
    /// </summary>
    public Task<string?> SetPairableAsync(bool pairable)
    {
        lock (_lock) ObjectDisposedException.ThrowIf(_disposed, this);
        return OffUi(() =>
        {
            if (BluetoothIsConnectable(0) == pairable)
                return null;
            return BluetoothEnableIncomingConnections(0, pairable) ? null : ConnectionsUnchanged;
        });
    }

    private const string DiscoveryUnchanged = "Windows did not change Bluetooth discovery.";
    private const string ConnectionsUnchanged = "Windows did not change incoming Bluetooth connections.";

    private static Task<T> OffUi<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

    public void StartDiscovery()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchers.Count > 0) return;
            _discoveryError = "";
            try
            {
                StartWatcher(WinClassicDevice.GetDeviceSelectorFromPairingState(false));
                StartWatcher(WinLeDevice.GetDeviceSelectorFromPairingState(false));
            }
            catch
            {
                StopDiscovery();
                throw;
            }
        }
    }

    private void StartWatcher(string selector)
    {
        var watcher = DeviceInformation.CreateWatcher(selector, EndpointProperties, DeviceInformationKind.AssociationEndpoint);
        // DeviceInformation.Update needs its original object; keep it local to this watcher.
        var entries = new Dictionary<string, DeviceInformation>(StringComparer.Ordinal);
        watcher.Added += (_, info) =>
        {
            lock (_lock)
            {
                if (_disposed || !_watchers.Contains(watcher)) return;
                entries[info.Id] = info;
                _pairingEndpoints[info.Id] = info;
                _nearby[info.Id] = ReadEndpoint(info);
            }
        };
        watcher.Updated += (_, update) =>
        {
            lock (_lock)
            {
                if (_disposed || !_watchers.Contains(watcher) || !entries.TryGetValue(update.Id, out var info)) return;
                if (_pairingUpdates.TryGetValue(info, out var pending))
                    pending.Add(update);
                else
                {
                    info.Update(update);
                    _nearby[info.Id] = ReadEndpoint(info);
                }
            }
        };
        watcher.Removed += (_, update) =>
        {
            lock (_lock)
            {
                if (!_watchers.Contains(watcher)) return;
                entries.Remove(update.Id);
                _pairingEndpoints.Remove(update.Id);
                _nearby.Remove(update.Id);
            }
        };
        watcher.Stopped += (_, _) =>
        {
            lock (_lock)
            {
                if (!_watchers.Remove(watcher)) return;
                foreach (var id in entries.Keys)
                {
                    _nearby.Remove(id);
                    _pairingEndpoints.Remove(id);
                }
                if (watcher.Status == DeviceWatcherStatus.Aborted)
                    _discoveryError = "Windows aborted Bluetooth discovery. Check Bluetooth access and try again.";
            }
        };
        _watchers.Add(watcher);
        watcher.Start();
    }

    public void StopDiscovery()
    {
        DeviceWatcher[] watchers;
        lock (_lock)
        {
            watchers = _watchers.ToArray();
            _watchers.Clear();
            _nearby.Clear();
            _pairingEndpoints.Clear();
        }
        foreach (var watcher in watchers)
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                try { watcher.Stop(); }
                catch (Exception e) when (IsExpectedFailure(e))
                {
                    lock (_lock) _discoveryError = e.Message;
                }
            }
        }
    }

    public async Task<string?> PairAsync(string id, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        DeviceInformation info;
        lock (_lock)
        {
            if (!_pairingEndpoints.TryGetValue(id, out info!)) return "The device is no longer available.";
            if (_pairingUpdates.ContainsKey(info)) return "Pairing is already in progress.";
            _pairingUpdates.Add(info, []);
        }
        try
        {
            if (info.Pairing.IsPaired) return null;
            if (!info.Pairing.CanPair) return "Windows reports that this device cannot be paired.";
            // Start consent on the caller's UI thread. The watcher defers updates to this object
            // until the WinRT pairing operation has finished, including cancellation or failure.
            var result = await info.Pairing.PairAsync().AsTask(cancellation).ConfigureAwait(false);
            return PairingError(result.Status);
        }
        finally
        {
            lock (_lock)
            {
                var pending = _pairingUpdates[info];
                _pairingUpdates.Remove(info);
                if (!_disposed && _pairingEndpoints.TryGetValue(id, out var current) && ReferenceEquals(current, info))
                {
                    foreach (var update in pending) info.Update(update);
                    _nearby[id] = ReadEndpoint(info);
                }
            }
        }
    }

    public async Task<string?> UnpairAsync(string id)
    {
        var info = await DeviceInformation.CreateFromIdAsync(id, null, DeviceInformationKind.AssociationEndpoint);
        if (info is null) return "The device is no longer available.";
        var result = await info.Pairing.UnpairAsync();
        lock (_lock) _nearby.Remove(id);
        return UnpairingError(result.Status);
    }

    internal static string? PairingError(DevicePairingResultStatus status) =>
        status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired ? null : status.ToString();
    internal static string? UnpairingError(DeviceUnpairingResultStatus status) =>
        status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired ? null : status.ToString();

    /// <summary>Requests a Bluetooth audio driver connection change. Other device types reconnect through Windows.</summary>
    public async Task<bool> SetAudioConnectionAsync(Guid container, bool connect)
    {
        var aqs = $"System.Devices.InterfaceClassGuid:=\"{{6994AD04-93EF-11D0-A3CC-00A0C9223196}}\" AND System.Devices.ContainerId:=\"{{{container}}}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var filters = await DeviceInformation.FindAllAsync(aqs, null, DeviceInformationKind.DeviceInterface);
        var any = false;
        foreach (var filter in filters)
            any |= KsOneShot(filter.Id, connect ? 0u : 1u);
        return any;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KSPROPERTY
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    private static unsafe bool KsOneShot(string path, uint property)
    {
        using var handle = CreateFileW(path, 0xC0000000, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return false;
        var request = new KSPROPERTY { Set = new Guid("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d"), Id = property, Flags = 1 };
        return DeviceIoControl(handle, 0x2F0003, &request, (uint)sizeof(KSPROPERTY), null, 0, out _, 0);
    }

    [LibraryImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothIsDiscoverable(nint radio);

    [LibraryImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothIsConnectable(nint radio);

    [LibraryImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothEnableDiscovery(nint radio, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothEnableIncomingConnections(nint radio, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, nint overlapped);

    internal static bool IsExpectedFailure(Exception e) => e is UnauthorizedAccessException or COMException
        or InvalidOperationException or NotSupportedException or ArgumentException or IOException;

    public void Dispose()
    {
        lock (_lock) _disposed = true;
        StopDiscovery();
    }
}
