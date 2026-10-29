using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class BlurCropTests
{
    [Theory]
    [InlineData("GaussianBlur { radius: 2")]
    [InlineData("FastBlur { radius: 4")]
    public void A_blur_without_a_transparent_border_crops_the_scaled_source_to_the_effect(string blur)
    {
        // A 100 by 100 source, red on the left and blue on the right, drawn into a 50 by 50 blur.
        using var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            import Qt5Compat.GraphicalEffects
            Item {
              property alias effect: effect
              Item {
                id: source
                width: 100; height: 100
                Rectangle { width: 50; height: 100; color: "red" }
                Rectangle { x: 50; width: 50; height: 100; color: "blue" }
              }
              {{blur}}; id: effect; width: 50; height: 50; source: source }
            }
            """));
        var root = (Item)shell.Load();
        shell.Loop.Process();
        var effect = (Item)root.ReadProperty("effect")!;

        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        effect.Paint(canvas, new PaintContext());

        var left = bitmap.GetPixel(5, 25);
        var right = bitmap.GetPixel(45, 25);
        Assert.True(left.Red > 200 && left.Blue < 50 && left.Alpha > 200, $"left was {left}");
        Assert.True(right.Blue > 200 && right.Red < 50 && right.Alpha > 200, $"right was {right}");
        Assert.Equal(0, bitmap.GetPixel(75, 25).Alpha);
        Assert.Equal(0, bitmap.GetPixel(25, 75).Alpha);
    }
}
