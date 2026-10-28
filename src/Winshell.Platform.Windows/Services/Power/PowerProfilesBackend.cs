using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Winshell.Platform.Windows.Services.Power;

internal sealed record PowerProfileSnapshot(bool Ready, PowerProfile Profile, bool Available);

/// <summary>
/// The Windows power mode (Settings, System, Power: "Power mode"), which is an overlay on the Balanced
/// power plan. Best power efficiency is PowerSaver, Balanced is Balanced and Best performance is Performance.
/// </summary>
internal sealed partial class PowerProfilesBackend : SnapshotSource<PowerProfileSnapshot>
{
    public static readonly PowerProfilesBackend Instance = new();

    // The overlay schemes behind the power mode slider.
    private static readonly Guid BestPowerEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid BetterBattery = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    private static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private PowerProfilesBackend()
        : base(new PowerProfileSnapshot(false, PowerProfile.Balanced, false))
    {
    }

    protected override string Category => "quickshell.service.powerprofiles";

    protected override async Task StartAsync()
    {
        await RefreshNowAsync().ConfigureAwait(false);
        Listen();
    }

    /// <summary>
    /// Asks Windows to call back whenever the effective power mode changes, which a new power mode does.
    /// The registration lasts as long as the process.
    /// </summary>
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
    private static void OnEffectivePowerModeChanged(int mode, nint context) => Instance.Refresh();

    protected override Task<PowerProfileSnapshot> ReadAsync()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var overlay) != 0)
                return Task.FromResult(new PowerProfileSnapshot(true, PowerProfile.Balanced, false));
            var profile = overlay == BestPowerEfficiency || overlay == BetterBattery ? PowerProfile.PowerSaver
                : overlay == BestPerformance ? PowerProfile.Performance
                : PowerProfile.Balanced;
            return Task.FromResult(new PowerProfileSnapshot(true, profile, true));
        }
        catch (EntryPointNotFoundException)
        {
            return Task.FromResult(new PowerProfileSnapshot(true, PowerProfile.Balanced, false));
        }
    }

    /// <summary>Sets the power mode. Returns the Windows error, or 0.</summary>
    public uint Set(PowerProfile profile)
    {
        var overlay = profile switch
        {
            PowerProfile.PowerSaver => BestPowerEfficiency,
            PowerProfile.Performance => BestPerformance,
            _ => Guid.Empty,
        };
        uint result;
        try
        {
            result = PowerSetActiveOverlayScheme(overlay);
        }
        catch (EntryPointNotFoundException)
        {
            result = 127; // ERROR_PROC_NOT_FOUND
        }
        Refresh();
        return result;
    }

    private const uint EffectivePowerModeV2 = 2;

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    [LibraryImport("powrprof.dll")]
    private static unsafe partial int PowerRegisterForEffectivePowerModeNotifications(
        uint version, delegate* unmanaged[Stdcall]<int, nint, void> callback, nint context, out nint registrationHandle);
}
