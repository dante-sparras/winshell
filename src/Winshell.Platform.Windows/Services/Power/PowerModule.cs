using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Power;

public enum PowerDeviceState
{
    Unknown = 0,
    Charging = 1,
    Discharging = 2,
    Empty = 3,
    FullyCharged = 4,
    NotCharging = 5,
}

public enum PowerDeviceType
{
    Unknown = 0,
    Battery = 2,
}

/// <summary>The three positions of the Windows power mode slider.</summary>
public enum PowerModeLevel
{
    BestEfficiency = 0,
    Balanced = 1,
    BestPerformance = 2,
}

/// <summary>The power mode Windows is applying, including energy saver and game mode.</summary>
public enum EffectivePowerMode
{
    EnergySaver = 0,
    BetterBattery = 1,
    Balanced = 2,
    HighPerformance = 3,
    MaxPerformance = 4,
    GameMode = 5,
    MixedReality = 6,
}

/// <summary>The <c>PowerDevice</c>: a battery, or the aggregate display device.</summary>
public sealed class PowerDevice : QObject
{
    private PowerDeviceInfo _info = PowerDeviceInfo.NoBattery;
    private bool _ready;

    [Qml] public PowerDeviceType Type { get { Track(); return _info.Type; } }

    [Qml] public bool PowersTheSystem { get { Track(); return _info.PowersTheSystem; } }

    /// <summary>Remaining capacity, in watt-hours.</summary>
    [Qml] public double RemainingCapacity { get { Track(); return _info.RemainingCapacity; } }

    /// <summary>Full-charge capacity, in watt-hours.</summary>
    [Qml] public double FullChargeCapacity { get { Track(); return _info.FullChargeCapacity; } }

    /// <summary>Charge rate, in watts. Negative while discharging.</summary>
    [Qml] public double ChargeRate { get { Track(); return _info.ChargeRate; } }

    [Qml] public double TimeToEmpty { get { Track(); return _info.TimeToEmpty; } }

    [Qml] public double TimeToFull { get { Track(); return _info.TimeToFull; } }

    /// <summary>The charge, from 0 to 1.</summary>
    [Qml] public double Percentage { get { Track(); return _info.Percentage; } }

    [Qml] public bool IsPresent { get { Track(); return _info.IsPresent; } }

    [Qml] public PowerDeviceState State { get { Track(); return _info.State; } }

    /// <summary>Full charge capacity over design capacity, from 0 to 100.</summary>
    [Qml] public double HealthPercentage { get { Track(); return _info.HealthPercentage; } }

    [Qml] public bool HealthSupported { get { Track(); return _info.HealthPercentage != 0; } }

    [Qml] public string IconName { get { Track(); return _info.IconName; } }

    [Qml] public bool IsSystemBattery { get { Track(); return _info.Type == PowerDeviceType.Battery && _info.PowersTheSystem; } }

    [Qml] public string DeviceId { get { Track(); return _info.DeviceId; } }

    [Qml] public string Model { get { Track(); return _info.Model; } }

    [Qml] public bool Ready { get { Track(); return _ready; } }

    internal void Update(PowerDeviceInfo info, bool ready)
    {
        var old = _info;
        _info = info;
        Changed(old.Type != info.Type, "type");
        Changed(old.PowersTheSystem != info.PowersTheSystem, "powersTheSystem");
        Changed(old.RemainingCapacity != info.RemainingCapacity, "remainingCapacity");
        Changed(old.FullChargeCapacity != info.FullChargeCapacity, "fullChargeCapacity");
        Changed(old.ChargeRate != info.ChargeRate, "chargeRate");
        Changed(old.TimeToEmpty != info.TimeToEmpty, "timeToEmpty");
        Changed(old.TimeToFull != info.TimeToFull, "timeToFull");
        Changed(old.Percentage != info.Percentage, "percentage");
        Changed(old.IsPresent != info.IsPresent, "isPresent");
        Changed(old.State != info.State, "state");
        Changed(old.HealthPercentage != info.HealthPercentage, "healthPercentage");
        Changed((old.HealthPercentage != 0) != (info.HealthPercentage != 0), "healthSupported");
        Changed(old.IconName != info.IconName, "iconName");
        Changed((old.Type == PowerDeviceType.Battery && old.PowersTheSystem) != (info.Type == PowerDeviceType.Battery && info.PowersTheSystem), "isSystemBattery");
        Changed(old.DeviceId != info.DeviceId, "deviceId");
        Changed(old.Model != info.Model, "model");
        SetField(ref _ready, ready, nameof(Ready));
    }

    private void Changed(bool changed, string property)
    {
        if (changed)
            NotifyChanged(property);
    }

    public override string ToString() => $"PowerDevice({(_info.DeviceId.Length > 0 ? _info.DeviceId : "DisplayDevice")})";
}

