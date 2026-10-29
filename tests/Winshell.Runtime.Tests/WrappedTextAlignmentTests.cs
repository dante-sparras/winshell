using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class WrappedTextAlignmentTests
{
    [Fact]
    public void Wrapped_text_without_a_width_stays_inside_the_item()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              Text {
                objectName: "center"
                text: "Hello"
                color: "#ff000000"
                wrapMode: Text.WordWrap
                horizontalAlignment: Text.AlignHCenter
              }
              Text {
                objectName: "right"
                y: 40
                text: "Hello"
                color: "#ff000000"
                wrapMode: Text.WordWrap
                horizontalAlignment: Text.AlignRight
              }
            }
            """);
        using var _ = shell;
        var center = Find(root, "center");
        var right = Find(root, "right");
        Assert.InRange(center.Width, 10, 300);
        Assert.InRange(right.Width, 10, 300);
        using var bitmap = Render(scene, 300, 80);
        Assert.True(HasInk(bitmap, 0, 0, (int)center.Width, 36), "centred text was drawn outside the item");
        Assert.True(HasInk(bitmap, 0, 40, (int)right.Width, 36), "right-aligned text was drawn outside the item");
    }

    private static bool HasInk(SKBitmap bitmap, int x, int y, int width, int height)
    {
        var right = Math.Min(bitmap.Width, x + width);
        var bottom = Math.Min(bitmap.Height, y + height);
        for (var py = Math.Max(0, y); py < bottom; py++)
        {
            for (var px = Math.Max(0, x); px < right; px++)
            {
                if (bitmap.GetPixel(px, py).Alpha > 0)
                    return true;
            }
        }
        return false;
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(300);
        scene.Root.SetLayoutHeight(80);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static SKBitmap Render(Scene scene, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        scene.Paint(canvas, TimeSpan.Zero);
        return bitmap;
    }

    private static Item Find(Item root, string objectName)
    {
        if (root.ObjectName == objectName)
            return root;
        foreach (var child in root.ChildItems)
        {
            try
            {
                return Find(child, objectName);
            }
            catch (InvalidOperationException)
            {
            }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }
}
