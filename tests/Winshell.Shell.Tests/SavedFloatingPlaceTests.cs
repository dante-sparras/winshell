using Winshell.Native;

namespace Winshell.ShellTests;

public sealed class SavedFloatingPlaceTests
{
    [Fact]
    public void A_saved_place_follows_a_moved_monitor_and_a_new_scale()
    {
        var saved = new SavedFloatingPlace(
            Win32.WS_OVERLAPPEDWINDOW, 1, new RECT(0, 0, 1920, 1080), @"\\.\DISPLAY1",
            100, 80, 400, 300, false, false, default, false);

        Assert.True(saved.Matches(@"\\.\DISPLAY1", new RECT(0, 0, 1920, 1080), 1));
        Assert.False(saved.Matches(@"\\.\DISPLAY1", new RECT(1920, 0, 3840, 1080), 1));
        Assert.False(saved.Matches(@"\\.\DISPLAY2", new RECT(0, 0, 1920, 1080), 1));
        Assert.False(saved.Matches(@"\\.\DISPLAY1", new RECT(0, 0, 1920, 1080), 2));
        // Logical (100, 80, 400, 300) on a monitor that starts at (1920, 0), still at scale 1.
        Assert.Equal(new RECT(2020, 80, 2420, 380), saved.RectOn(new RECT(1920, 0, 3840, 1080), 1));
        // The same logical place at scale 2 is twice the physical size, from the same corner.
        Assert.Equal(new RECT(200, 160, 1000, 760), saved.RectOn(new RECT(0, 0, 1920, 1080), 2));
    }
}
