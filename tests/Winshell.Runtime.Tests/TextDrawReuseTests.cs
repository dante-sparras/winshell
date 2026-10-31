using SkiaSharp;
using Vortice.DirectWrite;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextDrawReuseTests
{
    [Fact]
    public void Repainting_an_unchanged_text_lays_nothing_out_again()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Text { width: 200; text: "Centred"; horizontalAlignment: Text.AlignHCenter; style: Text.Outline }
            """);
        using var _ = shell;
        var layout = ((Text)root).CurrentLayout!;
        Render(scene, 200, 40).Dispose();
        var first = layout.Relayouts;
        Render(scene, 200, 40).Dispose();
        Assert.True(first > 0, "the first paint did not align the text in its box");
        Assert.Equal(first, layout.Relayouts);
    }

    [Fact]
    public void Text_draws_reuse_one_render_target()
    {
        var engine = DirectWriteEngine.Create();
        var (wide, _) = engine.CreateLayout("A wider line of text", FontSpec.Default, WordWrapping.NoWrap, 1_000_000f, 1_000_000f, false, 1, true);
        var (narrow, _) = engine.CreateLayout("Hi", FontSpec.Default, WordWrapping.NoWrap, 1_000_000f, 1_000_000f, false, 1, true);
        using (wide)
        using (narrow)
        {
            Blit(engine, wide, new SKRect(-8, -8, 200, 40), readRoot: true).Dispose();
            Blit(engine, wide, new SKRect(-8, -8, 200, 40), readRoot: true).Dispose();
            Blit(engine, narrow, new SKRect(-8, -8, 40, 30), readRoot: false).Dispose();
            Assert.Equal(1, engine.TargetsCreated);
        }
    }

    [Fact]
    public void A_reused_scratch_bitmap_brings_no_old_text_along()
    {
        var engine = DirectWriteEngine.Create();
        var (wide, _) = engine.CreateLayout("WWWWWWWWWWWW", FontSpec.Default, WordWrapping.NoWrap, 1_000_000f, 1_000_000f, false, 1, true);
        var (dot, _) = engine.CreateLayout(".", FontSpec.Default, WordWrapping.NoWrap, 1_000_000f, 1_000_000f, false, 1, true);
        using (wide)
        using (dot)
        {
            var box = new SKRect(-8, -8, 200, 40);
            Blit(engine, wide, box, readRoot: false).Dispose();
            using var bitmap = Blit(engine, dot, box, readRoot: false);
            Assert.False(HasInk(bitmap, 30, 0, 170, 40), "the scratch bitmap still held the earlier text");
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.99)]
    public void Outline_draws_the_style_colour_around_the_glyphs(double opacity)
    {
        var (_, scene, shell) = Show($$"""
            import QtQuick
            Rectangle {
              color: "#ff000000"; opacity: {{opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
              Text { x: 10; y: 4; text: "HHH"; color: "#ffffffff"; style: Text.Outline; styleColor: "#ffff0000"; font.pixelSize: 28 }
            }
            """);
        using var _ = shell;
        using var bitmap = Render(scene, 200, 40);
        var red = 0;
        var white = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red > 150 && c.Green < 60 && c.Blue < 60)
                    red++;
                if (c.Red > 200 && c.Green > 200 && c.Blue > 200)
                    white++;
            }
        }
        Assert.True(red > 40, $"only {red} outline pixels");
        Assert.True(white > 40, $"only {white} glyph pixels");
    }

    private static SKBitmap Blit(DirectWriteEngine engine, IDWriteTextLayout layout, SKRect box, bool readRoot)
    {
        var info = new SKImageInfo(200, 40, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        engine.Blit(layout, [], surface.Canvas, box, [new TextPass(0, 0, QColor.Black, UseSpans: true)], TextPixelMode.Grayscale, readRoot);
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
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
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(40);
        root.SetParentItem(scene.Root);
        root.SetLayoutWidth(200);
        root.SetLayoutHeight(40);
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
