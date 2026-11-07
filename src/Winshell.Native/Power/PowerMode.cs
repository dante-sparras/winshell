using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace Winshell.Native;

/// <summary>
/// The Windows power mode: the slider the user sets for each power supply, and the effective mode Windows applies.
/// A Windows without the power mode reports <c>ERROR_PROC_NOT_FOUND</c>.
/// </summary>
internal static unsafe class PowerMode
{
    private const uint ProcedureNotFound = 127;

    private static Action<int>? _effectiveChanged;
    private static void* _effectiveRegistration;

    /// <summary>The mode set for battery or mains power. Returns a Windows error, or 0.</summary>
    internal static uint GetUserConfigured(bool battery, out Guid mode)
    {
        try
        {
            return battery
                ? PInvoke.PowerGetUserConfiguredDCPowerMode(out mode)
                : PInvoke.PowerGetUserConfiguredACPowerMode(out mode);
        }
        catch (EntryPointNotFoundException)
        {
            mode = Guid.Empty;
            return ProcedureNotFound;
        }
    }

    /// <summary>Sets the mode for battery or mains power. Returns a Windows error, or 0.</summary>
    internal static uint SetUserConfigured(bool battery, Guid mode)
    {
        try
        {
            return battery
                ? PInvoke.PowerSetUserConfiguredDCPowerMode(in mode)
                : PInvoke.PowerSetUserConfiguredACPowerMode(in mode);
        }
        catch (EntryPointNotFoundException)
        {
            return ProcedureNotFound;
        }
    }

    /// <summary>
    /// Calls <paramref name="changed"/> on a Windows thread with the effective mode, once right away and then on each
    /// change, until <see cref="StopListeningEffective"/>, and hands <paramref name="failed"/> each exception it throws.
    /// The process has one listener: a later call replaces it. Does nothing on a Windows without these notifications.
    /// </summary>
    internal static void ListenEffective(Action<int> changed, Action<Exception> failed)
    {
        StopListeningEffective();
        _effectiveChanged = mode => Callback.Run(failed, () => changed(mode));
        try
        {
            _ = PInvoke.PowerRegisterForEffectivePowerModeNotifications(PInvoke.EFFECTIVE_POWER_MODE_V2,
                &OnEffectiveChanged, null, out _effectiveRegistration);
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Stops the calls <see cref="ListenEffective"/> started. A call Windows had already begun may still finish.</summary>
    internal static void StopListeningEffective()
    {
        _effectiveChanged = null;
        if (_effectiveRegistration is null)
            return;
        _ = PInvoke.PowerUnregisterFromEffectivePowerModeNotifications(_effectiveRegistration);
        _effectiveRegistration = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEffectiveChanged(EFFECTIVE_POWER_MODE mode, void* context)
    {
        _effectiveChanged?.Invoke((int)mode);
    }
}
