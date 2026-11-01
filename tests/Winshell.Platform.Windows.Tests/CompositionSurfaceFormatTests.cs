using System.Runtime.InteropServices;
using SkiaSharp;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Runs on a real composition device. A fake backend cannot show that Windows accepts these swap chains.</summary>
public class CompositionSurfaceFormatTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void A_real_swap_chain_takes_each_surface_format(bool? opaque)
    {
        var hwnd = CreateWindowExW(0x00200000, "STATIC", "Winshell surface format test", 0x80000000, 0, 0, 100, 100, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            using var surface = new CompositionSurface(hwnd, opaque);
            surface.Render(100, 100, c => c.Clear(SKColors.Red));
            surface.ApplyFormat(opaque != true);
            surface.Render(120, 100, c => c.Clear(SKColors.Blue));
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }
}
