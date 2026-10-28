using Windows.Devices.Enumeration;
using Windows.Foundation;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Bluetooth;

public enum BluetoothAdapterState
{
    Disabled = 0,
    Enabled = 1,
    Enabling = 2,
    Disabling = 3,
    Blocked = 4,
}

public enum BluetoothDeviceState
{
    Disconnected = 0,
    Connected = 1,
    Disconnecting = 2,
    Connecting = 3,
}

internal static class BtLog
{
    public const string Category = "bluetooth";
}

/// <summary>The <c>BluetoothAdapter</c>: the PC's Bluetooth radio.</summary>
public sealed class BluetoothAdapter : QObject
{
    private readonly IEventLoop _loop;
    private BluetoothAdapterInfo _info;
    private BluetoothAdapterState? _pending;

    internal BluetoothAdapter(BluetoothAdapterInfo info, IEventLoop loop)
    {
        _info = info;
        _loop = loop;
        Devices.SetOwner(this);
    }

    [Qml] public string Name { get { Track(); return _info.Name; } }

    /// <summary>Windows has no BlueZ, so there is no D-Bus path.</summary>
    [Qml] public string DbusPath => "";

    /// <summary>BlueZ calls its first adapter hci0; so does this, as Windows has one adapter at a time.</summary>
    [Qml] public string AdapterId => "hci0";

    [Qml] public BluetoothAdapterState State { get { Track(); return _pending ?? _info.State; } }

    [Qml]
    public bool Enabled
    {
        get { Track(); return State == BluetoothAdapterState.Enabled; }
        set
        {
            if (value == (_info.State == BluetoothAdapterState.Enabled))
                return;
            SetPending(value ? BluetoothAdapterState.Enabling : BluetoothAdapterState.Disabling);
            ServiceSupport.Run(BtLog.Category, async () =>
            {
                var ok = await BluetoothBackend.Instance.SetEnabledAsync(value).ConfigureAwait(false);
                _loop.Post(() =>
                {
                    SetPending(null);
                    if (!ok)
                        ServiceSupport.Warn(this, BtLog.Category, "Windows did not let the shell turn Bluetooth " + (value ? "on" : "off"));
                });
            });
        }
    }

    [Qml]
    public bool Discovering
    {
        get { Track(); return _info.Discovering; }
        set
        {
            if (value && _info.State != BluetoothAdapterState.Enabled)
            {
                ServiceSupport.Warn(this, BtLog.Category, "Cannot discover devices while the adapter is off");
                return;
            }
            BluetoothBackend.Instance.SetDiscovering(value);
        }
    }

    /// <summary>Windows makes the PC discoverable only while Bluetooth settings are open.</summary>
    [Qml]
    public bool Discoverable
    {
        get => false;
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows makes the PC discoverable only while Bluetooth settings are open");
    }

    [Qml]
    public int DiscoverableTimeout
    {
        get => 0;
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows has no discoverable timeout");
    }

    /// <summary>Windows always accepts incoming pairing requests, through its own prompt.</summary>
    [Qml]
    public bool Pairable
    {
        get => true;
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows cannot stop accepting pairing requests");
    }

    [Qml]
    public int PairableTimeout
    {
        get => 0;
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows has no pairable timeout");
    }

    [Qml] public ObjectModel Devices { get; } = new();

    internal void Update(BluetoothAdapterInfo info)
    {
        var oldState = State;
        var old = _info;
        _info = info;
        if (old.Name != info.Name)
            NotifyChanged("name");
        if (old.Discovering != info.Discovering)
            NotifyChanged("discovering");
        StateUpdated(oldState);
    }

    private void SetPending(BluetoothAdapterState? state)
    {
        var old = State;
        _pending = state;
        StateUpdated(old);
    }

    private void StateUpdated(BluetoothAdapterState oldState)
    {
        if (State == oldState)
            return;
        NotifyChanged("state");
        if ((State == BluetoothAdapterState.Enabled) != (oldState == BluetoothAdapterState.Enabled))
            NotifyChanged("enabled");
    }

    public override string ToString() => $"BluetoothAdapter({_info.Name})";
}

/// <summary>The <c>BluetoothDevice</c>.</summary>
public sealed class BluetoothDevice : QObject
{
    private readonly IEventLoop _loop;
    private BluetoothDeviceInfo _info;
    private BluetoothDeviceState? _pending;
    private IAsyncOperation<DevicePairingResult>? _pairing;

    internal BluetoothDevice(BluetoothDeviceInfo info, BluetoothAdapter adapter, IEventLoop loop)
    {
        _info = info;
        Adapter = adapter;
        _loop = loop;
    }

