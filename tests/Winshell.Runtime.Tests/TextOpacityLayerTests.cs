using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextOpacityLayerTests
{
    [Fact]
    public void Text_inside_a_translucent_item_does_not_punch_through_it()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            Rectangle {
              width: 160; height: 48; color: "#ffff0000"; opacity: 0.5
              Text { x: 8; y: 8; text: "H"; color: "#ff000000"; font.pixelSize: 28 }
            }
            """);
        using var _ = shell;
        using var bitmap = Render(scene, 160, 48);
        var away = MinAlpha(bitmap, 120, 8, 36, 32);
        var near = MinAlpha(bitmap, 0, 4, 48, 40);
        Assert.True(away > 80, $"the rectangle itself did not paint, alpha {away}");
        Assert.True(near > 40, $"text cleared the rectangle behind it, alpha {near}");
    }

    private static int MinAlpha(SKBitmap bitmap, int x, int y, int width, int height)
    {
        var min = 255;
        var right = Math.Min(bitmap.Width, x + width);
        var bottom = Math.Min(bitmap.Height, y + height);
        for (var py = Math.Max(0, y); py < bottom; py++)
        {
            for (var px = Math.Max(0, x); px < right; px++)
                min = Math.Min(min, bitmap.GetPixel(px, py).Alpha);
        }
        return min;
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(160);
        scene.Root.SetLayoutHeight(48);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static SKBitmap Render(Scene scene, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        scene.Paint(canvas, TimeSpan.Zero);
        canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }
}
