using Winshell.Native;

namespace Winshell.Services.Tests;

public sealed class WindowOnScreenTests
{
    // Primary work area starts 48px down. The scaled screen has no taskbar, so its work area is the whole screen.
    private static readonly RECT PrimaryMonitor = new(0, 0, 1920, 1080);
    private static readonly RECT PrimaryWork = new(0, 48, 1920, 1080);
    private static readonly MonitorFrame Primary = MonitorFrame.Of(PrimaryMonitor, PrimaryWork, 1);

    private static readonly MonitorFrame Scaled =
        MonitorFrame.Of(new RECT(1920, 0, 4480, 1440), new RECT(1920, 0, 4480, 1440), 1.5);

    [Fact]
    public void A_window_keeps_its_logical_size_and_work_area_offset_across_scales()
    {
        // 100px into the work area. Under a 48px top bar that is screen (100, 148), and the placement stores (100, 100).
        // Placing the workspace rectangle as if it were already in screen pixels would land 72px too high.
        var shown = new RECT(100, 148, 500, 448);
        var restored = new RECT(100, 100, 500, 400);
        var window = new WindowToMove(shown, restored, true, false, false, false);

        Assert.True(WindowOnScreen.TryMove(window, Primary, Scaled, PrimaryMonitor, PrimaryWork, out var place));
        Assert.Equal(new RECT(2070, 150, 2670, 600), place.Screen);
        Assert.Equal(new RECT(2070, 102, 2670, 552), place.Workspace);

        var back = new WindowToMove(place.Screen, place.Workspace, true, false, false, false);
        Assert.True(WindowOnScreen.TryMove(back, Scaled, Primary, PrimaryMonitor, PrimaryWork, out var returned));
        Assert.Equal(shown, returned.Screen);
        Assert.Equal(restored, returned.Workspace);
    }

    [Fact]
    public void A_maximized_or_minimized_window_moves_by_its_restored_rectangle()
    {
        var restored = new RECT(100, 100, 500, 400);
        var maximized = new WindowToMove(new RECT(0, 0, 1920, 1080), restored, true, false, true, false);
        var minimized = new WindowToMove(new RECT(-32000, -32000, -31800, -31900), restored, true, true, false, false);

        Assert.True(WindowOnScreen.TryMove(maximized, Primary, Scaled, PrimaryMonitor, PrimaryWork, out var zoomed));
        Assert.True(WindowOnScreen.TryMove(minimized, Primary, Scaled, PrimaryMonitor, PrimaryWork, out var iconic));
        Assert.Equal(new RECT(2070, 150, 2670, 600), zoomed.Screen);
        Assert.Equal(zoomed, iconic);

        // A tool window's placement is already in screen pixels, so the work-area gap is not added.
        var tool = maximized with { ToolWindow = true };
        Assert.True(WindowOnScreen.TryMove(tool, Primary, Scaled, PrimaryMonitor, PrimaryWork, out var toolPlace));
        Assert.Equal(new RECT(2070, 78, 2670, 528), toolPlace.Screen);
        Assert.Equal(toolPlace.Screen, toolPlace.Workspace);
    }

    [Fact]
    public void A_restored_window_moves_by_the_rectangle_it_shows()
    {
        // The shown rectangle is 30px right and 12px below the workspace rectangle plus the 48px work-area gap.
        // The move follows what is showing, and the stored rectangle keeps that same gap.
        var shown = new RECT(130, 160, 530, 460);
        var restored = new RECT(100, 100, 500, 400);
        var window = new WindowToMove(shown, restored, true, false, false, false);

        Assert.True(WindowOnScreen.TryMove(window, Primary, Scaled, PrimaryMonitor, PrimaryWork, out var place));
        Assert.Equal(new RECT(2115, 168, 2715, 618), place.Screen);
        Assert.Equal(new RECT(2085, 108, 2685, 558), place.Workspace);
    }

