using Winshell.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

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
    public void A_dpi_is_a_scale_of_96(uint dpi, double scale) => Assert.Equal(scale, DesktopSpace.Scale(dpi));

    [Fact]
    public void A_scaled_monitor_keeps_its_corner_and_divides_its_size()
    {
        Assert.Equal(new QRect(0, 0, 1920, 1080), DesktopSpace.ToLogical(Primary, Primary, 1));
        Assert.Equal(new QRect(1920, 0, 2560 / 1.5, 960), DesktopSpace.ToLogical(Scaled, Scaled, 1.5));
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
        Assert.Equal(new QRect(-3840, 0, 1920, 1080), logical);
        Assert.True(logical.Right <= primary.X);
    }

    [Fact]
    public void The_work_area_is_in_its_monitor_coordinates()
    {
        Assert.Equal(new QRect(1920, 0, 2560 / 1.5, 928), DesktopSpace.ToLogical(ScaledWork, Scaled, 1.5));

        // A taskbar on the left of the scaled monitor moves the work area's left edge by its logical width.
        var work = new RECT(1920 + 72, 0, 4480, 1440);
        Assert.Equal(new QRect(1920 + 48, 0, 2488 / 1.5, 960), DesktopSpace.ToLogical(work, Scaled, 1.5));
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
        var popup = DesktopSpace.ToPhysical(new QRect(parent.X + 10, parent.Y - 200, 300, 200), Scaled, 1.5);
        Assert.Equal(new RECT(2415, 1044, 2865, 1344), popup);
    }

    [Fact]
    public void A_logical_point_belongs_to_at_most_one_monitor()
    {
        var monitors = new[] { DesktopSpace.ToLogical(Primary, Primary, 1), DesktopSpace.ToLogical(Scaled, Scaled, 1.5) };
        Assert.Equal(0, DesktopSpace.MonitorAt(new QPoint(100, 100), monitors));
        Assert.Equal(1, DesktopSpace.MonitorAt(new QPoint(1920, 0), monitors));
        Assert.Equal(1, DesktopSpace.MonitorAt(new QPoint(3600, 959), monitors));
        // Past the scaled monitor's logical width, and below its logical height, is nothing.
        Assert.Null(DesktopSpace.MonitorAt(new QPoint(3700, 100), monitors));
        Assert.Null(DesktopSpace.MonitorAt(new QPoint(2000, 1000), monitors));
    }

    private static PanelPlacement Panel(Edges anchors, double width = 0, double height = 0, double left = 0, double top = 0, double right = 0, double bottom = 0) =>
        new(null, anchors, left, top, right, bottom, width, height, 0, 0, true, false, "");

    [Fact]
    public void A_top_bar_spans_its_area_at_the_monitor_scale()
    {
        var rect = DesktopSpace.Anchor(ScaledWork, Panel(Edges.Top | Edges.Left | Edges.Right, height: 32), 1.5);
        Assert.Equal(new RECT(1920, 0, 4480, 48), rect);
    }

    [Fact]
    public void A_bottom_bar_follows_the_work_area_when_the_taskbar_moves()
    {
        var bar = Panel(Edges.Bottom | Edges.Left | Edges.Right, height: 40);
        Assert.Equal(new RECT(1920, 1332, 4480, 1392), DesktopSpace.Anchor(ScaledWork, bar, 1.5));

        // The taskbar auto-hides: the work area is the whole monitor, and the bar moves down to its edge.
        Assert.Equal(new RECT(1920, 1380, 4480, 1440), DesktopSpace.Anchor(Scaled, bar, 1.5));
    }

    [Fact]
    public void A_corner_panel_keeps_its_margins_in_logical_pixels()
    {
        var rect = DesktopSpace.Anchor(ScaledWork, Panel(Edges.Bottom | Edges.Right, width: 200, height: 100, right: 8, bottom: 8), 1.5);
        Assert.Equal(new RECT(4480 - 12 - 300, 1392 - 12 - 150, 4480 - 12, 1392 - 12), rect);
    }

    [Fact]
    public void An_unanchored_panel_is_centered()
    {
        var rect = DesktopSpace.Anchor(Primary, Panel(Edges.None, width: 400, height: 300), 1);
        Assert.Equal(new RECT(760, 390, 1160, 690), rect);
    }

    [Fact]
    public void The_same_panel_is_placed_again_at_a_new_scale()
    {
        // The monitor's scale goes from 100% to 150%: the bar keeps 32 logical pixels.
        var bar = Panel(Edges.Top | Edges.Left | Edges.Right, height: 32);
        Assert.Equal(32, DesktopSpace.Anchor(Scaled, bar, 1).Height);
        Assert.Equal(48, DesktopSpace.Anchor(Scaled, bar, 1.5).Height);
    }

    [Fact]
    public void A_panel_is_never_empty()
    {
        var rect = DesktopSpace.Anchor(Primary, Panel(Edges.Left | Edges.Right, height: 0, left: 1000, right: 1000), 1);
        Assert.True(rect.Width >= 1);
        Assert.True(rect.Height >= 1);
    }
}
