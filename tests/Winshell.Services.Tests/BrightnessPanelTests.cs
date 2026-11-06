using Winshell.Power;

namespace Winshell.Services.Tests;

/// <summary>A built-in panel's WMI brightness is matched to the screen it belongs to.</summary>
public class BrightnessPanelTests
{
    [Fact]
    public void A_built_in_panel_is_matched_to_its_screen_and_named_by_its_model()
    {
        // What EnumDisplayDevicesW returned with flag 1 for a real monitor, and a WMI instance name in the form WMI
        // gives the same monitor: its device instance path with a "_0" suffix.
        var interfacePath = @"\\?\DISPLAY#AUS32A8#5&15335277&0&UID184582#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var instance = @"DISPLAY\AUS32A8\5&15335277&0&UID184582_0";
        var info = new ScreenInfo(@"\\.\DISPLAY1", "DISPLAY1", "Laptop panel", "AUS", "", default, default, 1, 0, 0,
            60, true);
        var screen = new ConnectedMonitor(1, info, interfacePath, default, default, 1);

        var monitors = BrightnessBackend.Combine([screen], [new BrightnessBackend.WmiPanel(instance, 0, 100, 60)],
            _ => null, []);

        Assert.Equal([new BrightnessInfo(instance, "Laptop panel", BrightnessConnection.Internal, 0, 100, 60)],
            monitors);
    }
}
