using Windows.Foundation;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class CalendarFlyoutPlacementTests
{
    [Fact]
    public void The_surface_sits_above_the_clock_and_inside_a_normal_work_area()
    {
        var placed = CalendarFlyoutPlacement.Place(new Rect(1800, 1036, 80, 40), new Rect(0, 0, 1920, 1032));

        Assert.Equal(new Rect(1496, 248, 384, 780), placed);
    }

    [Fact]
    public void A_short_work_area_shrinks_the_surface_and_keeps_it_inside()
    {
        var placed = CalendarFlyoutPlacement.Place(new Rect(2100, 360, 80, 40), new Rect(1920, 0, 300, 400));

        Assert.Equal(new Rect(1920, 0, 300, 384), placed);
    }

    [Fact]
    public void A_fractional_edge_is_rounded_to_a_whole_pixel()
    {
        var placed = CalendarFlyoutPlacement.Place(new Rect(800.25, 1500, 50, 40), new Rect(0, 0, 2000, 2000));

        Assert.Equal(new Rect(466, 712, 384, 780), placed);
    }
}
