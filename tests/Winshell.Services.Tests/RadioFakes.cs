using Windows.Devices.Radios;

namespace Winshell.Services.Tests;

internal sealed class FakeAccess : IRadioAccess
{
    public List<string> Calls { get; } = [];
    public RadioAccessStatus Access { get; set; } = RadioAccessStatus.Allowed;

    public Task<RadioAccessStatus> RequestAccessAsync()
    {
        Calls.Add("access");
        return Task.FromResult(Access);
    }
}

internal sealed class FakeRadio(RadioKind kind, RadioState state) : IDeviceRadio
{
    public RadioKind Kind { get; } = kind;
    public RadioState State { get; private set; } = state;
    public RadioAccessStatus Result { get; set; } = RadioAccessStatus.Allowed;
    public bool Reach { get; set; } = true;
    public Exception? Failure { get; set; }
    public TaskCompletionSource<RadioAccessStatus>? Gate { get; set; }
    public int Sets { get; private set; }

    public async Task<RadioAccessStatus> SetStateAsync(RadioState next)
    {
        Sets++;
        if (Failure is { } failure)
            throw failure;
        var result = Gate is { } gate ? await gate.Task.ConfigureAwait(false) : Result;
        if (result == RadioAccessStatus.Allowed && Reach)
            State = next;
        return result;
    }
}
