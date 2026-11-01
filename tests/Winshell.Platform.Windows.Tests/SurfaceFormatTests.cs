namespace Winshell.Platform.Windows.Tests;

/// <summary>The swap chain's alpha mode and the device-loss codes, without a real device.</summary>
public class SurfaceFormatTests
{
    [Fact]
    public void An_unset_or_transparent_surface_stays_premultiplied_and_opaque_ignores_alpha()
    {
        Assert.Equal(1, CompositionSurface.DxgiAlphaMode(null));
        Assert.Equal(1, CompositionSurface.DxgiAlphaMode(false));
        Assert.Equal(3, CompositionSurface.DxgiAlphaMode(true));
    }

    [Fact]
    public void Only_a_removed_or_reset_device_or_out_of_memory_is_a_lost_frame()
    {
        Assert.True(SurfaceLoss.IsDeviceLoss(unchecked((int)0x887A0005)));
        Assert.True(SurfaceLoss.IsDeviceLoss(unchecked((int)0x887A0007)));
        Assert.True(SurfaceLoss.IsDeviceLoss(unchecked((int)0x8007000E)));
        Assert.False(SurfaceLoss.IsDeviceLoss(unchecked((int)0x80004005)));
    }
}