/// <summary>The <c>Power</c> singleton, backed by the batteries Windows reports.</summary>
public sealed class PowerService : QObject
{
    private readonly IDisposable _subscription;
    private readonly ObjectModel _devices = new();
    private readonly List<PowerDevice> _deviceObjects = [];
    private bool _onBattery;

    internal PowerService(QmlEngine engine)
    {
        DisplayDevice.SetOwner(this);
        _devices.SetOwner(this);
        Apply(PowerBackend.Instance.Current);
        _subscription = PowerBackend.Instance.Subscribe(s => engine.Loop.Post(() => Apply(s)));
    }

    [Qml] public PowerDevice DisplayDevice { get; } = new();

    [Qml] public ObjectModel Devices => _devices;

    [Qml] public bool OnBattery { get { Track(); return _onBattery; } }

    private void Apply(PowerSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        DisplayDevice.Update(snapshot.Display, snapshot.Ready);
        SetField(ref _onBattery, snapshot.OnBattery, nameof(OnBattery));

        var next = new List<PowerDevice>();
        foreach (var info in snapshot.Devices)
        {
            var device = _deviceObjects.FirstOrDefault(d => d.DeviceId == info.DeviceId && !next.Contains(d));
            if (device is null)
            {
                device = new PowerDevice();
                device.SetOwner(this);
            }
            device.Update(info, true);
            next.Add(device);
        }
        foreach (var gone in _deviceObjects.Except(next).ToList())
            gone.Destroy();
        _deviceObjects.Clear();
        _deviceObjects.AddRange(next);
        _devices.Set([.. next]);
    }

    protected override void OnDestroy() => _subscription.Dispose();
}

/// <summary>The <c>PowerMode</c> singleton. Assigning <c>mode</c> changes the slider for the supply in use.</summary>
public sealed class PowerModeService : QObject
{
    private readonly IDisposable _subscription;
    private PowerModeLevel _mode = PowerModeLevel.Balanced;
    private EffectivePowerMode _effective = EffectivePowerMode.Balanced;
    private bool _available;
    private bool _ready;

    internal PowerModeService(QmlEngine engine)
    {
        Apply(PowerModeBackend.Instance.Current);
        _subscription = PowerModeBackend.Instance.Subscribe(s => engine.Loop.Post(() => Apply(s)));
    }

    [Qml]
    public PowerModeLevel Mode
    {
        get { Track(); return _mode; }
        set
        {
            if (!Enum.IsDefined(value))
            {
                ServiceSupport.Warn(this, "power", $"Cannot set the power mode to {(int)value}, which is not a PowerModeLevel.");
                return;
            }
            var error = PowerModeBackend.Instance.Set(value);
            if (error != 0)
                ServiceSupport.Warn(this, "power", $"Windows did not set the power mode (error {error}).");
        }
    }

    [Qml] public EffectivePowerMode Effective { get { Track(); return _effective; } }

    /// <summary>The public power-mode API answered. When this is false, <c>mode</c> stays Balanced.</summary>
    [Qml] public bool Available { get { Track(); return _available; } }

    [Qml] public bool Ready { get { Track(); return _ready; } }

    private void Apply(PowerModeSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        SetField(ref _mode, snapshot.Mode, nameof(Mode));
        SetField(ref _effective, snapshot.Effective, nameof(Effective));
        SetField(ref _available, snapshot.Available, nameof(Available));
        SetField(ref _ready, snapshot.Ready, nameof(Ready));
    }

    protected override void OnDestroy() => _subscription.Dispose();
}

internal static class PowerModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Power")
            .Singleton(e => new PowerService(e), "Power")
            .Singleton(e => new PowerModeService(e), "PowerMode")
            .Singleton(e => new BrightnessService(e), "Brightness")
            .Type<KeepAwake>("KeepAwake")
            .Type<IdleMonitor>("IdleMonitor")
            .Uncreatable<PowerDevice>("PowerDevice", "PowerDevices can only be acquired from Power")
            .Uncreatable<BrightnessMonitor>("BrightnessMonitor", "BrightnessMonitors can only be acquired from Brightness")
            .Enum<PowerDeviceState>(s => s switch
            {
                PowerDeviceState.Unknown => "Unknown",
                PowerDeviceState.Charging => "Charging",
                PowerDeviceState.Discharging => "Discharging",
                PowerDeviceState.Empty => "Empty",
                PowerDeviceState.FullyCharged => "Fully Charged",
                PowerDeviceState.NotCharging => "Not Charging",
                _ => "Invalid Status",
            })
            .Enum<PowerDeviceType>(t => Enum.IsDefined(t) ? t.ToString() : "Invalid Type")
            .Enum<PowerModeLevel>(m => Enum.IsDefined(m) ? m.ToString() : "Invalid")
            .Enum<EffectivePowerMode>(m => Enum.IsDefined(m) ? m.ToString() : "Invalid")
            .Enum<BrightnessConnection>(c => c switch
            {
                BrightnessConnection.Internal => "Internal",
                BrightnessConnection.External => "External",
                _ => "Invalid",
            });
    }
}
