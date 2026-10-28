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
    PendingCharge = 5,
    PendingDischarge = 6,
}

public enum PowerDeviceType
{
    Unknown = 0,
    LinePower = 1,
    Battery = 2,
    Ups = 3,
    Monitor = 4,
    Mouse = 5,
    Keyboard = 6,
    Pda = 7,
    Phone = 8,
    MediaPlayer = 9,
    Tablet = 10,
    Computer = 11,
    GamingInput = 12,
    Pen = 13,
    Touchpad = 14,
    Modem = 15,
    Network = 16,
    Headset = 17,
    Speakers = 18,
    Headphones = 19,
    Video = 20,
    OtherAudio = 21,
    RemoteControl = 22,
    Printer = 23,
    Scanner = 24,
    Camera = 25,
    Wearable = 26,
    Toy = 27,
    BluetoothGeneric = 28,
}

public enum PowerProfile
{
    PowerSaver = 0,
    Balanced = 1,
    Performance = 2,
}

public enum PerformanceDegradationReason
{
    None = 0,
    LapDetected = 1,
    HighTemperature = 2,
}

/// <summary>The <c>PowerDevice</c>: a battery, or the aggregate display device.</summary>
public sealed class PowerDevice : QObject
{
    private PowerDeviceInfo _info = PowerDeviceInfo.NoBattery;
    private bool _ready;

    [Qml] public PowerDeviceType Type { get { Track(); return _info.Type; } }

    [Qml] public bool PowerSupply { get { Track(); return _info.PowerSupply; } }

    [Qml] public double Energy { get { Track(); return _info.Energy; } }

    [Qml] public double EnergyCapacity { get { Track(); return _info.EnergyCapacity; } }

    [Qml] public double ChangeRate { get { Track(); return _info.ChangeRate; } }

    [Qml] public double TimeToEmpty { get { Track(); return _info.TimeToEmpty; } }

    [Qml] public double TimeToFull { get { Track(); return _info.TimeToFull; } }

    /// <summary>The charge, from 0 to 1 (UPower reports 0 to 100).</summary>
    [Qml] public double Percentage { get { Track(); return _info.Percentage; } }

    [Qml] public bool IsPresent { get { Track(); return _info.IsPresent; } }

    [Qml] public PowerDeviceState State { get { Track(); return _info.State; } }

    /// <summary>Full charge capacity over design capacity, from 0 to 100.</summary>
    [Qml] public double HealthPercentage { get { Track(); return _info.HealthPercentage; } }

    [Qml] public bool HealthSupported { get { Track(); return _info.HealthPercentage != 0; } }

    [Qml] public string IconName { get { Track(); return _info.IconName; } }

    [Qml] public bool IsLaptopBattery { get { Track(); return _info.Type == PowerDeviceType.Battery && _info.PowerSupply; } }

    [Qml] public string NativePath { get { Track(); return _info.NativePath; } }

    [Qml] public string Model { get { Track(); return _info.Model; } }

    [Qml] public bool Ready { get { Track(); return _ready; } }

    internal void Update(PowerDeviceInfo info, bool ready)
    {
        var old = _info;
        _info = info;
        Changed(old.Type != info.Type, "type");
        Changed(old.PowerSupply != info.PowerSupply, "powerSupply");
        Changed(old.Energy != info.Energy, "energy");
        Changed(old.EnergyCapacity != info.EnergyCapacity, "energyCapacity");
        Changed(old.ChangeRate != info.ChangeRate, "changeRate");
        Changed(old.TimeToEmpty != info.TimeToEmpty, "timeToEmpty");
        Changed(old.TimeToFull != info.TimeToFull, "timeToFull");
        Changed(old.Percentage != info.Percentage, "percentage");
        Changed(old.IsPresent != info.IsPresent, "isPresent");
        Changed(old.State != info.State, "state");
        Changed(old.HealthPercentage != info.HealthPercentage, "healthPercentage");
        Changed((old.HealthPercentage != 0) != (info.HealthPercentage != 0), "healthSupported");
        Changed(old.IconName != info.IconName, "iconName");
        Changed((old.Type == PowerDeviceType.Battery && old.PowerSupply) != (info.Type == PowerDeviceType.Battery && info.PowerSupply), "isLaptopBattery");
        Changed(old.NativePath != info.NativePath, "nativePath");
        Changed(old.Model != info.Model, "model");
        SetField(ref _ready, ready, nameof(Ready));
    }