    [Fact]
    public void A_window_that_would_hang_off_the_target_is_slid_back()
    {
        var from = new MonitorFrame(0, 0, 1920, 1080, 1);
        var narrow = new MonitorFrame(1920, 0, 2920, 1080, 1);
        var window = new WindowToMove(new RECT(1800, 0, 2200, 100), default, false, false, false, false);

        Assert.True(WindowOnScreen.TryMove(window, from, narrow, default, default, out var place));
        Assert.Equal(new RECT(2520, 0, 2920, 100), place.Screen);
    }

    [Fact]
    public void A_window_larger_than_the_target_shrinks_to_fit()
    {
        var window = new WindowToMove(new RECT(0, 0, 3000, 2000), default, false, false, false, false);

        Assert.True(WindowOnScreen.TryMove(window, new MonitorFrame(0, 0, 4000, 3000, 1),
            new MonitorFrame(0, 0, 800, 600, 1), default, default, out var place));
        Assert.Equal(new RECT(0, 0, 800, 600), place.Screen);
    }

    [Fact]
    public void A_window_above_the_work_area_starts_at_the_target_corner()
    {
        var window = new WindowToMove(new RECT(-50, -20, 150, 80), default, false, false, false, false);

        Assert.True(WindowOnScreen.TryMove(window, new MonitorFrame(0, 0, 1920, 1080, 1),
            new MonitorFrame(1920, 0, 3840, 1080, 1), default, default, out var place));
        Assert.Equal(new RECT(1920, 0, 2120, 100), place.Screen);
    }

    [Fact]
    public void A_bad_monitor_frame_leaves_the_window_where_it_is()
    {
        var shown = new RECT(10, 10, 20, 20);
        var window = new WindowToMove(shown, default, false, false, false, false);

        Assert.True(WindowOnScreen.TryMove(window, Primary with { Scale = 0 }, Scaled, PrimaryMonitor, PrimaryWork,
            out var place));
        Assert.Equal(shown, place.Screen);
    }

    [Fact]
    public void A_window_with_no_rectangle_is_not_moved()
    {
        var window = new WindowToMove(default, default, false, false, false, false);

        Assert.False(WindowOnScreen.TryMove(window, Primary, Scaled, PrimaryMonitor, PrimaryWork, out _));
    }

    [Fact]
    public void The_restored_rectangle_comes_back_in_screen_pixels_without_being_slid_onto_the_monitor()
    {
        var shown = new RECT(0, 0, 1920, 1080);
        var restored = new RECT(-50, -20, 150, 80);
        var maximized = new WindowToMove(shown, restored, true, false, true, false);

        Assert.Equal(new RECT(-50, 28, 150, 128), WindowOnScreen.Restored(maximized, PrimaryMonitor, PrimaryWork));
        Assert.Equal(restored,
            WindowOnScreen.Restored(maximized with { ToolWindow = true }, PrimaryMonitor, PrimaryWork));
        Assert.Equal(shown,
            WindowOnScreen.Restored(maximized with { Restored = default }, PrimaryMonitor, PrimaryWork));
        Assert.Equal(new RECT(14, 20, 214, 120),
            WindowOnScreen.Restored(maximized, new RECT(-3, 0, 1917, 1080), new RECT(61, 40, 1917, 1080)));
        Assert.Equal(restored, WindowOnScreen.Restored(maximized, PrimaryMonitor, default));
    }

    [Fact]
    public void The_frame_a_window_is_placed_in_is_the_work_area()
    {
        var bounds = new RECT(0, 0, 1920, 1080);
        Assert.Equal(new MonitorFrame(0, 48, 1920, 1080, 1), MonitorFrame.Of(bounds, new RECT(0, 48, 1920, 1080), 1));
        Assert.Equal(new MonitorFrame(0, 0, 1920, 1080, 1.5), MonitorFrame.Of(bounds, default, 1.5));
        Assert.Equal(new MonitorFrame(0, 0, 1920, 1080, 1), MonitorFrame.Of(bounds, new RECT(0, 48, 1920, 48), 1));
    }
}
