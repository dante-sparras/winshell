using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Power;

internal sealed record PowerModeSnapshot(bool Ready, PowerModeLevel Mode, EffectivePowerMode Effective, bool Available);

/// <summary>
/// The Windows power mode (Settings, System, Power). The slider is the public user-configured AC or DC
/// mode. The effective mode is what Windows is applying, including energy saver and game mode.
/// </summary>
internal sealed partial class PowerModeBackend : SnapshotSource<PowerModeSnapshot>
{
    public static readonly PowerModeBackend Instance = new();

    /// <summary><c>GUID_POWER_MODE_BEST_EFFICIENCY</c>.</summary>
    internal static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    /// <summary><c>GUID_POWER_MODE_BEST_PERFORMANCE</c>.</summary>
    internal static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>
    /// <c>GUID_POWER_MODE_PERFORMANCE</c>, which Windows documents as deprecated. It is a performance
    /// mode, so a read reports Best performance. A write never sends it.
    /// </summary>
    internal static readonly Guid DeprecatedPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");

    private static int _effective = (int)EffectivePowerMode.Balanced;

    private PowerModeBackend()
        : base(new PowerModeSnapshot(false, PowerModeLevel.Balanced, EffectivePowerMode.Balanced, false))
    {
    }

    protected override string Category => "power";

    protected override async Task StartAsync()
    {
        Listen();
        await RefreshNowAsync().ConfigureAwait(false);
    }

    /// <summary>The callback runs once immediately with the current mode, then on each change.</summary>
    private static unsafe void Listen()
    {
        try
        {
            _ = PowerRegisterForEffectivePowerModeNotifications(EffectivePowerModeV2, &OnEffectivePowerModeChanged, 0, out _);
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEffectivePowerModeChanged(int mode, nint context) =>
        NativeCallback.Guard(Log.Default, "power", () =>
        {
            Volatile.Write(ref _effective, mode);
            Instance.Refresh();
        });

    protected override Task<PowerModeSnapshot> ReadAsync()
    {
        var effective = EffectiveFrom(Volatile.Read(ref _effective));
        try
        {
            Guid guid;
            var hr = OnBattery()
                ? PowerGetUserConfiguredDCPowerMode(out guid)
                : PowerGetUserConfiguredACPowerMode(out guid);
            if (hr != 0)
                return Task.FromResult(new PowerModeSnapshot(true, PowerModeLevel.Balanced, effective, false));
            return Task.FromResult(new PowerModeSnapshot(true, FromGuid(guid), effective, true));
        }
        catch (EntryPointNotFoundException)
        {
            return Task.FromResult(new PowerModeSnapshot(true, PowerModeLevel.Balanced, effective, false));
        }
    }

    /// <summary>
    /// Sets the slider for the supply in use: DC while on battery, AC otherwise. Returns the Windows
    /// error, or 0. This changes the power mode, not the power plan.
    /// </summary>
    public uint Set(PowerModeLevel mode)
    {
        if (!Enum.IsDefined(mode))
            return 87;
        var guid = ToGuid(mode);
        uint result;
        try
        {
            result = OnBattery()
                ? PowerSetUserConfiguredDCPowerMode(in guid)
                : PowerSetUserConfiguredACPowerMode(in guid);
        }
        catch (EntryPointNotFoundException)
        {
            result = 127;
        }
        Refresh();
        return result;
    }

    internal static PowerModeLevel FromGuid(Guid guid)
    {
        if (guid == BestEfficiency)
            return PowerModeLevel.BestEfficiency;
        if (guid == BestPerformance || guid == DeprecatedPerformance)
            return PowerModeLevel.BestPerformance;
        return PowerModeLevel.Balanced;
    }

    internal static Guid ToGuid(PowerModeLevel mode) => mode switch
    {
        PowerModeLevel.BestEfficiency => BestEfficiency,
        PowerModeLevel.BestPerformance => BestPerformance,
        _ => Guid.Empty,
    };

    internal static EffectivePowerMode EffectiveFrom(int mode)
        => Enum.IsDefined((EffectivePowerMode)mode) ? (EffectivePowerMode)mode : EffectivePowerMode.Balanced;

    private static bool OnBattery()
    {
        var status = default(SYSTEM_POWER_STATUS);
        return GetSystemPowerStatus(ref status) && status.ACLineStatus == 0;
    }

    private const uint EffectivePowerModeV2 = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS status);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetUserConfiguredACPowerMode(in Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetUserConfiguredDCPowerMode(in Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static unsafe partial int PowerRegisterForEffectivePowerModeNotifications(
        uint version, delegate* unmanaged[Stdcall]<int, nint, void> callback, nint context, out nint registrationHandle);
}
