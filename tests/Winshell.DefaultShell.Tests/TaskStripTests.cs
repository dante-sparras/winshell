using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskStripTests
{
    [Fact]
    public void A_button_already_in_view_does_not_move_the_strip()
    {
        Assert.Equal(0, TaskStrip.Reveal(0, 200, 0, 40));
        Assert.Equal(0, TaskStrip.Reveal(0, 200, 44, 40));
        Assert.Equal(80, TaskStrip.Reveal(80, 200, 100, 40));
    }

    [Fact]
    public void A_button_past_either_end_is_scrolled_fully_into_view()
    {
        Assert.Equal(20, TaskStrip.Reveal(0, 200, 180, 40));
        Assert.Equal(40, TaskStrip.Reveal(100, 200, 40, 40));
        Assert.Equal(120, TaskStrip.Reveal(100, 200, 280, 40));
    }

    [Fact]
    public void A_few_apps_take_their_own_width_and_a_crowd_stops_before_the_tray()
    {
        var content = 1920 - 24;
        var tray = 180.0;
        var three = TaskStrip.NaturalWidth(3);
        Assert.Equal(3 * 40 + 2 * 4, three);

        var (centered, strip) = TaskStrip.Place(content, tray, three, centered: true);
        Assert.Equal(three, strip);
        Assert.Equal(Math.Round((content - (TaskStrip.Chrome + three)) / 2.0), centered);
        Assert.True(centered + TaskStrip.Chrome + strip <= content - tray - TaskStrip.Gap);

        var (left, leftStrip) = TaskStrip.Place(content, tray, three, centered: false);
        Assert.Equal(0, left);
        Assert.Equal(three, leftStrip);

        var many = TaskStrip.NaturalWidth(40);
        var (crowded, wide) = TaskStrip.Place(content, tray, many, centered: true);
        Assert.True(wide < many);
        Assert.Equal(0, crowded);
        Assert.Equal(content - tray - TaskStrip.Gap, crowded + TaskStrip.Chrome + wide);
    }

    [Fact]
    public void A_downward_notch_scrolls_toward_the_later_apps_and_stops_at_the_end()
    {
        Assert.Equal(48, TaskStrip.Wheel(0, 1000, 200, -120));
        Assert.Equal(0, TaskStrip.Wheel(0, 1000, 200, 120));
        Assert.Equal(52, TaskStrip.Wheel(100, 1000, 200, 120));
        Assert.Equal(800, TaskStrip.Wheel(780, 1000, 200, -120));
        Assert.Equal(0, TaskStrip.Wheel(0, 100, 200, -120));
    }

    [Fact]
    public void The_button_positions_are_the_width_plus_the_gap()
    {
        Assert.Equal(0, TaskStrip.ButtonX(0));
        Assert.Equal(44, TaskStrip.ButtonX(1));
        Assert.Equal(88, TaskStrip.ButtonX(2));
    }
}
