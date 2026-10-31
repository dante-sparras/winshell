using Winshell.Platform.Windows;

namespace Winshell.Platform.Windows.Tests;

/// <summary>How a monitor device id becomes <c>ShellScreen.manufacturer</c> and <c>serialNumber</c>.</summary>
public class MonitorIdentityTests
{
    [Fact]
    public void A_monitor_interface_path_splits_into_the_manufacturer_and_the_instance()
    {
        // What EnumDisplayDevicesW returned with flag 1 for a real monitor.
        var id = @"\\?\DISPLAY#AUS32A8#5&15335277&0&UID184582#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        Assert.Equal("AUS", Screens.Manufacturer(id));
        Assert.Equal("5&15335277&0&UID184582", Screens.Serial(id));
    }

    [Fact]
    public void A_monitor_windows_does_not_name_has_no_manufacturer_or_serial()
    {
        Assert.Equal("", Screens.Manufacturer(""));
        Assert.Equal("", Screens.Serial(""));
    }
}
