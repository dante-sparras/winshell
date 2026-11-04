using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskbarRevealTests
{
    [Fact]
    public void A_bar_shows_and_reserves_its_edge_until_auto_hide_is_on()
    {
        using var reveal = Open(out var time);

        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        Assert.True(reveal.ReservesEdge);
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);

        reveal.SetAutoHide(true);

        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
        Assert.False(reveal.ReservesEdge);
        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
    }

    [Fact]
    public void The_pointer_brings_it_back_and_it_hides_a_second_after_the_pointer_leaves()
    {
        using var reveal = Open(out var time);
        reveal.SetAutoHide(true);

        reveal.SetPointer(true);
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        Assert.False(reveal.ReservesEdge);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);

        reveal.SetPointer(false);
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
    }

    [Fact]
    public void Coming_back_onto_the_bar_before_the_second_ends_keeps_it_up()
    {
        using var reveal = Open(out var time);
        reveal.SetAutoHide(true);
        reveal.SetPointer(true);
        reveal.SetPointer(false);
        time.Advance(TimeSpan.FromMilliseconds(700));

        reveal.SetPointer(true);
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
    }

    [Fact]
    public void An_open_surface_keeps_it_up_and_closing_the_last_one_starts_the_second()
    {
        using var reveal = Open(out var time);
        reveal.SetAutoHide(true);
        reveal.SetPointer(true);
        reveal.SetOpen(new TaskbarSurfaces(Start: true));
        reveal.SetPointer(false);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);

        reveal.SetOpen(default);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
    }

    [Fact]
    public void Opening_something_does_not_bring_a_hidden_bar_back()
    {
        using var reveal = Open(out _);
        reveal.SetAutoHide(true);

        reveal.SetOpen(new TaskbarSurfaces(QuickSettings: true));
        reveal.SetOpen(new TaskbarSurfaces(Calendar: true));
        reveal.SetOpen(new TaskbarSurfaces(Preview: true));
        reveal.SetOpen(new TaskbarSurfaces(TaskMenu: true));
        reveal.SetOpen(new TaskbarSurfaces(Tray: true));
        reveal.SetOpen(new TaskbarSurfaces(BarMenu: true));

        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
    }

    [Fact]
    public void Keyboard_mode_brings_it_back_and_it_stays_until_a_second_after_the_keyboard_leaves()
    {
        using var reveal = Open(out var time);
        reveal.SetAutoHide(true);

        reveal.SetOpen(new TaskbarSurfaces(Keyboard: true));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);

        reveal.SetOpen(default);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
    }

    [Fact]
    public void Turning_auto_hide_off_reserves_the_edge_again_and_a_pending_hide_does_not_undo_it()
    {
        using var reveal = Open(out var time);
        reveal.SetAutoHide(true);
        reveal.SetPointer(true);
        reveal.SetPointer(false);
        time.Advance(TimeSpan.FromMilliseconds(500));

        reveal.SetAutoHide(false);

        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        Assert.True(reveal.ReservesEdge);
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TaskbarReveal.ShownHeight, reveal.WindowHeight);
        Assert.True(reveal.ReservesEdge);
    }

    [Fact]
    public void Turning_auto_hide_on_hides_at_once_even_while_the_pointer_is_on_it()
    {
        using var reveal = Open(out _);
        reveal.SetPointer(true);

        reveal.SetAutoHide(true);

        Assert.Equal(TaskbarReveal.HiddenHeight, reveal.WindowHeight);
        Assert.False(reveal.ReservesEdge);
    }

    [Fact]
    public void Every_open_surface_counts_as_busy()
    {
        Assert.False(default(TaskbarSurfaces).Busy);
        Assert.True(new TaskbarSurfaces(Start: true).Busy);
        Assert.True(new TaskbarSurfaces(QuickSettings: true).Busy);
        Assert.True(new TaskbarSurfaces(Calendar: true).Busy);
        Assert.True(new TaskbarSurfaces(Preview: true).Busy);
        Assert.True(new TaskbarSurfaces(TaskMenu: true).Busy);
        Assert.True(new TaskbarSurfaces(Tray: true).Busy);
        Assert.True(new TaskbarSurfaces(BarMenu: true).Busy);
        Assert.True(new TaskbarSurfaces(Keyboard: true).Busy);
    }

    private static TaskbarReveal Open(out FakeTimeProvider time)
    {
        time = new FakeTimeProvider();
        return new TaskbarReveal(time);
    }
}
