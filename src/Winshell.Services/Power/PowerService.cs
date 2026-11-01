using System.Collections.ObjectModel;

namespace Winshell.Power;

/// <summary>A battery, or the aggregate display device.</summary>
public sealed class PowerDevice : ObservableObject
{
    private PowerDeviceInfo _info = PowerDeviceInfo.NoBattery;
    private bool _ready;

    internal PowerDevice()
    {
    }

    public PowerDeviceType Type => _info.Type;

    public bool PowersTheSystem => _info.PowersTheSystem;

    /// <summary>Remaining capacity, in watt-hours.</summary>
    public double RemainingCapacity => _info.RemainingCapacity;

    /// <summary>Full-charge capacity, in watt-hours.</summary>
    public double FullChargeCapacity => _info.FullChargeCapacity;

    /// <summary>Charge rate, in watts. Negative while discharging.</summary>
    public double ChargeRate => _info.ChargeRate;

    public double TimeToEmpty => _info.TimeToEmpty;

    public double TimeToFull => _info.TimeToFull;

    /// <summary>The charge, from 0 to 1.</summary>
    public double Percentage => _info.Percentage;

    public bool IsPresent => _info.IsPresent;

    public PowerDeviceState State => _info.State;

    /// <summary>Full charge capacity over design capacity, from 0 to 100.</summary>
    public double HealthPercentage => _info.HealthPercentage;

    public bool HealthSupported => _info.HealthPercentage != 0;

    public string IconName => _info.IconName;

    public bool IsSystemBattery => _info.Type == PowerDeviceType.Battery && _info.PowersTheSystem;

    public string DeviceId => _info.DeviceId;

    public string Model => _info.Model;

    public bool Ready => _ready;

    internal void Update(PowerDeviceInfo info, bool ready)
    {
        var old = _info;
        _info = info;
        Changed(old.Type != info.Type, nameof(Type));
        Changed(old.PowersTheSystem != info.PowersTheSystem, nameof(PowersTheSystem));
        Changed(old.RemainingCapacity != info.RemainingCapacity, nameof(RemainingCapacity));
        Changed(old.FullChargeCapacity != info.FullChargeCapacity, nameof(FullChargeCapacity));
        Changed(old.ChargeRate != info.ChargeRate, nameof(ChargeRate));
        Changed(old.TimeToEmpty != info.TimeToEmpty, nameof(TimeToEmpty));
        Changed(old.TimeToFull != info.TimeToFull, nameof(TimeToFull));
        Changed(old.Percentage != info.Percentage, nameof(Percentage));
        Changed(old.IsPresent != info.IsPresent, nameof(IsPresent));
        Changed(old.State != info.State, nameof(State));
        Changed(old.HealthPercentage != info.HealthPercentage, nameof(HealthPercentage));
        Changed((old.HealthPercentage != 0) != HealthSupported, nameof(HealthSupported));
        Changed(old.IconName != info.IconName, nameof(IconName));
        Changed((old.Type == PowerDeviceType.Battery && old.PowersTheSystem) != IsSystemBattery, nameof(IsSystemBattery));
        Changed(old.DeviceId != info.DeviceId, nameof(DeviceId));
        Changed(old.Model != info.Model, nameof(Model));
        Set(ref _ready, ready, nameof(Ready));
    }

    public override string ToString() => $"PowerDevice({(_info.DeviceId.Length > 0 ? _info.DeviceId : "DisplayDevice")})";
}

/// <summary>The batteries of this PC, as Windows reports them.</summary>
public sealed class PowerService : ObservableObject, IDisposable
{
    private readonly IDisposable _subscription;
    private bool _onBattery;
    private bool _disposed;

    internal PowerService(IEventLoop loop, SnapshotSource<PowerSnapshot>? source = null)
    {
        source ??= PowerBackend.Instance;
        Apply(source.Current);
        _subscription = source.Subscribe(s => loop.Post(() => Apply(s)));
    }

    public PowerDevice DisplayDevice { get; } = new();

    public ObservableCollection<PowerDevice> Devices { get; } = [];

    public bool OnBattery => _onBattery;

    private void Apply(PowerSnapshot snapshot)
    {
        if (_disposed)
            return;
        DisplayDevice.Update(snapshot.Display, snapshot.Ready);
        Set(ref _onBattery, snapshot.OnBattery, nameof(OnBattery));
        Devices.Sync(snapshot.Devices, d => d.DeviceId, i => i.DeviceId, _ => new PowerDevice(), (d, i) => d.Update(i, true));
    }

    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
    }
}

/// <summary>The Windows power mode. Setting <see cref="Mode"/> changes the slider for the supply in use.</summary>
public sealed class PowerModeService : ObservableObject, IDisposable
{
    private readonly IDisposable _subscription;
    private readonly Func<PowerModeLevel, uint> _set;
    private readonly Log _log;
    private PowerModeLevel _mode = PowerModeLevel.Balanced;
    private EffectivePowerMode _effective = EffectivePowerMode.Balanced;
    private bool _available;
    private bool _ready;
    private bool _disposed;

    internal PowerModeService(IEventLoop loop, Log log, SnapshotSource<PowerModeSnapshot>? source = null, Func<PowerModeLevel, uint>? set = null)
    {
        _log = log;
        source ??= PowerModeBackend.Instance;
        _set = set ?? PowerModeBackend.Instance.Set;
        Apply(source.Current);
        _subscription = source.Subscribe(s => loop.Post(() => Apply(s)));
    }

    public PowerModeLevel Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                _log.Warn("power", $"Cannot set the power mode to {(int)value}, which is not a PowerModeLevel.");
                return;
            }
            var error = _set(value);
            if (error != 0)
                _log.Warn("power", $"Windows did not set the power mode (error {error}).");
        }
    }

    public EffectivePowerMode Effective => _effective;

    /// <summary>The public power-mode API answered. When this is false, <see cref="Mode"/> stays Balanced.</summary>
    public bool Available => _available;

    public bool Ready => _ready;

    private void Apply(PowerModeSnapshot snapshot)
    {
        if (_disposed)
            return;
        Set(ref _mode, snapshot.Mode, nameof(Mode));
        Set(ref _effective, snapshot.Effective, nameof(Effective));
        Set(ref _available, snapshot.Available, nameof(Available));
        Set(ref _ready, snapshot.Ready, nameof(Ready));
    }

    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
    }
}
