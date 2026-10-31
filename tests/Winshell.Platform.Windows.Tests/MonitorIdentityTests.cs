using Winshell.Platform.Windows;

namespace Winshell.Platform.Windows.Tests;

/// <summary>How a monitor device id becomes <c>ShellScreen.manufacturer</c> and <c>serialNumber</c>.</summary>
public class MonitorIdentityTests
{
    [Fact]
    public void A_monitor_device_id_splits_into_the_manufacturer_and_the_serial()
    {
        Assert.Equal("AUS", Screens.Manufacturer(@"MONITOR\AUS32A8\5&1"));
        Assert.Equal("5&1", Screens.Serial(@"MONITOR\AUS32A8\5&1"));
    }

    [Fact]
    public void An_interface_path_has_no_manufacturer_and_the_question_mark_serial()
    {
        // Flag 1 of EnumDisplayDevicesW returns this shape. The call site asks for flag 0 instead.
        var id = @"\\?\DISPLAY#AUS32A8#4&abc";
        Assert.Equal("", Screens.Manufacturer(id));
        Assert.Equal("?", Screens.Serial(id));
    }
}
