using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.System.Power;
using Winshell.Native;
using WinBattery = Windows.Devices.Power.Battery;
using WinBatteryReport = Windows.Devices.Power.BatteryReport;

namespace Winshell.Power;

/// <summary>One power device, read from Windows.</summary>
internal sealed record PowerDeviceInfo(
    string DeviceId,
    string Model,
    PowerDeviceType Type,
    bool PowersTheSystem,
    bool IsPresent,
    PowerDeviceState State,
    double Percentage,
    double RemainingCapacity,
    double FullChargeCapacity,
    double ChargeRate,
    double TimeToEmpty,
    double TimeToFull,
    double HealthPercentage)
{
    /// <summary>What a PC with no battery reports.</summary>
    public static readonly PowerDeviceInfo NoBattery = new("", "", PowerDeviceType.Unknown, false, false,
        PowerDeviceState.Unknown, 0, 0, 0, 0, 0, 0, 0);
}

internal sealed record PowerSnapshot(
    bool Ready,
    PowerDeviceInfo Display,
    IReadOnlyList<PowerDeviceInfo> Devices,
    bool OnBattery)
{
    public static readonly PowerSnapshot Initial = new(false, PowerDeviceInfo.NoBattery, [], false);
}

internal interface IPowerBackend : IDisposable
{
    /// <summary>Windows reported a change to a battery or the power supply. Raised on any thread.</summary>
    event Action? Changed;

    /// <summary>Reads the batteries. The first read starts watching Windows until the backend is disposed.</summary>
    Task<PowerSnapshot> ReadAsync(CancellationToken cancellation);
}

/// <summary>
/// The batteries of this PC. The display device is Windows' aggregate battery; the devices are each
/// battery Windows enumerates. Windows tells about changes through the batteries' ReportUpdated and the
/// PowerManager events.
/// </summary>
/// <param name="log">Where a battery Windows cannot open is reported.</param>
internal sealed class PowerBackend(Log log) : IPowerBackend
{
    private readonly object _batteriesLock = new();

    private readonly Dictionary<string, (WinBattery Battery, string Name)> _batteries = new(StringComparer.Ordinal);

    // Held so the objects whose events this listens to stay alive.
    private WinBattery? _aggregate;
    private DeviceWatcher? _watcher;

    public event Action? Changed;

    public Task<PowerSnapshot> ReadAsync(CancellationToken cancellation)
    {
        return Task.Run(Read, cancellation);
    }

    private void Watch()
    {
        _aggregate = WinBattery.AggregateBattery;
        _aggregate.ReportUpdated += OnReportUpdated;
        PowerManager.BatteryStatusChanged += OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
        PowerManager.RemainingChargePercentChanged += OnPowerChanged;
        PowerManager.RemainingDischargeTimeChanged += OnPowerChanged;

        // Each battery, kept up to date as batteries come and go.
        _watcher = DeviceInformation.CreateWatcher(WinBattery.GetDeviceSelector());
        _watcher.Added += OnBatteryAdded;
        _watcher.Removed += OnBatteryRemoved;
        _watcher.Start();
    }

    /// <summary>Stops watching Windows.</summary>
    public void Dispose()
    {
        if (_aggregate is null)
            return;
        _aggregate.ReportUpdated -= OnReportUpdated;
        _aggregate = null;
        PowerManager.BatteryStatusChanged -= OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
        PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
        PowerManager.RemainingDischargeTimeChanged -= OnPowerChanged;
        if (_watcher is { } watcher)
        {
            watcher.Added -= OnBatteryAdded;
            watcher.Removed -= OnBatteryRemoved;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
            _watcher = null;
        }

        lock (_batteriesLock)
        {
            foreach (var (battery, _) in _batteries.Values)
                battery.ReportUpdated -= OnReportUpdated;
            _batteries.Clear();
        }
    }

    private void OnReportUpdated(WinBattery sender, object args)
    {
        Changed?.Invoke();
    }

    private void OnPowerChanged(object? sender, object args)
    {
        Changed?.Invoke();
    }

    private void OnBatteryAdded(DeviceWatcher sender, DeviceInformation info)
    {
        _ = AddBatteryAsync(sender, info.Id, info.Name);
    }

