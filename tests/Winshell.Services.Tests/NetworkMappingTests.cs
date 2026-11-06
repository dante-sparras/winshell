using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Winshell.Network;

namespace Winshell.Services.Tests;

public sealed class NetworkMappingTests
{
    [Theory]
    [InlineData(NetworkConnectivityLevel.None, NetworkConnectivity.None)]
    [InlineData(NetworkConnectivityLevel.LocalAccess, NetworkConnectivity.LocalAccess)]
    [InlineData(NetworkConnectivityLevel.ConstrainedInternetAccess, NetworkConnectivity.CaptivePortal)]
    [InlineData(NetworkConnectivityLevel.InternetAccess, NetworkConnectivity.InternetAccess)]
    public void Connectivity_mapping(NetworkConnectivityLevel value, NetworkConnectivity expected)
    {
        Assert.Equal(expected, NetworkBackend.MapConnectivity(value));
    }

    [Theory]
    [InlineData(WiFiAccessStatus.Allowed, NetworkAccessStatus.Allowed)]
    [InlineData(WiFiAccessStatus.DeniedByUser, NetworkAccessStatus.DeniedByUser)]
    [InlineData(WiFiAccessStatus.DeniedBySystem, NetworkAccessStatus.DeniedBySystem)]
    [InlineData(WiFiAccessStatus.Unspecified, NetworkAccessStatus.Unspecified)]
    public void Access_mapping(WiFiAccessStatus value, NetworkAccessStatus expected)
    {
        Assert.Equal(expected, NetworkBackend.MapAccess(value));
    }

    [Theory]
    [InlineData(null, AirplaneModeState.Unknown)]
    [InlineData(0, AirplaneModeState.Off)]
    [InlineData(1, AirplaneModeState.On)]
    [InlineData(42, AirplaneModeState.Unknown)]
    public void Airplane_mode_is_not_inferred_from_radios(object? value, AirplaneModeState expected)
    {
        Assert.Equal(expected, NetworkBackend.MapAirplaneMode(value));
    }

    [Theory]
    [InlineData(0x00000004u, WifiDeviceMode.Station)]
    [InlineData(0x00000008u, WifiDeviceMode.AccessPoint)]
    [InlineData(0x00000000u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000001u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000002u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000010u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000020u, WifiDeviceMode.Unknown)]
    [InlineData(0x00000040u, WifiDeviceMode.Unknown)]
    [InlineData(0x40000000u, WifiDeviceMode.Unknown)]
    [InlineData(0x80000000u, WifiDeviceMode.Unknown)]
    public void Operation_mode_maps_extensible_station_and_access_point(uint operationMode, WifiDeviceMode expected)
    {
        var mode = NetworkBackend.MapDeviceMode(operationMode);
        Assert.Equal(expected, mode);
        Assert.NotEqual(WifiDeviceMode.AdHoc, mode);
        Assert.NotEqual(WifiDeviceMode.Mesh, mode);
    }

    [Fact]
    public void A_failed_operation_mode_query_is_unknown()
    {
        var mode = NetworkBackend.MapDeviceMode(null);
        Assert.Equal(WifiDeviceMode.Unknown, mode);
        Assert.NotEqual(WifiDeviceMode.AdHoc, mode);
        Assert.NotEqual(WifiDeviceMode.Mesh, mode);
    }

    [Theory]
    [InlineData(NetworkCostType.Unknown, NetworkCost.Unknown)]
    [InlineData(NetworkCostType.Unrestricted, NetworkCost.Unrestricted)]
    [InlineData(NetworkCostType.Fixed, NetworkCost.Fixed)]
    [InlineData(NetworkCostType.Variable, NetworkCost.Variable)]
    public void Cost_mapping(NetworkCostType value, NetworkCost expected)
    {
        Assert.Equal(expected, NetworkBackend.MapCost(value));
    }

    [Theory]
    [InlineData(WiFiConnectionStatus.Success, null)]
    [InlineData(WiFiConnectionStatus.InvalidCredential, ConnectionFailReason.InvalidPassword)]
    [InlineData(WiFiConnectionStatus.Timeout, ConnectionFailReason.Timeout)]
    [InlineData(WiFiConnectionStatus.NetworkNotAvailable, ConnectionFailReason.NetworkUnavailable)]
    [InlineData(WiFiConnectionStatus.UnsupportedAuthenticationProtocol, ConnectionFailReason.UnsupportedAuthentication)]
    [InlineData(WiFiConnectionStatus.UnspecifiedFailure, ConnectionFailReason.Unknown)]
    public void Native_connection_result_mapping(WiFiConnectionStatus value, ConnectionFailReason? expected)
    {
        Assert.Equal(expected, NetworkBackend.MapConnectionResult(value));
    }

    [Fact]
    public void Wifi_radios_and_adapters_are_reread_when_the_set_of_wifi_devices_changes()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.True(NetworkBackend.WifiDevicesChanged(null, new HashSet<Guid>()));
        Assert.False(NetworkBackend.WifiDevicesChanged(new HashSet<Guid> { a, b }, new HashSet<Guid> { b, a }));
        Assert.True(NetworkBackend.WifiDevicesChanged(new HashSet<Guid> { a }, new HashSet<Guid> { a, b }));
        Assert.True(NetworkBackend.WifiDevicesChanged(new HashSet<Guid> { a, b }, new HashSet<Guid> { a }));
    }
}
