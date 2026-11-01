using System.Runtime.InteropServices;
using SkiaSharp;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Runs on a real composition device. A fake backend cannot show that Windows accepts these calls.</summary>
public class CompositionOpacityTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [Fact]
    public void Opacity_and_clip_run_on_a_real_visual()
    {
        var hwnd = CreateWindowExW(0x00200000, "STATIC", "Winshell opacity test", 0x80000000, 0, 0, 100, 100, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            using var surface = new CompositionSurface(hwnd, null);
            surface.Render(100, 100, c => c.Clear(SKColors.Red));
            surface.SetOpacity(0.5f);
            surface.SetClip(0, 0, 50, 50);
            surface.ClearClip();
            surface.SetOpacity(1f);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }
}