    private void OnBatteryRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_batteriesLock)
        {
            if (_batteries.Remove(update.Id, out var removed))
                removed.Battery.ReportUpdated -= OnReportUpdated;
        }

        Changed?.Invoke();
    }

    private async Task AddBatteryAsync(DeviceWatcher watcher, string id, string name)
    {
        try
        {
            var battery = await WinBattery.FromIdAsync(id);
            if (battery is null)
                return;
            lock (_batteriesLock)
            {
                // A battery found as the backend stops is not watched.
                if (!ReferenceEquals(watcher, _watcher))
                    return;
                battery.ReportUpdated += OnReportUpdated;
                _batteries[id] = (battery, name);
            }

            Changed?.Invoke();
        }
#pragma warning disable CA1031 // Nobody awaits this. A battery Windows cannot open is logged and left out.
        catch (Exception e)
#pragma warning restore CA1031
        {
            log.Warn("power", e.Message);
        }
    }

    private PowerSnapshot Read()
    {
        if (_aggregate is null)
            Watch();
        var hasStatus = Win32.GetSystemPowerStatus(out var status);
        // Bit 128 of BatteryFlag: "no system battery".
        var noSystemBattery = hasStatus && (status.BatteryFlag & 128) != 0;

        var aggregate = ReadAggregate(status, hasStatus, noSystemBattery);

        List<PowerDeviceInfo> devices;
        lock (_batteriesLock)
        {
            devices =
            [
                .. _batteries.Select(kv => Describe(kv.Value.Battery.GetReport(), kv.Key, kv.Value.Name, null))
                    .Where(d => d.IsPresent)
            ];
        }

        return new PowerSnapshot(true, aggregate, devices, PowerSupply.OnBattery());
    }

    private static PowerDeviceInfo ReadAggregate(SYSTEM_POWER_STATUS status, bool hasStatus, bool noSystemBattery)
    {
        WinBatteryReport? report = null;
        try
        {
            report = WinBattery.AggregateBattery.GetReport();
        }
        catch (COMException)
        {
        }

        if (report is null || report.Status == BatteryStatus.NotPresent)
        {
            // GetSystemPowerStatus is the fallback when the battery API has nothing to say.
            if (!hasStatus || noSystemBattery || status.BatteryLifePercent == 255)
                return PowerDeviceInfo.NoBattery;
            var percentage = status.BatteryLifePercent / 100.0;
            var charging = (status.BatteryFlag & 8) != 0;
            var state = charging
                ? PowerDeviceState.Charging
                : status.ACLineStatus == 1
                    ? percentage >= 0.99 ? PowerDeviceState.FullyCharged : PowerDeviceState.NotCharging
                    : PowerDeviceState.Discharging;
            var timeToEmpty = state == PowerDeviceState.Discharging && status.BatteryLifeTime != uint.MaxValue
                ? status.BatteryLifeTime
                : 0;
            return new PowerDeviceInfo("", "", PowerDeviceType.Battery, true, true, state, percentage, 0, 0, 0,
                timeToEmpty, 0, 0);
        }

        double? remainingTime = null;
        try
        {
            var t = PowerManager.RemainingDischargeTime;
            if (t > TimeSpan.Zero && t < TimeSpan.FromDays(10))
                remainingTime = t.TotalSeconds;
        }
        catch (COMException)
        {
        }

        if (remainingTime is null && hasStatus && status.BatteryLifeTime != uint.MaxValue)
            remainingTime = status.BatteryLifeTime;
        return Describe(report, "", "", remainingTime);
    }

    /// <summary>A battery report. Capacities are watt-hours and the charge rate is watts.</summary>
    private static PowerDeviceInfo Describe(WinBatteryReport report, string nativePath, string model,
        double? timeToEmptyHint)
    {
        if (report.Status == BatteryStatus.NotPresent)
            return PowerDeviceInfo.NoBattery with
            {
                DeviceId = nativePath, Model = model, Type = PowerDeviceType.Battery, PowersTheSystem = true
            };
        var remaining = report.RemainingCapacityInMilliwattHours / 1000.0;
        var full = report.FullChargeCapacityInMilliwattHours / 1000.0;
        var design = report.DesignCapacityInMilliwattHours / 1000.0;
        var rate = (report.ChargeRateInMilliwatts ?? 0) / 1000.0;
        var percentage = remaining is { } r && full is > 0 ? Math.Clamp(r / full.Value, 0, 1) : 0;
        if (remaining is null || full is not > 0)
            try
            {
                percentage = PowerManager.RemainingChargePercent / 100.0;
            }
            catch (COMException)
            {
            }

        var state = report.Status switch
        {
            BatteryStatus.Charging => PowerDeviceState.Charging,
            BatteryStatus.Discharging => percentage <= 0 ? PowerDeviceState.Empty : PowerDeviceState.Discharging,
            // Idle: on line power and not charging, either full or held below full.
            BatteryStatus.Idle => percentage >= 0.99 ? PowerDeviceState.FullyCharged : PowerDeviceState.NotCharging,
            _ => PowerDeviceState.Unknown
        };
        var timeToEmpty = 0.0;
        if (state == PowerDeviceState.Discharging)
            timeToEmpty = timeToEmptyHint ?? (rate < 0 && remaining is > 0 ? remaining.Value / -rate * 3600 : 0);
        var timeToFull = state == PowerDeviceState.Charging && rate > 0 && remaining is { } now && full is { } max &&
                         max > now
            ? (max - now) / rate * 3600
            : 0;
        var health = full is > 0 && design is > 0 ? Math.Min(100, full.Value / design.Value * 100) : 0;
        return new PowerDeviceInfo(nativePath, model, PowerDeviceType.Battery, true, true, state, percentage,
            remaining ?? 0, full ?? 0, rate,
            Math.Round(timeToEmpty), Math.Round(timeToFull), health);
    }
}

/// <summary>Which supply powers the PC.</summary>
internal static class PowerSupply
{
    /// <summary>
    /// Whether the PC runs on its battery: it has one, and no power supply is plugged in. Windows' power supply status
    /// decides, and the AC line status when that does not answer.
    /// </summary>
    public static bool OnBattery()
    {
        var hasStatus = Win32.GetSystemPowerStatus(out var status);
        // Bit 128 of BatteryFlag: "no system battery".
        if (hasStatus && (status.BatteryFlag & 128) != 0)
            return false;
        try
        {
            return PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;
        }
        catch (COMException)
        {
            return hasStatus && status.ACLineStatus == 0;
        }
    }
}
