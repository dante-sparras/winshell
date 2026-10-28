using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextLayoutTests
{
    private static TextLayout Layout(string text, double size = 14, string family = "Segoe UI Variable") =>
        TextLayout.Build(
            [new TextSpan(text, new FontSpec(family, size, 400, false, false, false, 0, 0))],
            double.NaN,
            TextLayout.Wrap.NoWrap,
            TextLayout.Elide.None,
            0,
            1,
            true);

    [Fact]
    public void Save_changes_matches_directwrite_metrics_and_the_caret_round_trips()
    {
        using var layout = Layout("Save changes");
        Assert.InRange(layout.Width, 70, 100);
        using var again = Layout("Save changes");
        Assert.Equal(layout.Width, again.Width);
        Assert.Equal(0, layout.CaretX(0));
        Assert.InRange(Math.Abs(layout.CaretX("Save changes".Length) - layout.Width), 0, 1.5);
        var previous = -1.0;
        for (var i = 0; i <= "Save changes".Length; i++)
        {
            var x = layout.CaretX(i);
            Assert.True(x >= previous - 0.01, $"caret {i} moved backwards to {x}");
            previous = x;
        }
        Assert.Equal(0, layout.PositionAt(0));
        Assert.Equal("Save changes".Length, layout.PositionAt(layout.Width + 2));
        Assert.Equal(4, layout.PositionAt(layout.CaretX(4) + 0.1));
    }

    [Fact]
    public void Fallback_uses_cjk_emoji_and_fluent_icons()
    {
        using var layout = Layout("Aあ😀\uE700");
        var families = string.Join(" | ", layout.FontRuns.Select(run => run.Family));
        Assert.Contains("Segoe UI Variable", families, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            families.Contains("Gothic", StringComparison.OrdinalIgnoreCase)
            || families.Contains("YaHei", StringComparison.OrdinalIgnoreCase)
            || families.Contains("Meiryo", StringComparison.OrdinalIgnoreCase)
            || families.Contains("CJK", StringComparison.OrdinalIgnoreCase),
            families);
        Assert.Contains("Emoji", families, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            families.Contains("Fluent", StringComparison.OrdinalIgnoreCase) || families.Contains("MDL2", StringComparison.OrdinalIgnoreCase),
            families);
    }

    [Fact]
    public void Optical_size_grows_with_the_font()
    {
        using var small = Layout("Ag", 8);
        using var large = Layout("Ag", 36);
        var smallOps = small.FontRuns.Count == 0 ? 0 : small.FontRuns.Max(run => run.OpticalSize);
        var largeOps = large.FontRuns.Count == 0 ? 0 : large.FontRuns.Max(run => run.OpticalSize);
        Assert.True(largeOps > smallOps + 4, $"opsz {smallOps} -> {largeOps}");
    }

    [Fact]
    public void Raster_choice_follows_smoothing_and_the_backdrop()
    {
        Assert.Equal(TextPixelMode.Grayscale, TextRaster.Choose(true, true, smoothing: true, clearType: true));
        Assert.Equal(TextPixelMode.ClearType, TextRaster.Choose(false, true, true, true));
        Assert.Equal(TextPixelMode.Grayscale, TextRaster.Choose(false, true, true, false));
        Assert.Equal(TextPixelMode.Aliased, TextRaster.Choose(false, true, false, true));
        Assert.Equal(TextPixelMode.Aliased, TextRaster.Choose(false, false, true, true));
        Assert.Equal(
            TextRaster.Choose(false, true, TextRaster.SmoothingEnabled, TextRaster.ClearTypeEnabled),
            TextRaster.Choose(false, true));
        Assert.Equal(TextPixelMode.Grayscale, TextRaster.Choose(true, true));
    }

    [Fact]
    public void Text_scales_with_the_scene_and_keeps_dip_metrics()
    {
        var text = new Text { Text_ = "Save changes", Color = QColor.Black };
        text.Font.PixelSize = 14;
        text.Font.Family = "Segoe UI Variable";
        Assert.InRange(text.ImplicitWidth, 70, 100);
        var dip = text.ImplicitWidth;
        var scene = new Scene { DevicePixelRatio = 1 };
        scene.Root.SetLayoutWidth(400);
        scene.Root.SetLayoutHeight(80);
        var page = new Rectangle { Color = QColor.White };
        page.SetLayoutWidth(400);
        page.SetLayoutHeight(80);
        page.SetParentItem(scene.Root);
        text.SetParentItem(scene.Root);
        text.SetLayoutWidth(text.ImplicitWidth);
        text.SetLayoutHeight(text.ImplicitHeight);
        PolishQueue.Flush();
        var at1 = InkWidth(scene, 1);
        var at15 = InkWidth(scene, 1.5);
        var at2 = InkWidth(scene, 2);
        Assert.True(at1 > 20, $"ink at 1x was {at1}");
        Assert.InRange(at2 / (double)at1, 1.5, 2.5);
        Assert.InRange(at15 / (double)at1, 1.125, 1.875);
        Assert.Equal(dip, text.ImplicitWidth);
    }

    [Fact]
    public void Composition_is_drawn_before_it_commits()
    {
        var input = new TextInput { Text_ = "ab", CursorPosition = 2 };
        var before = input.CursorRectangle.X;
        input.SetComposition("あ");
        Assert.Equal("ab", input.Text_);
        Assert.Equal("あ", input.CompositionText);
        Assert.True(input.CursorRectangle.X > before);
        input.CancelComposition();
        Assert.Equal("", input.CompositionText);
        Assert.Equal("ab", input.Text_);
        input.CommitComposition("い");
        Assert.Equal("abい", input.Text_);
        Assert.Equal("", input.CompositionText);
    }

    private static int InkWidth(Scene scene, double dpr)
    {
        scene.DevicePixelRatio = dpr;
        var width = (int)Math.Ceiling(400 * dpr);
        var height = (int)Math.Ceiling(80 * dpr);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        scene.Paint(surface.Canvas, TimeSpan.Zero);
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var min = int.MaxValue;
        var max = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 250 && pixel.Green > 250 && pixel.Blue > 250)
                    continue;
                if (x < min)
                    min = x;
                if (x > max)
                    max = x;
            }
        }
        return max < 0 ? 0 : max - min + 1;
    }
}
