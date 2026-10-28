using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation;
using WinAdapter = Windows.Devices.Bluetooth.BluetoothAdapter;
using WinClassicDevice = Windows.Devices.Bluetooth.BluetoothDevice;
using WinLeDevice = Windows.Devices.Bluetooth.BluetoothLEDevice;

namespace Winshell.Platform.Windows.Services.Bluetooth;

internal sealed record BluetoothAdapterInfo(string Id, string Name, string Address, BluetoothAdapterState State, bool Discovering);

internal sealed record BluetoothDeviceInfo(
    string Id,
    string Address,
    string Name,
    bool Connected,
    bool Paired,
    double? Battery,
    string Icon,
    Guid? Container);

internal sealed record BluetoothSnapshot(bool Ready, BluetoothAdapterInfo? Adapter, IReadOnlyList<BluetoothDeviceInfo> Devices)
{
    public static readonly BluetoothSnapshot Initial = new(false, null, []);
}

/// <summary>
/// The Bluetooth adapter and devices of this PC. Windows has one Bluetooth adapter at a time. Paired
/// devices come from device watchers over the paired association endpoints (classic and LE); while
/// discovering, more watchers add the unpaired devices in range. Battery levels are the Bluetooth
/// battery property Windows puts on a device's device nodes.
/// </summary>
internal sealed partial class BluetoothBackend : SnapshotSource<BluetoothSnapshot>
{
    public static readonly BluetoothBackend Instance = new();

    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string PairedKey = "System.Devices.Aep.IsPaired";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly string[] EndpointProperties = [AddressKey, ConnectedKey, PairedKey, ContainerKey];

    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceInformation> _endpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (double? Level, DateTime Read)> _batteries = [];
    private readonly List<DeviceWatcher> _paired = [];
    private readonly List<DeviceWatcher> _discovery = [];
    private WinAdapter? _adapter;
    private string _adapterName = "";
    private Radio? _radio;

    private BluetoothBackend()
        : base(BluetoothSnapshot.Initial)
    {
    }

    protected override string Category => "quickshell.bluetooth";

    protected override async Task StartAsync()
    {
        var adapter = await WinAdapter.GetDefaultAsync();
        if (adapter is null)
        {
            await RefreshNowAsync().ConfigureAwait(false);
            return;
        }
        var name = (await DeviceInformation.CreateFromIdAsync(adapter.DeviceId))?.Name ?? "Bluetooth";
        Radio? radio = null;
        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
                radio = await adapter.GetRadioAsync();
        }
        catch (UnauthorizedAccessException)
        {
        }
        if (radio is not null)
            radio.StateChanged += (_, _) => Refresh();
        lock (_lock)
        {
            _adapter = adapter;
            _adapterName = name;
            _radio = radio;
        }

        lock (_lock)
        {
            StartWatcher(WinClassicDevice.GetDeviceSelectorFromPairingState(true), _paired);
            StartWatcher(WinLeDevice.GetDeviceSelectorFromPairingState(true), _paired);
        }
        await RefreshNowAsync().ConfigureAwait(false);