    private void Changed(bool changed, string property)
    {
        if (changed)
            NotifyChanged(property);
    }

    public override string ToString() => $"PowerDevice({(_info.NativePath.Length > 0 ? _info.NativePath : "DisplayDevice")})";
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

        // Devices are matched by their native path, so an object stays the same while its battery does.
        var next = new List<PowerDevice>();
        foreach (var info in snapshot.Devices)
        {
            var device = _deviceObjects.FirstOrDefault(d => d.NativePath == info.NativePath && !next.Contains(d));
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

/// <summary>The <c>PowerProfiles</c> singleton, backed by the Windows power mode.</summary>
public sealed class PowerProfilesService : QObject
{
    private readonly IDisposable _subscription;
    private PowerProfile _profile = PowerProfile.Balanced;
    private bool _hasPerformanceProfile;

    internal PowerProfilesService(QmlEngine engine)
    {
        Apply(PowerProfilesBackend.Instance.Current);
        _subscription = PowerProfilesBackend.Instance.Subscribe(s => engine.Loop.Post(() => Apply(s)));
    }

    [Qml]
    public PowerProfile Profile
    {
        get { Track(); return _profile; }
        set
        {
            if (value == PowerProfile.Performance && !_hasPerformanceProfile)
            {
                ServiceSupport.Warn(this, "power", "Cannot set the profile to Performance, as the system has no performance profile");
                return;
            }
            if (!Enum.IsDefined(value))
            {
                ServiceSupport.Warn(this, "power", $"Cannot set the profile to {(int)value}, which is not a PowerProfile");
                return;
            }
            var error = PowerProfilesBackend.Instance.Set(value);
            if (error != 0)
                ServiceSupport.Warn(this, "power", $"Windows did not set the power mode (error {error}). The power mode only applies to the Balanced power plan.");
        }
    }

    /// <summary>Windows has the Best performance power mode wherever it has power modes at all.</summary>
    [Qml] public bool HasPerformanceProfile { get { Track(); return _hasPerformanceProfile; } }

    /// <summary>Windows does not say why it limits performance, so this is always None.</summary>
    [Qml] public PerformanceDegradationReason DegradationReason => PerformanceDegradationReason.None;

    /// <summary>Windows has no profile holds that other applications can see.</summary>
    [Qml] public IReadOnlyList<object> Holds => [];

    private void Apply(PowerProfileSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        SetField(ref _profile, snapshot.Profile, nameof(Profile));
        SetField(ref _hasPerformanceProfile, snapshot.Available, nameof(HasPerformanceProfile));
    }

    protected override void OnDestroy() => _subscription.Dispose();
}

internal static class PowerModule
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Power")
            .Singleton(e => new PowerService(e), "Power")
            .Singleton(e => new PowerProfilesService(e), "PowerProfiles")
            .Type<KeepAwake>("KeepAwake")
            .Type<IdleMonitor>("IdleMonitor")
            .Uncreatable<PowerDevice>("PowerDevice", "PowerDevices can only be acquired from Power")
            .Enum<PowerDeviceState>(s => s switch
            {
                PowerDeviceState.Unknown => "Unknown",
                PowerDeviceState.Charging => "Charging",
                PowerDeviceState.Discharging => "Discharging",
                PowerDeviceState.Empty => "Empty",
                PowerDeviceState.FullyCharged => "Fully Charged",
                PowerDeviceState.PendingCharge => "Pending Charge",
                PowerDeviceState.PendingDischarge => "Pending Discharge",
                _ => "Invalid Status",
            })
            .Enum<PowerDeviceType>(t => t switch
            {
                PowerDeviceType.LinePower => "Line Power",
                PowerDeviceType.MediaPlayer => "Media Player",
                PowerDeviceType.GamingInput => "Gaming Input",
                PowerDeviceType.OtherAudio => "Other Audio",
                PowerDeviceType.RemoteControl => "Remote Control",
                PowerDeviceType.BluetoothGeneric => "Bluetooth Generic",
                _ when Enum.IsDefined(t) => t.ToString(),
                _ => "Invalid Type",
            })
            .Enum<PowerProfile>(p => Enum.IsDefined(p) ? p.ToString() : "Invalid")
            .Enum<PerformanceDegradationReason>(r => r is PerformanceDegradationReason.LapDetected or PerformanceDegradationReason.HighTemperature ? r.ToString() : "Invalid");
    }
}
