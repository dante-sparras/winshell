using Windows.Devices.Radios;
using Winshell.Platform.Windows.Services;
using Xunit;

namespace Winshell.Platform.Windows.Tests;

public sealed class RadioSwitchTests
{
    [Fact]
    public async Task The_current_value_and_a_missing_radio_do_not_request_access()
    {
        var wifi = new FakeRadio(RadioKind.WiFi, RadioState.On);
        var bluetooth = new FakeRadio(RadioKind.Bluetooth, RadioState.Off);
        var source = new FakeSource(wifi, bluetooth);

        Assert.Null(await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, true, source));
        Assert.Equal("get", Assert.Single(source.Calls));
        Assert.Equal(0, wifi.Sets);

        var missing = new FakeSource();
        Assert.Equal(RadioSwitch.Missing(RadioKind.Bluetooth), await RadioSwitch.SetEnabledAsync(RadioKind.Bluetooth, true, missing));
        Assert.Equal("get", Assert.Single(missing.Calls));
    }

    [Fact]
    public async Task An_empty_list_does_not_request_access()
    {
        var source = new FakeSource();
        Assert.Equal("Windows reports no Wi-Fi radio.", await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, true, source));
        Assert.Equal("get", Assert.Single(source.Calls));
    }

    [Theory]
    [InlineData(RadioAccessStatus.DeniedByUser, "The user denied radio control.")]
    [InlineData(RadioAccessStatus.DeniedBySystem, "Windows or its policy denied radio control.")]
    [InlineData(RadioAccessStatus.Unspecified, "Windows did not allow radio control.")]
    public async Task Access_refusal_does_not_set_the_radio(RadioAccessStatus status, string expected)
    {
        var radio = new FakeRadio(RadioKind.WiFi, RadioState.Off);
        var source = new FakeSource(radio) { Access = status };
        Assert.Equal(expected, await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, true, source));
        Assert.Equal(["get", "access"], source.Calls);
        Assert.Equal(0, radio.Sets);
        Assert.Equal(RadioState.Off, radio.State);
    }

    [Fact]
    public async Task A_refused_set_and_a_state_windows_did_not_reach_are_failures()
    {
        var denied = new FakeRadio(RadioKind.Bluetooth, RadioState.Off) { Result = RadioAccessStatus.DeniedBySystem };
        var source = new FakeSource(denied);
        Assert.Equal(RadioSwitch.AccessError(RadioAccessStatus.DeniedBySystem), await RadioSwitch.SetEnabledAsync(RadioKind.Bluetooth, true, source));
        Assert.Equal(1, denied.Sets);
        Assert.Equal(RadioState.Off, denied.State);

        var stuck = new FakeRadio(RadioKind.WiFi, RadioState.On) { Reach = false };
        Assert.Equal(RadioSwitch.Unchanged, await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, false, new FakeSource(stuck)));
        Assert.Equal(RadioState.On, stuck.State);
    }

    [Fact]
    public async Task Only_radios_of_the_requested_kind_change_and_one_refusal_stops_the_rest()
    {
        var wifiOff = new FakeRadio(RadioKind.WiFi, RadioState.Off);
        var wifiOn = new FakeRadio(RadioKind.WiFi, RadioState.On);
        var bluetooth = new FakeRadio(RadioKind.Bluetooth, RadioState.On);
        var source = new FakeSource(wifiOff, wifiOn, bluetooth);

        Assert.Null(await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, false, source));
        Assert.Equal(0, wifiOff.Sets);
        Assert.Equal(1, wifiOn.Sets);
        Assert.Equal(RadioState.Off, wifiOn.State);
        Assert.Equal(0, bluetooth.Sets);

        var blocked = new FakeRadio(RadioKind.WiFi, RadioState.Disabled) { Result = RadioAccessStatus.DeniedBySystem };
        var other = new FakeRadio(RadioKind.WiFi, RadioState.Off);
        Assert.Equal(RadioSwitch.AccessError(RadioAccessStatus.DeniedBySystem),
            await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, true, new FakeSource(blocked, other)));
        Assert.Equal(1, blocked.Sets);
        Assert.Equal(0, other.Sets);
        Assert.Equal(RadioState.Disabled, blocked.State);
        Assert.Equal(RadioState.Off, other.State);
    }

    [Fact]
    public async Task Turning_off_leaves_a_disabled_radio_alone_and_turning_on_reaches_off_radios()
    {
        var on = new FakeRadio(RadioKind.Bluetooth, RadioState.On);
        var disabled = new FakeRadio(RadioKind.Bluetooth, RadioState.Disabled);
        Assert.Null(await RadioSwitch.SetEnabledAsync(RadioKind.Bluetooth, false, new FakeSource(on, disabled)));
        Assert.Equal(RadioState.Off, on.State);
        Assert.Equal(0, disabled.Sets);

        var off = new FakeRadio(RadioKind.WiFi, RadioState.Off);
        var turnOn = new FakeSource(off);
        Assert.Null(await RadioSwitch.SetEnabledAsync(RadioKind.WiFi, true, turnOn));
        Assert.Equal(RadioState.On, off.State);
        Assert.Equal(["get", "access"], turnOn.Calls);
        Assert.Equal("set", Assert.Single(off.Calls));
    }

    private sealed class FakeSource : IRadioSource
    {
        public FakeSource(params FakeRadio[] radios) => Radios = radios;
        public List<string> Calls { get; } = [];
        public FakeRadio[] Radios { get; }
        public RadioAccessStatus Access { get; init; } = RadioAccessStatus.Allowed;
        public Task<IReadOnlyList<IDeviceRadio>> GetRadiosAsync()
        {
            Calls.Add("get");
            return Task.FromResult<IReadOnlyList<IDeviceRadio>>(Radios);
        }
        public Task<RadioAccessStatus> RequestAccessAsync()
        {
            Calls.Add("access");
            return Task.FromResult(Access);
        }
    }

    private sealed class FakeRadio(RadioKind kind, RadioState state) : IDeviceRadio
    {
        public RadioKind Kind { get; } = kind;
        public RadioState State { get; private set; } = state;
        public RadioAccessStatus Result { get; init; } = RadioAccessStatus.Allowed;
        public bool Reach { get; init; } = true;
        public int Sets { get; private set; }
        public List<string> Calls { get; } = [];

        public Task<RadioAccessStatus> SetStateAsync(RadioState next)
        {
            Sets++;
            Calls.Add("set");
            if (Result == RadioAccessStatus.Allowed && Reach)
                State = next;
            return Task.FromResult(Result);
        }
    }
}
