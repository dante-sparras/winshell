using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Pixel density is the screen's width over the physical millimeters captured with the snapshot.</summary>
public class ScreenDensityTests
{
    [Fact]
    public void Density_is_width_over_millimeters_and_zero_without_a_size()
    {
        var screen = new ShellScreen(Info(1920, 2, 480));
        Assert.Equal(8d, screen.PhysicalPixelDensity);
        Assert.Equal(4d, screen.LogicalPixelDensity);

        var seen = new List<string>();
        using var physical = screen.Connect("physicalPixelDensityChanged", _ => seen.Add("physical"));
        using var logical = screen.Connect("logicalPixelDensityChanged", _ => seen.Add("logical"));

        screen.Update(Info(960, 2, 480));
        Assert.Equal(4d, screen.PhysicalPixelDensity);
        Assert.Equal(2d, screen.LogicalPixelDensity);
        Assert.Equal(["physical", "logical"], seen);

        seen.Clear();
        screen.Update(Info(960, 3, 480));
        Assert.Equal(6d, screen.PhysicalPixelDensity);
        Assert.Equal(2d, screen.LogicalPixelDensity);
        Assert.Equal(["physical"], seen);

        seen.Clear();
        screen.Update(Info(960, 3, 0));
        Assert.Equal(0d, screen.PhysicalPixelDensity);
        Assert.Equal(0d, screen.LogicalPixelDensity);
        Assert.Equal(["physical", "logical"], seen);

        seen.Clear();
        var taller = Info(960, 3, 0) with { Geometry = new QRect(0, 0, 960, 2000) };
        screen.Update(taller);
        Assert.Empty(seen);
    }

    private static ScreenInfo Info(double width, double dpr, double widthMm) =>
        new("1", "DISPLAY1", "Monitor", "AUS", "S", new QRect(0, 0, width, 1080), new QRect(0, 0, width, 1040), dpr, widthMm, 270, 60, true);
}
