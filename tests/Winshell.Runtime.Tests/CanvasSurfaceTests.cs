using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Canvas;

namespace Winshell.Runtime.Tests;

public class CanvasSurfaceTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml, double dpr = 1)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene { DevicePixelRatio = dpr };
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(200);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        shell.Loop.Process();
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

    private static Canvas FindCanvas(Item root, string objectName) =>
        root.ChildItems.OfType<Canvas>().Single(c => c.ObjectName == objectName);

    [Fact]
    public void A_canvas_drawn_onto_another_keeps_its_size_at_150_percent()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            Item {
              Canvas {
                id: source
                objectName: "source"
                width: 20; height: 20
                onPaint: { const c = getContext("2d"); c.fillStyle = "red"; c.fillRect(0, 0, 20, 20) }
                onPainted: target.requestPaint()
              }
              Canvas {
                id: target
                objectName: "target"
                y: 100
                width: 60; height: 60
                onPaint: { const c = getContext("2d"); c.reset(); c.drawImage(source, 0, 0) }
              }
            }
            """, dpr: 1.5);
        using var _ = shell;
        using var bitmap = Render(scene, 300, 300);

        // The target sits at y 100 DIPs, 150 device pixels. The source is 20 DIPs, 30 device pixels, wide.
        Assert.Equal(SKColors.Red, bitmap.GetPixel(15, 165));
        // Drawn as if its device pixels were DIPs, it reached 45 device pixels.
        Assert.Equal(0, bitmap.GetPixel(40, 165).Alpha);
        Assert.Equal(0, bitmap.GetPixel(15, 190).Alpha);
    }

    [Fact]
    public void A_canvas_source_rectangle_is_in_the_source_canvas_units()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            Item {
              Canvas {
                id: source
                width: 20; height: 20
                onPaint: { const c = getContext("2d"); c.fillStyle = "blue"; c.fillRect(10, 0, 10, 20) }
                onPainted: target.requestPaint()
              }
              Canvas {
                id: target
                y: 100
                width: 60; height: 60
                onPaint: { const c = getContext("2d"); c.reset(); c.drawImage(source, 10, 0, 10, 20, 0, 0, 10, 20) }
              }
            }
            """, dpr: 2);
        using var _ = shell;
        using var bitmap = Render(scene, 400, 400);

        // The right half of the source fills the target's first 10 DIPs, 20 device pixels.
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(10, 220));
        Assert.Equal(0, bitmap.GetPixel(30, 220).Alpha);
    }

    [Fact]
    public void A_huge_canvas_size_is_capped_and_still_fills_the_item()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              Canvas {
                objectName: "canvas"
                width: 100; height: 100
                canvasSize: Qt.size(100000, 100000)
                onPaint: { const c = getContext("2d"); c.fillStyle = "red"; c.fillRect(0, 0, 100000, 100000) }
              }
            }
            """);
        using var _ = shell;
        var surface = FindCanvas(root, "canvas").Surface;

        Assert.NotNull(surface);
        var info = surface.Canvas.DeviceClipBounds;
        Assert.True(info.Width <= 16384 && info.Height <= 16384);
        Assert.True((long)info.Width * info.Height <= 64L * 1024 * 1024);
        Assert.Contains(shell.Messages, m => m.Contains("canvas", StringComparison.OrdinalIgnoreCase) && m.Contains("cap", StringComparison.OrdinalIgnoreCase));
        using var bitmap = Render(scene, 200, 200);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(10, 10));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(90, 90));
    }

    [Fact]
    public void Save_writes_a_relative_path_next_to_the_qml_file()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              Canvas {
                objectName: "canvas"
                width: 10; height: 10
                onPaint: { const c = getContext("2d"); c.fillRect(0, 0, 10, 10) }
              }
            }
            """);
        using var _ = shell;
        var canvas = FindCanvas(root, "canvas");

        Assert.True(canvas.Save("saved.png"));
        Assert.True(File.Exists(Path.Combine(shell.Directory, "saved.png")));
    }

    [Fact]
    public void Save_to_a_path_it_cannot_write_returns_false()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              Canvas {
                objectName: "canvas"
                width: 10; height: 10
                onPaint: { const c = getContext("2d"); c.fillRect(0, 0, 10, 10) }
              }
            }
            """);
        using var _ = shell;
        var canvas = FindCanvas(root, "canvas");
        Directory.CreateDirectory(Path.Combine(shell.Directory, "folder"));

        // Writing over a folder throws UnauthorizedAccessException, which escaped to JS before.
        Assert.False(canvas.Save("folder"));
    }
}
