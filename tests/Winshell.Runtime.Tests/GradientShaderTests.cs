using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Effects;

namespace Winshell.Runtime.Tests;

public class GradientShaderTests
{
    private static Gradient TwoStops(out GradientStop first)
    {
        var gradient = new Gradient();
        first = new GradientStop { Position = 0, Color = QColor.FromRgb(255, 0, 0) };
        gradient.Stops.Add(first);
        gradient.Stops.Add(new GradientStop { Position = 1, Color = QColor.FromRgb(0, 0, 255) });
        return gradient;
    }

    private static SKColor Paint(Item item)
    {
        using var bitmap = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        item.Paint(canvas, new PaintContext());
        return bitmap.GetPixel(1, 1);
    }

    [Fact]
    public void Replacing_a_rectangle_gradient_drops_the_previous_listener()
    {
        var rectangle = new Rectangle { Width = 40, Height = 20 };
        var first = TwoStops(out _);
        var second = TwoStops(out var stop);
        rectangle.Gradient = first;
        rectangle.Gradient = second;
        rectangle.Gradient = first;
        Assert.Single(first.Changed);
        Assert.Empty(second.Changed);
        rectangle.Gradient = second;
        rectangle.Gradient = second;
        Assert.Empty(first.Changed);
        Assert.Single(second.Changed);

        var firstPaint = Paint(rectangle);
        var secondPaint = Paint(rectangle);
        Assert.Equal(1, second.ShaderBuilds);
        Assert.True(secondPaint.Red > 200 && secondPaint.Alpha > 200, $"second paint was {secondPaint}");
        Assert.Equal(firstPaint, secondPaint);
        stop.Color = QColor.FromRgb(0, 255, 0);
        Paint(rectangle);
        Assert.Equal(2, second.ShaderBuilds);
        rectangle.Width = 80;
        Paint(rectangle);
        Assert.Equal(3, second.ShaderBuilds);

        rectangle.Gradient = null;
        Assert.Empty(second.Changed);
        rectangle.Gradient = first;
        rectangle.Destroy();
        Assert.Empty(first.Changed);
    }

    [Fact]
    public void A_linear_gradient_releases_its_gradient_and_caches_the_shader()
    {
        var effect = new LinearGradient { Width = 40, Height = 20 };
        var first = TwoStops(out _);
        var second = TwoStops(out var stop);
        effect.Gradient = first;
        effect.Gradient = second;
        effect.Gradient = first;
        Assert.Single(first.Changed);
        Assert.Empty(second.Changed);
        effect.Gradient = second;
        Assert.Empty(first.Changed);
        Assert.Single(second.Changed);

        var firstPaint = Paint(effect);
        var secondPaint = Paint(effect);
        Assert.Equal(1, effect.ShaderBuilds);
        Assert.True(secondPaint.Red > 200 && secondPaint.Alpha > 200, $"second paint was {secondPaint}");
        Assert.Equal(firstPaint, secondPaint);
        effect.Start = new QPoint(4, 0);
        Paint(effect);
        Assert.Equal(2, effect.ShaderBuilds);
        stop.Color = QColor.FromRgb(0, 255, 0);
        Paint(effect);
        Assert.Equal(3, effect.ShaderBuilds);

        effect.Gradient = null;
        Assert.Empty(second.Changed);
        effect.Gradient = first;
        effect.Destroy();
        Assert.Empty(first.Changed);
    }
}
