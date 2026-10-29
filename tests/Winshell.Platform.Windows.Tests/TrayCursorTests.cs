using Winshell.Platform.Windows.Services.Tray;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public sealed class TrayCursorTests
{
    [Fact]
    public void Moving_into_the_menu_keeps_the_pointer_where_the_user_left_it()
    {
        var loop = new ManualEventLoop();
        (int X, int Y) pointer = (100, 100);
        using var cursor = new TrayCursor(loop, () => pointer, point => pointer = point);

        cursor.Place((500, 500));
        Assert.Equal((500, 500), pointer);
        pointer = (520, 540);
        loop.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Equal((520, 540), pointer);
    }

    [Fact]
    public void A_keyboard_menu_restores_an_untouched_pointer()
    {
        var loop = new ManualEventLoop();
        (int X, int Y) pointer = (100, 100);
        using var cursor = new TrayCursor(loop, () => pointer, point => pointer = point);

        cursor.Place((500, 500));
        loop.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Equal((500, 500), pointer);
        loop.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal((100, 100), pointer);
    }

    [Fact]
    public void A_mouse_click_near_the_pointer_needs_no_move_or_timer()
    {
        var loop = new ManualEventLoop();
        using var cursor = new TrayCursor(loop, () => (100, 100), _ => Assert.Fail("The pointer moved."));

        cursor.Place((102, 99));

        Assert.Null(loop.NextDue);
    }

    [Fact]
    public void Disposing_the_tray_cancels_a_pending_restore()
    {
        var loop = new ManualEventLoop();
        (int X, int Y) pointer = (100, 100);
        var cursor = new TrayCursor(loop, () => pointer, point => pointer = point);
        cursor.Place((500, 500));

        cursor.Dispose();
        loop.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal((500, 500), pointer);
    }
}
