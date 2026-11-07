using Windows.Devices.Radios;

namespace Winshell;

/// <summary>A radio a backend read. <see cref="RadioSwitch"/> asks Windows to change it.</summary>
internal interface IDeviceRadio
{
    RadioKind Kind { get; }
    RadioState State { get; }
    Task<RadioAccessStatus> SetStateAsync(RadioState state);
}

/// <summary>Asks for radio control. It can show Windows' consent UI, so callers use it only when a change is needed.</summary>
internal interface IRadioAccess
{
    Task<RadioAccessStatus> RequestAccessAsync();
}

/// <summary>
/// The radios a backend read for one device, and their state at that read. Two readings are equal when their states
/// are: Windows hands out new radio objects on every read, and they are only how to change that state.
/// </summary>
internal sealed record RadioReading(IReadOnlyList<IDeviceRadio> Radios, RadioState State)
{
    public static readonly RadioReading None = new([], RadioState.Unknown);

    public bool Equals(RadioReading? other)
    {
        return other is not null && State == other.State;
    }

    public override int GetHashCode()
    {
        return State.GetHashCode();
    }

    /// <summary>
    /// The radios taken together: <see cref="RadioState.On"/> when any is on, else <see cref="RadioState.Off"/> when
    /// any can be turned on, else <see cref="RadioState.Disabled"/> when any is held off, else
    /// <see cref="RadioState.Unknown"/>.
    /// </summary>
    public static RadioReading Read(IReadOnlyList<IDeviceRadio> radios)
    {
        var states = radios.Select(static radio => radio.State).ToHashSet();
        var state = states.Contains(RadioState.On) ? RadioState.On
            : states.Contains(RadioState.Off) ? RadioState.Off
            : states.Contains(RadioState.Disabled) ? RadioState.Disabled
            : RadioState.Unknown;
        return new RadioReading(radios, state);
    }
}

/// <summary>A Windows <see cref="Radio"/>. The only production caller of <see cref="Radio.SetStateAsync"/>.</summary>
internal sealed class WindowsRadio(Radio radio) : IDeviceRadio
{
    public RadioKind Kind => radio.Kind;
    public RadioState State => radio.State;

    public Task<RadioAccessStatus> SetStateAsync(RadioState state)
    {
        return radio.SetStateAsync(state).AsTask();
    }
}

internal sealed class WindowsRadioAccess : IRadioAccess
{
    public static readonly WindowsRadioAccess Instance = new();

    private WindowsRadioAccess()
    {
    }

    public async Task<RadioAccessStatus> RequestAccessAsync()
    {
        return await Radio.RequestAccessAsync();
    }
}

/// <summary>
/// Asks Windows to turn radios on or off: <see cref="Radio.RequestAccessAsync"/>, then
/// <see cref="Radio.SetStateAsync"/>. An allowed request can still leave a radio where hardware or policy put it.
/// The result is an error message, or null when every radio it asked reports the requested state.
/// </summary>
internal static class RadioSwitch
{
    internal const string Unchanged = "Windows did not change the radio.";

    /// <summary>
    /// The radios of <paramref name="kind"/> among <paramref name="radios"/>, leaving every other radio alone.
    /// Null when each of them reports <paramref name="enabled"/>.
    /// </summary>
    public static async Task<string?> SetEnabledAsync(RadioKind kind, IReadOnlyList<IDeviceRadio> radios,
        bool enabled, IRadioAccess access)
    {
        var asked = radios.Where(radio => radio.Kind == kind).ToList();
        if (asked.Count == 0)
            return Missing(kind);
        // Already there: do not request access. That call can prompt, and nothing would change.
        if (!asked.Any(radio => NeedsChange(radio, enabled)))
            return null;

        var allowed = await access.RequestAccessAsync().ConfigureAwait(false);
        if (allowed != RadioAccessStatus.Allowed)
            return AccessError(allowed);

        var desired = enabled ? RadioState.On : RadioState.Off;
        foreach (var radio in asked)
        {
            if (!NeedsChange(radio, enabled))
                continue;
            var result = await radio.SetStateAsync(desired).ConfigureAwait(false);
            if (result != RadioAccessStatus.Allowed)
                return AccessError(result);
            // Allowed means Windows accepted the request. Hardware or policy can leave the previous state.
            if (radio.State != desired)
                return Unchanged;
        }

        return null;
    }

    internal static string Missing(RadioKind kind)
    {
        return kind switch
        {
            RadioKind.WiFi => "Windows reports no Wi-Fi radio.",
            RadioKind.Bluetooth => "Windows reports no Bluetooth radio for this adapter.",
            _ => "Windows reports no radio."
        };
    }

    internal static string AccessError(RadioAccessStatus status)
    {
        return status switch
        {
            RadioAccessStatus.DeniedByUser => "The user denied radio control.",
            RadioAccessStatus.DeniedBySystem => "Windows or its policy denied radio control.",
            _ => "Windows did not allow radio control."
        };
    }

    // Turning off skips a radio that is not on. Disabled is not a state an app can request.
    // Turning on asks every radio that is not on, including a disabled one, which may refuse.
    private static bool NeedsChange(IDeviceRadio radio, bool turnOn)
    {
        return turnOn ? radio.State != RadioState.On : radio.State == RadioState.On;
    }
}
