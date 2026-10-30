using Winshell.Platform.Windows.Services.Network;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>The taskbar's Wi-Fi glyph, drawn in bars from the connected network's signal.</summary>
[Collection(ThemeCollection.Name)]
public class WifiSignalTests
{
    private const string Status = """
        import QtQuick
        Item {
          readonly property string glyph: SystemStatus.networkGlyph
          readonly property string track: SystemStatus.networkTrackGlyph
        }
        """;

    /// <summary>A Wi-Fi network with a signal strength, which the shared fake network does not have.</summary>
    public sealed class SignalNetwork(double signal) : QObject
    {
        [Qml] public string Name => "Home";
        [Qml] public bool Connected => true;
        [Qml] public double SignalStrength => signal;
    }

    [Theory]
    [InlineData(0.1, "", "")]
    [InlineData(0.4, "", "")]
    [InlineData(0.7, "", "")]
    [InlineData(1.0, "", "")]
    [InlineData(0.0, "", "")]
    public void Wifi_draws_the_bars_its_signal_reaches_over_faint_ones(double signal, string glyph, string track)
    {
        var services = new FakeServices();
        var wifi = new FakeNetworkDevice(DeviceType.Wifi) { Connected = true };
        wifi.Networks.Add(new SignalNetwork(signal));
        services.Networking.Devices.Add(wifi);
        services.Networking.WifiEnabled = true;
        services.Networking.WifiState = WifiRadioState.On;
        services.Networking.Connectivity = NetworkConnectivity.InternetAccess;
        using var shell = new DefaultShellHarness(Status, services: services);
        Assert.Equal(glyph, shell.Root.ReadProperty("glyph"));
        Assert.Equal(track, shell.Root.ReadProperty("track"));
    }
}
