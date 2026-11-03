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
    [InlineData(-1, true, false, 0, false)]
    [InlineData(2, true, false, 2, true)]
    [InlineData(2, true, true, 2, false)]
    [InlineData(2, false, false, 2, false)]
    public void Read_again_the_keyboard_stays_on_its_window_and_keeps_the_close_button_only_in_the_same_layout(
        int found, bool keepClose, bool layoutChanged, int toIndex, bool toClose)
    {
        Assert.Equal(new PreviewFocus(toIndex, toClose), PreviewFocus.Restore(found, keepClose, layoutChanged));
    }

    [Fact]
    public void A_row_scrolls_fully_into_view_and_a_row_in_view_does_not_scroll()
    {
        var viewport = 1032 * 0.6;
        var last = 29 * (double)TaskPreview.RowHeight + TaskPreview.RowHeight - viewport;

        Assert.Equal(last, PreviewFocus.Reveal(29, 0, viewport));
        Assert.True(Math.Abs(last - 580.8) < 0.5);
        Assert.Equal(0, PreviewFocus.Reveal(0, last, viewport));
        Assert.Equal(300, PreviewFocus.Reveal(15, 300, viewport));
        Assert.Equal(60, PreviewFocus.Reveal(3, 0, 100));
    }
}
