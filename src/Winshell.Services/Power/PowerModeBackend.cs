using Winshell.Native;

namespace Winshell.Power;

internal sealed record PowerModeSnapshot(bool Ready, PowerModeLevel Mode, EffectivePowerMode Effective, bool Available)
{
    public static readonly PowerModeSnapshot Initial = new(false, PowerModeLevel.Balanced, EffectivePowerMode.Balanced,
        false);
}

internal interface IPowerModeBackend : IDisposable
{
    /// <summary>Windows reported a change, or a write changed something. Raised on any thread.</summary>
    event Action? Changed;

    /// <summary>Reads the power mode. The first read starts watching Windows until the backend is disposed.</summary>
    Task<PowerModeSnapshot> ReadAsync(CancellationToken cancellation);

    /// <summary>
    /// Sets the slider for the supply in use: DC while on battery, AC otherwise. Returns the Windows error, or 0. This
    /// changes the power mode, not the power plan.
    /// </summary>
    uint Set(PowerModeLevel mode);
}

/// <summary>
/// The Windows power mode (Settings, System, Power). The slider is the public user-configured AC or DC
/// mode. The effective mode is what Windows is applying, including energy saver and game mode.
/// </summary>
/// <param name="log">Where a failed power mode callback is reported.</param>
internal sealed class PowerModeBackend(Log log) : IPowerModeBackend
{
    /// <summary><c>GUID_POWER_MODE_BEST_EFFICIENCY</c>.</summary>
    internal static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    /// <summary><c>GUID_POWER_MODE_BEST_PERFORMANCE</c>.</summary>
    internal static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>
    /// <c>GUID_POWER_MODE_PERFORMANCE</c>, which Windows documents as deprecated. It is a performance
    /// mode, so a read reports Best performance. A write never sends it.
    /// </summary>
    internal static readonly Guid DeprecatedPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");

    private int _effective = (int)EffectivePowerMode.Balanced;
    private bool _watching;

    public event Action? Changed;

    public Task<PowerModeSnapshot> ReadAsync(CancellationToken cancellation)
    {
        return Task.Run(Read, cancellation);
    }

    private PowerModeSnapshot Read()
    {
        if (!_watching)
        {
            _watching = true;
            // Windows calls back once at once with the effective mode, then on each change.
            PowerMode.ListenEffective(mode =>
            {
                Volatile.Write(ref _effective, mode);
                Changed?.Invoke();
            }, e => log.Warn("power", e.Message));
        }

        var effective = EffectiveFrom(Volatile.Read(ref _effective));
        return PowerMode.GetUserConfigured(PowerSupply.OnBattery(), out var guid) == 0
            ? new PowerModeSnapshot(true, FromGuid(guid), effective, true)
            : new PowerModeSnapshot(true, PowerModeLevel.Balanced, effective, false);
    }

    public uint Set(PowerModeLevel mode)
    {
        if (!Enum.IsDefined(mode))
            return 87;
        var result = PowerMode.SetUserConfigured(PowerSupply.OnBattery(), ToGuid(mode));
        Changed?.Invoke();
        return result;
    }

    public void Dispose()
    {
        if (_watching)
            PowerMode.StopListeningEffective();
        _watching = false;
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
}
