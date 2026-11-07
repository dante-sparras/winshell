using Winshell.Native;

namespace Winshell.ShellTests;

/// <summary>Which part of a window a thumbnail shows, and where in its box.</summary>
public sealed class WindowThumbnailPreviewTests
{
    [Fact]
    public void A_preview_leaves_out_the_invisible_resize_borders()
    {
        // A 1280x720 frame with 7-pixel borders on the left, right and bottom, as Windows 11 draws a normal window.
        Assert.Equal(new RECT(7, 0, 1287, 720),
            WindowThumbnail.PreviewSource(new RECT(93, 100, 1387, 827), new RECT(100, 100, 1380, 820)));
        // A maximized window hangs over the monitor by its borders; the frame is the monitor.
        Assert.Equal(new RECT(8, 8, 1928, 1048),
            WindowThumbnail.PreviewSource(new RECT(-8, -8, 1928, 1048), new RECT(0, 0, 1920, 1040)));
    }

    [Fact]
    public void Without_a_frame_inside_the_window_the_preview_is_the_whole_window()
    {
        Assert.Equal(new RECT(0, 0, 400, 300), WindowThumbnail.PreviewSource(new RECT(10, 10, 410, 310), default));
        Assert.Equal(new RECT(0, 0, 400, 300),
            WindowThumbnail.PreviewSource(new RECT(10, 10, 410, 310), new RECT(500, 500, 600, 600)));
        Assert.Equal(new RECT(0, 0, 0, 0), WindowThumbnail.PreviewSource(default, new RECT(0, 0, 10, 10)));
    }

    [Fact]
    public void A_preview_fits_its_box_keeping_its_shape_centered()
    {
        Assert.Equal(new RECT(10, 38, 210, 150), WindowThumbnail.FitPreview(new RECT(10, 20, 210, 168), 1280, 720));
        Assert.Equal(new RECT(70, 20, 150, 168), WindowThumbnail.FitPreview(new RECT(10, 20, 210, 168), 540, 1000));
    }

    [Fact]
    public void A_small_window_is_previewed_at_its_own_size_not_enlarged()
    {
        Assert.Equal(new RECT(60, 59, 160, 129), WindowThumbnail.FitPreview(new RECT(10, 20, 210, 168), 100, 70));
    }

    [Fact]
    public void A_preview_with_no_box_or_no_source_is_empty()
    {
        Assert.Equal(default, WindowThumbnail.FitPreview(default, 100, 100));
        Assert.Equal(default, WindowThumbnail.FitPreview(new RECT(0, 0, 100, 100), 0, 100));
    }
}
