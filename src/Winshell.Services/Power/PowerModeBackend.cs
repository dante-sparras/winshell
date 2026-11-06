using Winshell.Native;

namespace Winshell.Power;

internal sealed record PowerModeSnapshot(bool Ready, PowerModeLevel Mode, EffectivePowerMode Effective, bool Available)
{
    public static readonly PowerModeSnapshot Initial = new(false, PowerModeLevel.Balanced, EffectivePowerMode.Balanced,
        false);
}

/// <summary>
/// The Windows power mode (Settings, System, Power). The slider is the public user-configured AC or DC
/// mode. The effective mode is what Windows is applying, including energy saver and game mode.
/// </summary>
internal sealed class PowerModeBackend : SnapshotSource<PowerModeSnapshot>
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
    private static void Listen()
    {
        PowerMode.ListenEffective(mode =>
        {
            Volatile.Write(ref _effective, mode);
            Instance.Refresh();
        }, e => Log.Default.Warn("power", e.Message));
    }

    protected override Task<PowerModeSnapshot> ReadAsync()
    {
        var effective = EffectiveFrom(Volatile.Read(ref _effective));
        if (PowerMode.GetUserConfigured(OnBattery(), out var guid) != 0)
            return Task.FromResult(new PowerModeSnapshot(true, PowerModeLevel.Balanced, effective, false));
        return Task.FromResult(new PowerModeSnapshot(true, FromGuid(guid), effective, true));
    }

    /// <summary>
    /// Sets the slider for the supply in use: DC while on battery, AC otherwise. Returns the Windows
    /// error, or 0. This changes the power mode, not the power plan.
    /// </summary>
    public uint Set(PowerModeLevel mode)
    {
        if (!Enum.IsDefined(mode))
            return 87;
        var result = PowerMode.SetUserConfigured(OnBattery(), ToGuid(mode));
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

    internal static Guid ToGuid(PowerModeLevel mode)
    {
        return mode switch
        {
            PowerModeLevel.BestEfficiency => BestEfficiency,
            PowerModeLevel.BestPerformance => BestPerformance,
            _ => Guid.Empty
        };
    }

    internal static EffectivePowerMode EffectiveFrom(int mode)
    {
        return Enum.IsDefined((EffectivePowerMode)mode) ? (EffectivePowerMode)mode : EffectivePowerMode.Balanced;
    }

    private static bool OnBattery()
    {
        var status = default(SYSTEM_POWER_STATUS);
        return Win32.GetSystemPowerStatus(out status) && status.ACLineStatus == 0;
    }
}
