using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.System.Power;
using Winshell.Platform.Windows.Native;
using WinBattery = Windows.Devices.Power.Battery;
using WinBatteryReport = Windows.Devices.Power.BatteryReport;

namespace Winshell.Platform.Windows.Services.Power;

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
    double HealthPercentage,
    string IconName)
{
    /// <summary>What a PC with no battery reports.</summary>
    public static readonly PowerDeviceInfo NoBattery = new("", "", PowerDeviceType.Unknown, false, false, PowerDeviceState.Unknown, 0, 0, 0, 0, 0, 0, 0, "battery-missing");
}

internal sealed record PowerSnapshot(bool Ready, PowerDeviceInfo Display, IReadOnlyList<PowerDeviceInfo> Devices, bool OnBattery)
{
    public static readonly PowerSnapshot Initial = new(false, PowerDeviceInfo.NoBattery, [], false);
}

/// <summary>
/// The batteries of this PC. The display device is Windows' aggregate battery; the devices are each
/// battery Windows enumerates. Windows tells about changes through the batteries' ReportUpdated and the
/// PowerManager events.
/// </summary>
internal sealed partial class PowerBackend : SnapshotSource<PowerSnapshot>
{
    public static readonly PowerBackend Instance = new();

    private readonly object _batteriesLock = new();
    private readonly Dictionary<string, (WinBattery Battery, string Name)> _batteries = new(StringComparer.Ordinal);
    // Held so the objects whose events this listens to stay alive.
    private WinBattery? _aggregate;
    private DeviceWatcher? _watcher;

    private PowerBackend()
        : base(PowerSnapshot.Initial)
    {
    }

    protected override string Category => "power";

    protected override async Task StartAsync()
    {
        _aggregate = WinBattery.AggregateBattery;
        _aggregate.ReportUpdated += (_, _) => Refresh();
        PowerManager.BatteryStatusChanged += (_, _) => Refresh();
        PowerManager.PowerSupplyStatusChanged += (_, _) => Refresh();
        PowerManager.RemainingChargePercentChanged += (_, _) => Refresh();
        PowerManager.RemainingDischargeTimeChanged += (_, _) => Refresh();
        await RefreshNowAsync().ConfigureAwait(false);

        // Each battery, kept up to date as batteries come and go.
        _watcher = DeviceInformation.CreateWatcher(WinBattery.GetDeviceSelector());
        _watcher.Added += (_, info) => ServiceSupport.Run(Category, () => AddBatteryAsync(info.Id, info.Name));
        _watcher.Removed += (_, update) =>
        {
            lock (_batteriesLock)
                _batteries.Remove(update.Id);
            Refresh();
        };
        _watcher.Start();
    }

    private async Task AddBatteryAsync(string id, string name)
    {
        var battery = await WinBattery.FromIdAsync(id);
        if (battery is null)
            return;
        battery.ReportUpdated += (_, _) => Refresh();
        lock (_batteriesLock)
            _batteries[id] = (battery, name);
        Refresh();
    }

    protected override Task<PowerSnapshot> ReadAsync()
    {
        var status = default(SYSTEM_POWER_STATUS);
        var hasStatus = Win32.GetSystemPowerStatus(out status);
        // Bit 128 of BatteryFlag: "no system battery".
        var noSystemBattery = hasStatus && (status.BatteryFlag & 128) != 0;

        var aggregate = ReadAggregate(status, hasStatus, noSystemBattery);

        List<PowerDeviceInfo> devices;
        lock (_batteriesLock)
            devices = [.. _batteries.Select(kv => Describe(kv.Value.Battery.GetReport(), kv.Key, kv.Value.Name, null)).Where(d => d.IsPresent)];

        var onBattery = aggregate.IsPresent && (PowerSupplyNotPresent() ?? (hasStatus && status.ACLineStatus == 0));
        return Task.FromResult(new PowerSnapshot(true, aggregate, devices, onBattery));
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
            var state = charging ? PowerDeviceState.Charging
                : status.ACLineStatus == 1 ? (percentage >= 0.99 ? PowerDeviceState.FullyCharged : PowerDeviceState.NotCharging)
                : PowerDeviceState.Discharging;
            var timeToEmpty = state == PowerDeviceState.Discharging && status.BatteryLifeTime != uint.MaxValue ? status.BatteryLifeTime : 0;
            return new PowerDeviceInfo("", "", PowerDeviceType.Battery, true, true, state, percentage, 0, 0, 0, timeToEmpty, 0, 0, IconName(state, percentage));
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

    private static bool? PowerSupplyNotPresent()
    {
        try
        {
            return PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>A battery report. Capacities are watt-hours and the charge rate is watts.</summary>
    private static PowerDeviceInfo Describe(WinBatteryReport report, string nativePath, string model, double? timeToEmptyHint)
    {
        if (report.Status == BatteryStatus.NotPresent)
            return PowerDeviceInfo.NoBattery with { DeviceId = nativePath, Model = model, Type = PowerDeviceType.Battery, PowersTheSystem = true };
        var remaining = report.RemainingCapacityInMilliwattHours / 1000.0;
        var full = report.FullChargeCapacityInMilliwattHours / 1000.0;
        var design = report.DesignCapacityInMilliwattHours / 1000.0;
        var rate = (report.ChargeRateInMilliwatts ?? 0) / 1000.0;
        var percentage = remaining is { } r && full is > 0 ? Math.Clamp(r / full.Value, 0, 1) : 0;
        if (remaining is null || full is not > 0)
        {
            try
            {
                percentage = PowerManager.RemainingChargePercent / 100.0;
            }
            catch (COMException)
            {
            }
        }
        var state = report.Status switch
        {
            BatteryStatus.Charging => PowerDeviceState.Charging,
            BatteryStatus.Discharging => percentage <= 0 ? PowerDeviceState.Empty : PowerDeviceState.Discharging,
            // Idle: on line power and not charging, either full or held below full.
            BatteryStatus.Idle => percentage >= 0.99 ? PowerDeviceState.FullyCharged : PowerDeviceState.NotCharging,
            _ => PowerDeviceState.Unknown,
        };
        var timeToEmpty = 0.0;
        if (state == PowerDeviceState.Discharging)
            timeToEmpty = timeToEmptyHint ?? (rate < 0 && remaining is > 0 ? remaining.Value / -rate * 3600 : 0);
        var timeToFull = state == PowerDeviceState.Charging && rate > 0 && remaining is { } now && full is { } max && max > now
            ? (max - now) / rate * 3600
            : 0;
        var health = full is > 0 && design is > 0 ? Math.Min(100, full.Value / design.Value * 100) : 0;
        return new PowerDeviceInfo(nativePath, model, PowerDeviceType.Battery, true, true, state, percentage, remaining ?? 0, full ?? 0, rate,
            Math.Round(timeToEmpty), Math.Round(timeToFull), health, IconName(state, percentage));
    }

    /// <summary>A shell icon name for a battery, such as <c>battery-good-charging</c>.</summary>
    public static string IconName(PowerDeviceState state, double percentage)
    {
        var level = percentage < 0.10 ? "caution" : percentage < 0.30 ? "low" : percentage < 0.60 ? "good" : "full";
        return state switch
        {
            PowerDeviceState.FullyCharged => "battery-full",
            PowerDeviceState.Empty => "battery-empty",
            PowerDeviceState.Charging => $"battery-{level}-charging",
            PowerDeviceState.Discharging or PowerDeviceState.NotCharging => $"battery-{level}",
            _ => "battery-missing",
        };
    }
}
