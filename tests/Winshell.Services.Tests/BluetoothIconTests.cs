using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Winshell.Bluetooth;

namespace Winshell.Services.Tests;

public sealed class BluetoothIconTests
{
    private static readonly Guid Container = Guid.Parse("71886561-73c1-4d67-a4a0-5077c90859d0");

    [Theory]
    [InlineData(null, BluetoothAdapterState.Unknown)]
    [InlineData(RadioState.Unknown, BluetoothAdapterState.Unknown)]
    [InlineData(RadioState.On, BluetoothAdapterState.On)]
    [InlineData(RadioState.Off, BluetoothAdapterState.Off)]
    [InlineData(RadioState.Disabled, BluetoothAdapterState.Disabled)]
    public void Radio_state_does_not_guess_when_unavailable(RadioState? state, BluetoothAdapterState expected) =>
        Assert.Equal(expected, BluetoothBackend.MapRadioState(state));

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0.0)]
    [InlineData(42, .42)]
    [InlineData(100, 1.0)]
    [InlineData(255, null)]
    [InlineData(-1, null)]
    [InlineData("80", null)]
    public void Battery_rejects_missing_invalid_and_unknown_values(object? value, double? expected) =>
        Assert.Equal(expected, BluetoothBackend.BatteryLevel(value));

    [Fact]
    public void Battery_accepts_the_windows_numeric_property_types()
    {
        Assert.Equal(.5, BluetoothBackend.BatteryLevel((byte)50));
        Assert.Equal(.5, BluetoothBackend.BatteryLevel((ushort)50));
        Assert.Equal(.5, BluetoothBackend.BatteryLevel(50u));
        Assert.Null(BluetoothBackend.BatteryLevel(255u));
    }

    [Theory]
    [InlineData(DevicePairingResultStatus.Paired, null)]
    [InlineData(DevicePairingResultStatus.AlreadyPaired, null)]
    [InlineData(DevicePairingResultStatus.AuthenticationFailure, "AuthenticationFailure")]
    [InlineData(DevicePairingResultStatus.RejectedByHandler, "RejectedByHandler")]
    [InlineData(DevicePairingResultStatus.OperationAlreadyInProgress, "OperationAlreadyInProgress")]
    public void Pairing_result_mapping(DevicePairingResultStatus status, string? error) =>
        Assert.Equal(error, BluetoothBackend.PairingError(status));

    [Theory]
    [InlineData(DeviceUnpairingResultStatus.Unpaired, null)]
    [InlineData(DeviceUnpairingResultStatus.AlreadyUnpaired, null)]
    [InlineData(DeviceUnpairingResultStatus.AccessDenied, "AccessDenied")]
    [InlineData(DeviceUnpairingResultStatus.Failed, "Failed")]
    public void Unpairing_result_mapping(DeviceUnpairingResultStatus status, string? error) =>
        Assert.Equal(error, BluetoothBackend.UnpairingError(status));

    // Major and minor are the unshifted Windows codes: headphones is 6, not the raw class bits.
    [Theory]
    [InlineData(0x01, 0, null, "computer")]
    [InlineData(0x01, 5, 1 << 6, "computer")]
    [InlineData(0x02, 0x01, null, "phone")]
    [InlineData(0x02, 0x02, null, "phone")]
    [InlineData(0x02, 0x03, null, "phone")]
    [InlineData(0x02, 0x05, null, "phone")]
    [InlineData(0x02, 0x04, null, "modem")]
    [InlineData(0x02, 0x00, 1 << 6, "")]
    [InlineData(0x03, 0, null, "network-wireless")]
    [InlineData(0x03, 7, null, "network-wireless")]
    [InlineData(0x04, 0x01, null, "audio-headset")]
    [InlineData(0x04, 0x02, null, "audio-headset")]
    [InlineData(0x04, 0x06, null, "audio-headphones")]
    [InlineData(0x04, 0x0b, null, "camera-video")]
    [InlineData(0x04, 0x0c, null, "camera-video")]
    [InlineData(0x04, 0x0d, null, "camera-video")]
    [InlineData(0x04, 0x00, null, "audio-card")]
    [InlineData(0x04, 0x03, null, "audio-card")]
    [InlineData(0x04, 0x0e, null, "audio-card")]
    [InlineData(0x04, 0x0f, null, "audio-card")]
    [InlineData(0x04, 0x12, null, "audio-card")]
    [InlineData(0x04, 0x06, 1 << 6, "audio-headphones")]
    [InlineData(0x05, 0x01, null, "input-gaming")]
    [InlineData(0x05, 0x02, null, "input-gaming")]
    [InlineData(0x05, 0x10, null, "input-keyboard")]
    [InlineData(0x05, 0x20, null, "input-mouse")]
    [InlineData(0x05, 0x25, null, "input-tablet")]
    [InlineData(0x05, 0x22, null, "input-mouse")]
    [InlineData(0x05, 0x30, null, "")]
    [InlineData(0x05, 0x03, null, "")]
    [InlineData(0x05, 0x00, null, "")]
    [InlineData(0x06, 0x20, null, "printer")]
    [InlineData(0x06, 0x08, null, "camera-photo")]
    [InlineData(0x06, 0x28, null, "printer")]
    [InlineData(0x06, 0x04, null, "")]
    [InlineData(0x07, 0x01, 1 << 6, "")]
    [InlineData(0x08, 0, null, "")]
    [InlineData(0x09, 0, null, "")]
    [InlineData(0x00, 1, 1 << 6, "")]
    [InlineData(0x1f, 0, null, "")]
    [InlineData(0x00, 0, null, "")]
    [InlineData(0x00, 0, 1 << 6, "phone")]
    [InlineData(32, 0, 1 << 6, "")]
    [InlineData(0x04, 64, 1 << 6, "")]
    [InlineData(null, null, null, "")]
    [InlineData(null, null, 0, "")]
    [InlineData(null, null, 1, "unknown")]
    [InlineData(null, null, 1 << 6, "phone")]
    [InlineData(null, null, 2 << 6, "computer")]
    [InlineData(null, null, 5 << 6, "video-display")]
    [InlineData(null, null, 10 << 6, "multimedia-player")]
    [InlineData(null, null, 11 << 6, "scanner")]
    [InlineData(null, null, (0x0f << 6) | 1, "input-keyboard")]
    [InlineData(null, null, (0x0f << 6) | 2, "input-mouse")]
    [InlineData(null, null, (0x0f << 6) | 3, "input-gaming")]
    [InlineData(null, null, (0x0f << 6) | 4, "input-gaming")]
    [InlineData(null, null, (0x0f << 6) | 5, "input-tablet")]
    [InlineData(null, null, (0x0f << 6) | 8, "scanner")]
    [InlineData(null, null, 0x0f << 6, "")]
    [InlineData(null, null, 3 << 6, "")]
    [InlineData(null, null, 0x2a << 6, "")]
    public void Icon_names_follow_the_BlueZ_class_and_appearance(int? major, int? minor, int? appearance, string expected) =>
        Assert.Equal(expected, BluetoothIcon.Name(Code(major), Code(minor), Code(appearance)));

    [Fact]
    public void Merged_endpoints_prefer_a_classic_class_over_an_appearance()
    {
        var headphones = new BluetoothClassReport(0x04, 0x06, null);
        var phone = new BluetoothClassReport(null, null, 1 << 6);
        Assert.Equal("audio-headphones", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("le", paired: true, phone), ClassEndpoint("classic", paired: false, headphones)])).Icon);

        Assert.Equal("computer", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("headphones", paired: false, headphones),
            ClassEndpoint("paired-computer", paired: true, new BluetoothClassReport(0x01, 0, null))])).Icon);

        Assert.Equal("computer", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("b", paired: true, headphones),
            ClassEndpoint("a", paired: true, new BluetoothClassReport(0x01, 1, null))])).Icon);

        Assert.Equal("phone", Assert.Single(BluetoothBackend.MergeEndpoints([ClassEndpoint("only-le", paired: true, phone)])).Icon);
        Assert.Equal("", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("band", paired: true, new BluetoothClassReport(0x07, 0x01, null)),
            ClassEndpoint("band-le", paired: false, phone)])).Icon);
        Assert.Equal("", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("broken", paired: true, new BluetoothClassReport(null, null, null, ClassUnreadable: true)),
            ClassEndpoint("broken-le", paired: false, phone)])).Icon);
        Assert.Equal("phone", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("zero", paired: true, new BluetoothClassReport(0, 0, null)),
            ClassEndpoint("zero-le", paired: false, phone)])).Icon);
        Assert.Equal("audio-headset", Assert.Single(BluetoothBackend.MergeEndpoints([
            ClassEndpoint("zero-first", paired: true, new BluetoothClassReport(0, 0, null)),
            ClassEndpoint("headset", paired: false, new BluetoothClassReport(0x04, 0x01, null))])).Icon);
    }

    [Fact]
    public void A_class_property_of_the_wrong_type_is_empty_and_does_not_throw()
    {
        Assert.Equal("", IconProperties());
        Assert.Equal("video-display", IconProperties((BluetoothIcon.CategoryKey, (ushort)5)));
        Assert.Equal("input-keyboard", IconProperties(
            (BluetoothIcon.CategoryKey, (ushort)0x0f), (BluetoothIcon.SubcategoryKey, (ushort)1)));
        Assert.Equal("phone", IconProperties(
            (BluetoothIcon.AppearanceKey, (ushort)(1 << 6)), (BluetoothIcon.CategoryKey, (ushort)2)));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.AppearanceKey, (ushort)0), (BluetoothIcon.CategoryKey, (ushort)1)));
        Assert.Equal("unknown", IconProperties(
            (BluetoothIcon.CategoryKey, (ushort)0), (BluetoothIcon.SubcategoryKey, (ushort)1)));
        Assert.Equal("", IconProperties((BluetoothIcon.CategoryKey, (ushort)0x400)));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.CategoryKey, (ushort)5), (BluetoothIcon.SubcategoryKey, "no")));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.MajorKey, "4"), (BluetoothIcon.AppearanceKey, (ushort)(1 << 6))));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.MajorKey, (ushort)4), (BluetoothIcon.MinorKey, "6"),
            (BluetoothIcon.AppearanceKey, (ushort)(1 << 6))));
        Assert.Equal("audio-card", IconProperties((BluetoothIcon.MajorKey, (ushort)4)));
        Assert.Equal("", IconProperties(
            (BluetoothIcon.MajorKey, 4u), (BluetoothIcon.AppearanceKey, (ushort)(1 << 6))));
        Assert.Equal("", BluetoothIcon.Name(null, null, (ushort)(1 << 6), classUnreadable: true));
        Assert.Equal("", BluetoothIcon.Name(null, null, (ushort)(1 << 6), appearanceUnreadable: true));
    }

    private static ushort? Code(int? value) => value is int code ? (ushort)code : null;

    private static BluetoothEndpointInfo ClassEndpoint(string id, bool paired, BluetoothClassReport report) =>
        new(id, "aa:bb:cc:dd:ee:ff", "Device", false, paired, true, false, Container, report);

    private static string IconProperties(params (string Key, object Value)[] properties) =>
        BluetoothIcon.FromReports([BluetoothIcon.Read(properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal))]);
}
