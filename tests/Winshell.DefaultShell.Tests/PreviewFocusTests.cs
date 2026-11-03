using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class PreviewFocusTests
{
    [Theory]
    [InlineData(0, false, 1, 0, true)]
    [InlineData(0, true, 1, 1, false)]
    [InlineData(1, false, 1, 1, true)]
    [InlineData(1, true, 1, 2, false)]
    [InlineData(2, false, 1, 2, true)]
    [InlineData(2, true, 1, 2, true)]
    [InlineData(2, true, -1, 2, false)]
    [InlineData(1, false, -1, 0, true)]
    [InlineData(0, true, -1, 0, false)]
    [InlineData(0, false, -1, 0, false)]
    public void A_step_goes_window_close_button_next_window_and_stops_at_both_ends(
        int index, bool close, int direction, int toIndex, bool toClose)
    {
        Assert.Equal(new PreviewFocus(toIndex, toClose), new PreviewFocus(index, close).Step(direction, 3));
    }

    [Theory]
    [InlineData(0, false, 1, 1, false)]
    [InlineData(28, false, 1, 29, false)]
    [InlineData(29, false, 1, 29, false)]
    [InlineData(2, true, 1, 3, false)]
    [InlineData(29, true, 1, 29, true)]
    [InlineData(29, true, -1, 29, false)]
    [InlineData(29, false, -1, 28, false)]
    [InlineData(0, false, -1, 0, false)]
    public void Up_and_Down_skip_the_close_button_but_Up_from_it_returns_to_its_row(
        int index, bool close, int direction, int toIndex, bool toClose)
    {
        Assert.Equal(new PreviewFocus(toIndex, toClose), new PreviewFocus(index, close).Vertical(direction, 30));
    }

    [Theory]
    [InlineData(0, 580, 600, 0)]
    [InlineData(15, 300, 600, 300)]
    [InlineData(3, 0, 100, 60)]
    public void A_row_above_scrolls_to_its_top_a_row_below_to_its_bottom_and_a_row_in_view_stays(
        int index, double scroll, double viewport, double expected)
    {
        Assert.Equal(expected, PreviewFocus.Reveal(index, scroll, viewport));
    }
}
