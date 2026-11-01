using Windows.Devices.Radios;

namespace Winshell.Platform.Windows.Services;

/// <summary>A radio <see cref="RadioSwitch"/> can read and ask Windows to change. Tests pass a fake.</summary>
internal interface IDeviceRadio
{
    RadioKind Kind { get; }
    RadioState State { get; }
    Task<RadioAccessStatus> SetStateAsync(RadioState state);
}

/// <summary>
/// The machine's radios. <see cref="GetRadiosAsync"/> does not request control.
/// <see cref="RequestAccessAsync"/> can show Windows' consent UI, so callers use it only when a change is needed.
/// </summary>
internal interface IRadioSource
{
    Task<IReadOnlyList<IDeviceRadio>> GetRadiosAsync();
    Task<RadioAccessStatus> RequestAccessAsync();
}

/// <summary><see cref="Windows.Devices.Radios.Radio"/> for Wi-Fi and Bluetooth. The only production caller of <see cref="Radio.SetStateAsync"/>.</summary>
internal sealed class WindowsRadioSource : IRadioSource
{
    public static readonly WindowsRadioSource Instance = new();

    private WindowsRadioSource() { }

    public async Task<IReadOnlyList<IDeviceRadio>> GetRadiosAsync()
    {
        var radios = await Radio.GetRadiosAsync();
        return radios.Select(static radio => (IDeviceRadio)new WindowsRadio(radio)).ToList();
    }

    public async Task<RadioAccessStatus> RequestAccessAsync() => await Radio.RequestAccessAsync();

    private sealed class WindowsRadio(Radio radio) : IDeviceRadio
    {
        public RadioKind Kind => radio.Kind;
        public RadioState State => radio.State;
        public Task<RadioAccessStatus> SetStateAsync(RadioState state) => radio.SetStateAsync(state).AsTask();
    }
}

/// <summary>
/// Asks Windows to turn every radio of one kind on or off: <see cref="Radio.RequestAccessAsync"/>, then
/// <see cref="Radio.SetStateAsync"/>. An allowed request can still leave the radio where hardware or policy put it.
/// The result is an error message, or null when every radio of that kind reports the requested state.
/// </summary>
internal static class RadioSwitch
{
    internal const string NoAdapterRadio = "Windows reports no Bluetooth radio for this adapter.";
    internal const string Unchanged = "Windows did not change the radio.";

    public static async Task<string?> SetEnabledAsync(RadioKind kind, bool enabled, IRadioSource source)
    {
        var radios = (await source.GetRadiosAsync().ConfigureAwait(false)).Where(radio => radio.Kind == kind).ToList();
        if (radios.Count == 0)
            return Missing(kind);

        // Already there: do not request access. That call can prompt, and nothing would change.
        // Turning off skips a radio that is not on. Disabled is not a state an app can request.
        // Turning on asks every radio that is not on, including a disabled one, which may refuse.
        if (!radios.Any(radio => NeedsChange(radio, enabled)))
            return null;

        var access = await source.RequestAccessAsync().ConfigureAwait(false);
        if (access != RadioAccessStatus.Allowed)
            return AccessError(access);

        var desired = enabled ? RadioState.On : RadioState.Off;
        foreach (var radio in radios)
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

    internal static string Missing(RadioKind kind) => kind switch
    {
        RadioKind.WiFi => "Windows reports no Wi-Fi radio.",
        RadioKind.Bluetooth => "Windows reports no Bluetooth radio.",
        _ => "Windows reports no radio.",
    };

    internal static string AccessError(RadioAccessStatus status) => status switch
    {
        RadioAccessStatus.DeniedByUser => "The user denied radio control.",
        RadioAccessStatus.DeniedBySystem => "Windows or its policy denied radio control.",
        _ => "Windows did not allow radio control.",
    };

    private static bool NeedsChange(IDeviceRadio radio, bool turnOn) =>
        turnOn ? radio.State != RadioState.On : radio.State == RadioState.On;
}