        // Battery levels change without an event this code can see; read them again now and then.
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            Refresh();
        }
    }

    private void StartWatcher(string selector, List<DeviceWatcher> into)
    {
        var watcher = DeviceInformation.CreateWatcher(selector, EndpointProperties, DeviceInformationKind.AssociationEndpoint);
        watcher.Added += (_, info) =>
        {
            lock (_lock)
                _endpoints[info.Id] = info;
            ServiceSupport.Run(Category, () => ReadIconAsync(info.Id));
            Refresh();
        };
        watcher.Updated += (_, update) =>
        {
            lock (_lock)
            {
                if (_endpoints.TryGetValue(update.Id, out var info))
                    info.Update(update);
            }
            Refresh();
        };
        watcher.Removed += (_, update) =>
        {
            lock (_lock)
                _endpoints.Remove(update.Id);
            Refresh();
        };
        into.Add(watcher);
        watcher.Start();
    }

    /// <summary>The device's icon, from its class of device (classic) or appearance (LE), as BlueZ names them.</summary>
    private async Task ReadIconAsync(string id)
    {
        string icon;
        if (id.StartsWith("BluetoothLE#", StringComparison.Ordinal))
        {
            using var device = await WinLeDevice.FromIdAsync(id);
            icon = device is null ? "" : AppearanceIcon(device.Appearance.RawValue);
        }
        else
        {
            using var device = await WinClassicDevice.FromIdAsync(id);
            icon = device is null ? "" : ClassIcon(device.ClassOfDevice.RawValue);
        }
        lock (_lock)
            _icons[id] = icon;
        Refresh();
    }

    protected override async Task<BluetoothSnapshot> ReadAsync()
    {
        WinAdapter? adapter;
        Radio? radio;
        string adapterName;
        bool discovering;
        List<DeviceInformation> endpoints;
        Dictionary<string, string> icons;
        lock (_lock)
        {
            adapter = _adapter;
            radio = _radio;
            adapterName = _adapterName;
            discovering = _discovery.Count > 0;
            endpoints = [.. _endpoints.Values];
            icons = new Dictionary<string, string>(_icons, StringComparer.Ordinal);
        }
        if (adapter is null)
            return new BluetoothSnapshot(true, null, []);

        var state = radio?.State switch
        {
            RadioState.On => BluetoothAdapterState.Enabled,
            RadioState.Disabled => BluetoothAdapterState.Blocked,
            RadioState.Off => BluetoothAdapterState.Disabled,
            // Without access to the radio, an adapter Windows reports is taken as on.
            _ => BluetoothAdapterState.Enabled,
        };
        var adapterInfo = new BluetoothAdapterInfo(adapter.DeviceId, adapterName, ServiceSupport.FormatMac(adapter.BluetoothAddress), state, discovering);

        // A dual-mode device has a classic and an LE endpoint with one address: it is one device.
        var devices = new List<BluetoothDeviceInfo>();
        foreach (var group in endpoints.GroupBy(e => Address(e) ?? e.Id, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.OrderBy(e => e.Id.StartsWith("BluetoothLE#", StringComparison.Ordinal)).First();
            var address = (Address(first) ?? "").ToUpperInvariant();
            var connected = group.Any(e => Bool(e, ConnectedKey));
            var paired = group.Any(e => Bool(e, PairedKey) || e.Pairing.IsPaired);
            var container = group.Select(e => e.Properties.TryGetValue(ContainerKey, out var c) && c is Guid g ? g : (Guid?)null).FirstOrDefault(g => g is not null);
            var name = group.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? address.Replace(':', '-');
            var icon = group.Select(e => icons.GetValueOrDefault(e.Id)).FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? "";
            var battery = connected && container is { } id ? await BatteryAsync(id).ConfigureAwait(false) : null;
            devices.Add(new BluetoothDeviceInfo(first.Id, address, name, connected, paired, battery, icon, container));
        }
        return new BluetoothSnapshot(true, adapterInfo, devices);
    }

    private static string? Address(DeviceInformation info) =>
        info.Properties.TryGetValue(AddressKey, out var a) && a is string s && s.Length > 0 ? s : null;

    private static bool Bool(DeviceInformation info, string key) =>
        info.Properties.TryGetValue(key, out var v) && v is true;

    /// <summary>The battery level Windows shows in Settings, from 0 to 1, read at most every 30 seconds.</summary>
    private async Task<double?> BatteryAsync(Guid container)
    {
        lock (_lock)
        {
            if (_batteries.TryGetValue(container, out var cached) && DateTime.UtcNow - cached.Read < TimeSpan.FromSeconds(30))
                return cached.Level;
        }
        double? level = null;
        try
        {
            var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"", [BatteryKey], DeviceInformationKind.Device);
            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue(BatteryKey, out var value) && value is not null)
                {
                    level = Math.Clamp(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) / 100, 0, 1);
                    break;
                }
            }
        }
        catch (ArgumentException)
        {
        }
        lock (_lock)
            _batteries[container] = (level, DateTime.UtcNow);
        return level;
    }

    // Actions.

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        Radio? radio;
        lock (_lock)
            radio = _radio;
        if (radio is null)
            return false;
        var ok = await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        Refresh();
        return ok;
    }

    /// <summary>Starts or stops looking for unpaired devices in range.</summary>
    public void SetDiscovering(bool discovering)
    {
        List<DeviceWatcher> stopped = [];
        lock (_lock)
        {
            if (_adapter is null || discovering == _discovery.Count > 0)
                return;
            if (discovering)
            {
                StartWatcher(WinClassicDevice.GetDeviceSelectorFromPairingState(false), _discovery);
                StartWatcher(WinLeDevice.GetDeviceSelectorFromPairingState(false), _discovery);
            }
            else
            {
                stopped.AddRange(_discovery);
                _discovery.Clear();
                // Unpaired devices found while discovering go when discovering stops.
                foreach (var id in _endpoints.Where(kv => !Bool(kv.Value, PairedKey) && !Bool(kv.Value, ConnectedKey)).Select(kv => kv.Key).ToList())
                    _endpoints.Remove(id);
            }
        }
        foreach (var watcher in stopped)
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }
        Refresh();
    }

    public async Task<string?> PairAsync(string id, Action<IAsyncOperation<DevicePairingResult>> started)
    {
        var info = await DeviceInformation.CreateFromIdAsync(id);
        if (info.Pairing.IsPaired)
            return null;
        if (!info.Pairing.CanPair)
            return "the device cannot be paired";
        // Without a pairing agent to show a PIN, pairing accepts the confirmations Windows asks for.
        var custom = info.Pairing.Custom;
        custom.PairingRequested += (_, args) =>
        {
            if (args.PairingKind is DevicePairingKinds.ConfirmPinMatch or DevicePairingKinds.DisplayPin)
                Winshell.Runtime.Log.Default.Info(Category, $"Pairing {info.Name} with PIN {args.Pin}");
            args.Accept();
        };
        var operation = custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin);
        started(operation);
        var result = await operation;
        Refresh();
        return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired ? null : result.Status.ToString();
    }

    public async Task<string?> ForgetAsync(string id)
    {
        var info = await DeviceInformation.CreateFromIdAsync(id);
        var result = await info.Pairing.UnpairAsync();
        Refresh();
        return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired ? null : result.Status.ToString();
    }

    /// <summary>
    /// Connects or disconnects a Bluetooth audio device, as the Connect button in Settings does: a one-shot
    /// reconnect or disconnect request to the device's kernel streaming audio filters. Windows has no API to
    /// connect other kinds of devices; they connect on their own when they are paired and in range.
    /// </summary>
    public async Task<bool> SetAudioConnectionAsync(Guid container, bool connect)
    {
        var aqs = $"System.Devices.InterfaceClassGuid:=\"{{6994AD04-93EF-11D0-A3CC-00A0C9223196}}\" AND System.Devices.ContainerId:=\"{{{container}}}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var filters = await DeviceInformation.FindAllAsync(aqs, null, DeviceInformationKind.DeviceInterface);
        var any = false;
        foreach (var filter in filters)
            any |= KsOneShot(filter.Id, connect ? 0u : 1u);
        Refresh();
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
        if (handle.IsInvalid)
            return false;
        // KSPROPSETID_BtAudio, KSPROPERTY_ONESHOT_RECONNECT (0) or _DISCONNECT (1), KSPROPERTY_TYPE_GET.
        var request = new KSPROPERTY { Set = new Guid("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d"), Id = property, Flags = 1 };
        return DeviceIoControl(handle, 0x2F0003, &request, (uint)sizeof(KSPROPERTY), null, 0, out _, 0);
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, nint overlapped);

    // BlueZ's icon names (src/dbus-common.c, class_to_icon and gap_appearance_to_icon).

    private static string ClassIcon(uint cls)
    {
        switch ((cls & 0x1f00) >> 8)
        {
            case 0x01:
                return "computer";
            case 0x02:
                return ((cls & 0xfc) >> 2) switch
                {
                    0x01 or 0x02 or 0x03 or 0x05 => "phone",
                    0x04 => "modem",
                    _ => "",
                };
            case 0x03:
                return "network-wireless";
            case 0x04:
                return ((cls & 0xfc) >> 2) switch
                {
                    0x01 or 0x02 => "audio-headset",
                    0x06 => "audio-headphones",
                    0x0b or 0x0c or 0x0d => "camera-video",
                    _ => "audio-card",
                };
            case 0x05:
                switch ((cls & 0xc0) >> 6)
                {
                    case 0x00:
                        return ((cls & 0x1e) >> 2) is 0x01 or 0x02 ? "input-gaming" : "";
                    case 0x01:
                        return "input-keyboard";
                    case 0x02:
                        return ((cls & 0x1e) >> 2) == 0x05 ? "input-tablet" : "input-mouse";
                }
                return "";
            case 0x06:
                if ((cls & 0x80) != 0)
                    return "printer";
                if ((cls & 0x20) != 0)
                    return "camera-photo";
                return "";
            default:
                return "";
        }
    }

    private static string AppearanceIcon(ushort appearance)
    {
        switch ((appearance & 0xffc0) >> 6)
        {
            case 0x01:
                return "phone";
            case 0x02:
                return "computer";
            case 0x05:
                return "video-display";
            case 0x0a:
                return "multimedia-player";
            case 0x0b:
                return "scanner";
            case 0x0f:
                return (appearance & 0x3f) switch
                {
                    0x01 => "input-keyboard",
                    0x02 => "input-mouse",
                    0x03 or 0x04 => "input-gaming",
                    0x05 => "input-tablet",
                    0x08 => "scanner",
                    _ => "",
                };
            default:
                return "";
        }
    }
}