    internal string Id => _info.Id;

    [Qml] public BluetoothAdapter Adapter { get; }

    [Qml] public string Address { get { Track(); return _info.Address; } }

    [Qml] public string DeviceName { get { Track(); return _info.Name; } }

    /// <summary>Windows shows one name, which renaming in Settings changes; it cannot be set from here.</summary>
    [Qml]
    public string Name
    {
        get { Track(); return _info.Name; }
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows cannot rename a Bluetooth device from the shell");
    }

    [Qml] public string Icon { get { Track(); return _info.Icon; } }

    [Qml] public string DbusPath => "";

    [Qml] public bool Paired { get { Track(); return _info.Paired; } }

    [Qml] public bool Bonded { get { Track(); return _info.Paired; } }

    [Qml] public bool Pairing { get { Track(); return _pairing is not null; } }

    /// <summary>Windows lets every paired device reconnect on its own.</summary>
    [Qml]
    public bool Trusted
    {
        get { Track(); return _info.Paired; }
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows trusts every paired device");
    }

    [Qml]
    public bool Blocked
    {
        get => false;
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows cannot block a Bluetooth device");
    }

    [Qml]
    public bool WakeAllowed
    {
        get => false;
        set => ServiceSupport.Warn(this, BtLog.Category, "Windows sets wake from a device in Device Manager");
    }

    [Qml] public BluetoothDeviceState State { get { Track(); return _pending ?? (_info.Connected ? BluetoothDeviceState.Connected : BluetoothDeviceState.Disconnected); } }

    [Qml]
    public bool Connected
    {
        get { Track(); return _info.Connected; }
        set
        {
            if (value == _info.Connected)
                return;
            if (value)
                Connect();
            else
                Disconnect();
        }
    }

    [Qml] public bool BatteryAvailable { get { Track(); return _info.Battery is not null; } }

    [Qml] public double Battery { get { Track(); return _info.Battery ?? 0; } }

    internal void Update(BluetoothDeviceInfo info)
    {
        var oldState = State;
        var old = _info;
        _info = info;
        if (old.Address != info.Address)
            NotifyChanged("address");
        if (old.Name != info.Name)
        {
            NotifyChanged("name");
            NotifyChanged("deviceName");
        }
        if (old.Icon != info.Icon)
            NotifyChanged("icon");
        if (old.Paired != info.Paired)
        {
            NotifyChanged("paired");
            NotifyChanged("bonded");
            NotifyChanged("trusted");
        }
        if (old.Connected != info.Connected)
        {
            // A state Windows reports ends what this device was waiting for.
            _pending = null;
            NotifyChanged("connected");
        }
        if ((old.Battery is null) != (info.Battery is null))
            NotifyChanged("batteryAvailable");
        if (old.Battery != info.Battery)
            NotifyChanged("battery");
        if (State != oldState)
            NotifyChanged("state");
    }

    private void SetPending(BluetoothDeviceState? state)
    {
        var old = State;
        _pending = state;
        if (State != old)
            NotifyChanged("state");
    }

    [Qml("connect")]
    public void Connect()
    {
        if (_info.Connected)
        {
            ServiceSupport.Warn(this, BtLog.Category, $"Device {this} is already connected");
            return;
        }
        if (_pending == BluetoothDeviceState.Connecting)
        {
            ServiceSupport.Warn(this, BtLog.Category, $"Device {this} is already connecting");
            return;
        }
        SetConnection(true);
    }

    [Qml]
    public void Disconnect()
    {
        if (!_info.Connected)
        {
            ServiceSupport.Warn(this, BtLog.Category, $"Device {this} is already disconnected");
            return;
        }
        if (_pending == BluetoothDeviceState.Disconnecting)
        {
            ServiceSupport.Warn(this, BtLog.Category, $"Device {this} is already disconnecting");
            return;
        }
        SetConnection(false);
    }

    private void SetConnection(bool connect)
    {
        if (_info.Container is not { } container || !_info.Paired)
        {
            ServiceSupport.Warn(this, BtLog.Category, $"Windows can only {(connect ? "connect" : "disconnect")} paired audio devices; {this} is not one");
            return;
        }
        SetPending(connect ? BluetoothDeviceState.Connecting : BluetoothDeviceState.Disconnecting);
        ServiceSupport.Run(BtLog.Category, async () =>
        {
            var ok = false;
            try
            {
                ok = await BluetoothBackend.Instance.SetAudioConnectionAsync(container, connect).ConfigureAwait(false);
            }
            finally
            {
                _loop.Post(() =>
                {
                    if (!ok)
                    {
                        SetPending(null);
                        ServiceSupport.Warn(this, BtLog.Category, $"Windows can only {(connect ? "connect" : "disconnect")} Bluetooth audio devices; {this} did not respond");
                    }
                    else
                    {
                        // Windows reports the new state when the device has made it; until then this stays pending.
                        _ = _loop.Schedule(TimeSpan.FromSeconds(15), () => SetPending(null));
                    }
                });
            }
        });
    }

