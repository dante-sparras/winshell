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

    /// <summary>
    /// Whether the device is a battery. <see cref="PowerDeviceType.Unknown"/> on a PC with no battery.
    /// </summary>
    public PowerDeviceType Type => _info.Type;

    /// <summary>Whether the battery powers the PC, rather than a connected device.</summary>
    public bool PowersTheSystem => _info.PowersTheSystem;

    /// <summary>Remaining capacity, in watt-hours.</summary>
    public double RemainingCapacity => _info.RemainingCapacity;

    /// <summary>Full-charge capacity, in watt-hours.</summary>
    public double FullChargeCapacity => _info.FullChargeCapacity;

    /// <summary>Charge rate, in watts. Negative while discharging.</summary>
    public double ChargeRate => _info.ChargeRate;

    /// <summary>
    /// The seconds left until the battery is empty. 0 when it is not discharging or Windows does not know.
    /// </summary>
    public double TimeToEmpty => _info.TimeToEmpty;

    /// <summary>
    /// The seconds left until the battery is full. 0 when it is not charging or Windows does not know.
    /// </summary>
    public double TimeToFull => _info.TimeToFull;

    /// <summary>The charge, from 0 to 1.</summary>
    public double Percentage => _info.Percentage;

    /// <summary>Whether a battery is present. False on a PC with no battery.</summary>
    public bool IsPresent => _info.IsPresent;

    /// <summary>Whether the battery is charging, discharging, full or empty.</summary>
    public PowerDeviceState State => _info.State;

    /// <summary>Full charge capacity over design capacity, from 0 to 100.</summary>
    public double HealthPercentage => _info.HealthPercentage;

    /// <summary>Whether Windows reports the capacities <see cref="HealthPercentage"/> needs.</summary>
    public bool HealthSupported => _info.HealthPercentage != 0;

    /// <summary>Whether the device is a battery that powers the PC.</summary>
    public bool IsSystemBattery => _info.Type == PowerDeviceType.Battery && _info.PowersTheSystem;

    /// <summary>The Windows device ID of the battery. Empty for <see cref="PowerService.DisplayDevice"/>.</summary>
    public string DeviceId => _info.DeviceId;

    /// <summary>
    /// The battery's name, as Windows lists the device. Empty for <see cref="PowerService.DisplayDevice"/>.
    /// </summary>
    public string Model => _info.Model;

    /// <summary>Whether the first read of the batteries has finished.</summary>
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
        Changed(old.HealthPercentage != 0 != HealthSupported, nameof(HealthSupported));
        Changed((old.Type == PowerDeviceType.Battery && old.PowersTheSystem) != IsSystemBattery,
            nameof(IsSystemBattery));
        Changed(old.DeviceId != info.DeviceId, nameof(DeviceId));
        Changed(old.Model != info.Model, nameof(Model));
        Set(ref _ready, ready, nameof(Ready));
    }

    /// <summary>A label for logs: the device ID, or <c>DisplayDevice</c> for the aggregate battery.</summary>
    public override string ToString()
    {
        return $"PowerDevice({(_info.DeviceId.Length > 0 ? _info.DeviceId : "DisplayDevice")})";
    }
}

/// <summary>The batteries of this PC, as Windows reports them.</summary>
public sealed class PowerService : ObservableObject, IDisposable
{
    private readonly IPowerBackend _backend;
    private readonly StateReader<PowerSnapshot> _reader;
    private bool _onBattery;

    internal PowerService(IEventLoop loop, Log log, IPowerBackend? backend = null)
    {
        _backend = backend ?? new PowerBackend(log);
        _reader = new StateReader<PowerSnapshot>(_backend.ReadAsync, Apply, loop,
            new MonitorWarning(log, "power"));
        _backend.Changed += _reader.Request;
        _reader.Request();
    }

    /// <summary>All the batteries as one, as Windows reports its aggregate battery.</summary>
    public PowerDevice DisplayDevice { get; } = new();

    /// <summary>Each battery Windows lists, as it comes and goes. Empty on a PC with no battery.</summary>
    public ObservableCollection<PowerDevice> Devices { get; } = [];

    /// <summary>Whether the PC has a battery and runs on it, with no power supply plugged in.</summary>
    public bool OnBattery => _onBattery;

    private void Apply(PowerSnapshot snapshot)
    {
        DisplayDevice.Update(snapshot.Display, snapshot.Ready);
        Set(ref _onBattery, snapshot.OnBattery, nameof(OnBattery));
        Devices.Sync(snapshot.Devices, d => d.DeviceId, i => i.DeviceId, _ => new PowerDevice(),
            (d, i) => d.Update(i, true));
    }

    /// <summary>Stops following battery changes.</summary>
    public void Dispose()
    {
        _backend.Changed -= _reader.Request;
        _reader.Stop(_backend.Dispose);
    }
}

/// <summary>The Windows power mode. Setting <see cref="Mode"/> changes the slider for the supply in use.</summary>
public sealed class PowerModeService : ObservableObject, IDisposable
{
    private readonly IPowerModeBackend _backend;
    private readonly StateReader<PowerModeSnapshot> _reader;
    private readonly Log _log;
    private PowerModeLevel _mode = PowerModeLevel.Balanced;
    private EffectivePowerMode _effective = EffectivePowerMode.Balanced;
    private bool _available;
    private bool _ready;

    internal PowerModeService(IEventLoop loop, Log log, IPowerModeBackend? backend = null)
    {
        _log = log;
        _backend = backend ?? new PowerModeBackend(log);
        _reader = new StateReader<PowerModeSnapshot>(_backend.ReadAsync, Apply, loop,
            new MonitorWarning(log, "power"));
        _backend.Changed += _reader.Request;
        _reader.Request();
    }

    /// <summary>
    /// The position of the power mode slider for the supply in use, battery or plugged in. Setting it moves that
    /// slider; a value Windows refuses is logged.
    /// </summary>
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

            var error = _backend.Set(value);
            if (error != 0)
                _log.Warn("power", $"Windows did not set the power mode (error {error}).");
        }
    }

    /// <summary>
    /// The power mode Windows is applying now, which can differ from <see cref="Mode"/> while energy saver or game
    /// mode is on.
    /// </summary>
    public EffectivePowerMode Effective => _effective;

    /// <summary>The public power-mode API answered. When this is false, <see cref="Mode"/> stays Balanced.</summary>
    public bool Available => _available;

    /// <summary>Whether the first read of the power mode has finished.</summary>
    public bool Ready => _ready;

    private void Apply(PowerModeSnapshot snapshot)
    {
        Set(ref _mode, snapshot.Mode, nameof(Mode));
        Set(ref _effective, snapshot.Effective, nameof(Effective));
        Set(ref _available, snapshot.Available, nameof(Available));
        Set(ref _ready, snapshot.Ready, nameof(Ready));
    }

    /// <summary>Stops following power mode changes.</summary>
    public void Dispose()
    {
        _backend.Changed -= _reader.Request;
        _reader.Stop(_backend.Dispose);
    }
}
