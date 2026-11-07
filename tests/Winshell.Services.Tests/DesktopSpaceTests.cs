using Windows.Foundation;
using Winshell.Native;

namespace Winshell.Services.Tests;

public class DesktopSpaceTests
{
    // A 100% 1920×1080 primary, and a 150% 2560×1440 monitor to its right with a 48 px taskbar at the bottom.
    private static readonly RECT Primary = new(0, 0, 1920, 1080);
    private static readonly RECT Scaled = new(1920, 0, 4480, 1440);
    private static readonly RECT ScaledWork = new(1920, 0, 4480, 1392);

    [Theory]
    [InlineData(96u, 1.0)]
    [InlineData(120u, 1.25)]
    [InlineData(144u, 1.5)]
    [InlineData(192u, 2.0)]
    [InlineData(0u, 1.0)]
    public void A_dpi_is_a_scale_of_96(uint dpi, double scale)
    {
        Assert.Equal(scale, DesktopSpace.Scale(dpi));
    }

    [Fact]
    public void A_scaled_monitor_keeps_its_corner_and_divides_its_size()
    {
        Assert.Equal(new Rect(0, 0, 1920, 1080), DesktopSpace.ToLogical(Primary, Primary, 1));
        Assert.Equal(new Rect(1920, 0, 2560 / 1.5, 960), DesktopSpace.ToLogical(Scaled, Scaled, 1.5));
    }

    [Fact]
    public void Monitors_of_mixed_dpi_do_not_overlap()
    {
        var primary = DesktopSpace.ToLogical(Primary, Primary, 1);
        var scaled = DesktopSpace.ToLogical(Scaled, Scaled, 1.5);
        Assert.True(scaled.X >= primary.Right);

        // A 200% monitor to the left of the primary, at negative coordinates, ends where the primary starts.
        var left = new RECT(-3840, 0, 0, 2160);
        var logical = DesktopSpace.ToLogical(left, left, 2);
        Assert.Equal(new Rect(-3840, 0, 1920, 1080), logical);
        Assert.True(logical.Right <= primary.X);
    }

    [Fact]
    public void The_work_area_is_in_its_monitor_coordinates()
    {
        Assert.Equal(new Rect(1920, 0, 2560 / 1.5, 928), DesktopSpace.ToLogical(ScaledWork, Scaled, 1.5));

        // A taskbar on the left of the scaled monitor moves the work area's left edge by its logical width.
        var work = new RECT(1920 + 72, 0, 4480, 1440);
        Assert.Equal(new Rect(1920 + 48, 0, 2488 / 1.5, 960), DesktopSpace.ToLogical(work, Scaled, 1.5));
    }

    [Theory]
    [InlineData(1920, 0)]
    [InlineData(2000, 700)]
    [InlineData(4479, 1439)]
    public void A_physical_point_maps_back_to_itself(int x, int y)
    {
        var logical = DesktopSpace.ToLogical(new POINT { X = x, Y = y }, Scaled, 1.5);
        var physical = DesktopSpace.ToPhysical(logical, Scaled, 1.5);
        Assert.Equal((x, y), (physical.X, physical.Y));
    }

    [Fact]
    public void A_popup_lands_its_logical_offset_times_the_scale_from_its_parent()
    {
        // The parent's client area starts at physical (2400, 1344) on the scaled monitor; the popup is 10, -200 from it.
        var parent = DesktopSpace.ToLogical(new POINT { X = 2400, Y = 1344 }, Scaled, 1.5);
        var popup = DesktopSpace.ToPhysical(new Rect(parent.X + 10, parent.Y - 200, 300, 200), Scaled, 1.5);
        Assert.Equal(new RECT(2415, 1044, 2865, 1344), popup);
    }

    [Fact]
    public void A_logical_point_belongs_to_at_most_one_monitor()
    {
        var monitors = new[]
            { DesktopSpace.ToLogical(Primary, Primary, 1), DesktopSpace.ToLogical(Scaled, Scaled, 1.5) };
        Assert.Equal(0, DesktopSpace.MonitorAt(new Point(100, 100), monitors));
        Assert.Equal(1, DesktopSpace.MonitorAt(new Point(1920, 0), monitors));
        Assert.Equal(1, DesktopSpace.MonitorAt(new Point(3600, 959), monitors));
        // Past the scaled monitor's logical width, and below its logical height, is nothing.
        Assert.Null(DesktopSpace.MonitorAt(new Point(3700, 100), monitors));
        Assert.Null(DesktopSpace.MonitorAt(new Point(2000, 1000), monitors));
    }
}