    [Qml]
    public void Pair()
    {
        if (_info.Paired || _pairing is not null)
            return;
        var id = _info.Id;
        ServiceSupport.Run(BtLog.Category, async () =>
        {
            string? error = "the pairing failed";
            try
            {
                error = await BluetoothBackend.Instance.PairAsync(id, operation => _loop.Post(() =>
                {
                    _pairing = operation;
                    NotifyChanged("pairing");
                })).ConfigureAwait(false);
            }
            finally
            {
                _loop.Post(() =>
                {
                    _pairing = null;
                    NotifyChanged("pairing");
                    if (error is not null)
                        ServiceSupport.Warn(this, BtLog.Category, $"Could not pair {this}: {error}");
                });
            }
        });
    }

    [Qml]
    public void CancelPair()
    {
        _pairing?.Cancel();
    }

    [Qml]
    public void Forget()
    {
        var id = _info.Id;
        ServiceSupport.Run(BtLog.Category, async () =>
        {
            var error = await BluetoothBackend.Instance.ForgetAsync(id).ConfigureAwait(false);
            if (error is not null)
                _loop.Post(() => ServiceSupport.Warn(this, BtLog.Category, $"Could not forget {this}: {error}"));
        });
    }

    public override string ToString() => $"BluetoothDevice({_info.Address}, {_info.Name})";
}

/// <summary>The <c>Bluetooth</c> singleton.</summary>
public sealed class BluetoothService : QObject
{
    private readonly IEventLoop _loop;
    private readonly IDisposable _subscription;
    private readonly Dictionary<string, BluetoothDevice> _devices = new(StringComparer.Ordinal);
    private BluetoothAdapter? _adapter;

    internal BluetoothService(QmlEngine engine)
    {
        _loop = engine.Loop;
        Adapters.SetOwner(this);
        Devices.SetOwner(this);
        Apply(BluetoothBackend.Instance.Current);
        _subscription = BluetoothBackend.Instance.Subscribe(s => _loop.Post(() => Apply(s)));
    }

    [Qml] public BluetoothAdapter? DefaultAdapter { get { Track(); return _adapter; } }

    [Qml] public ObjectModel Adapters { get; } = new();

    [Qml] public ObjectModel Devices { get; } = new();

    private void Apply(BluetoothSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        if (snapshot.Adapter is null)
        {
            if (_adapter is not null)
            {
                Devices.Set([]);
                Adapters.Set([]);
                _devices.Clear();
                _adapter.Destroy();
                _adapter = null;
                NotifyChanged("defaultAdapter");
            }
            return;
        }
        if (_adapter is null)
        {
            _adapter = new BluetoothAdapter(snapshot.Adapter, _loop);
            _adapter.SetOwner(this);
            Adapters.Set([_adapter]);
            NotifyChanged("defaultAdapter");
        }
        _adapter.Update(snapshot.Adapter);

        var next = new List<object?>();
        foreach (var info in snapshot.Devices)
        {
            if (!_devices.TryGetValue(info.Id, out var device))
            {
                device = _devices[info.Id] = new BluetoothDevice(info, _adapter, _loop);
                device.SetOwner(_adapter);
            }
            device.Update(info);
            next.Add(device);
        }
        foreach (var gone in _devices.Where(kv => !next.Contains(kv.Value)).ToList())
        {
            _devices.Remove(gone.Key);
            gone.Value.Destroy();
        }
        Devices.Set(next);
        _adapter.Devices.Set(next);
    }

    protected override void OnDestroy() => _subscription.Dispose();
}

internal static class BluetoothModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Bluetooth")
            .Singleton(e => new BluetoothService(e), "Bluetooth")
            .Uncreatable<BluetoothAdapter>("BluetoothAdapter")
            .Uncreatable<BluetoothDevice>("BluetoothDevice")
            .Enum<BluetoothAdapterState>(s => Enum.IsDefined(s) ? s.ToString() : "Unknown")
            .Enum<BluetoothDeviceState>(s => Enum.IsDefined(s) ? s.ToString() : "Unknown");
    }
}
