using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Winshell.Kit;

/// <summary>
/// The weather condition on the bar, as one 16-pixel outline mark. Segoe Fluent has a sun, a moon, and a cloud.
/// Rain, snow, a storm, and fog are that cloud with a few thin strokes under it, so every mark keeps the font's line weight.
/// </summary>
internal static class WeatherMarks
{
    /// <summary>The edge of the box every mark is drawn in.</summary>
    public const double Size = 16;

    private const double Stroke = 1.25;

    /// <summary>True for a weather glyph name, such as <c>weather-sun</c>.</summary>
    public static bool Names(string glyph) => glyph.StartsWith("weather-", StringComparison.Ordinal);

    /// <summary>Draws <paramref name="glyph"/> into <paramref name="host"/>, a 16-pixel box, in <paramref name="ink"/>.</summary>
    public static void Fill(Grid host, string glyph, Brush ink)
    {
        host.Children.Clear();
        switch (glyph)
        {
            case "weather-sun":
                host.Children.Add(Icon("", Size, ink));
                break;
            case "weather-moon":
                host.Children.Add(Icon("", Size, ink));
                break;
            case "weather-partly":
            case "weather-partly-night":
                var behind = Icon(glyph == "weather-partly" ? "" : "", 10, ink);
                behind.HorizontalAlignment = HorizontalAlignment.Left;
                behind.VerticalAlignment = VerticalAlignment.Top;
                var front = Icon("", 13, ink);
                front.HorizontalAlignment = HorizontalAlignment.Right;
                front.VerticalAlignment = VerticalAlignment.Bottom;
                host.Children.Add(behind);
                host.Children.Add(front);
                break;
            case "weather-rain":
                Under(host, ink, Line(4.5, 13, 3.3, 16, ink), Line(8.5, 13, 7.3, 16, ink), Line(12.5, 13, 11.3, 16, ink));
                break;
            case "weather-snow":
                Under(host, ink, Dot(4, 13.5, ink), Dot(8, 15.5, ink), Dot(12, 13.5, ink));
                break;
            case "weather-storm":
                Under(host, ink, new Polygon
                {
                    Fill = ink,
                    Points = new PointCollection { new(9, 10), new(5.4, 14), new(7.8, 14), new(6.6, 17.4), new(10.6, 12.6), new(8.2, 12.6), new(9.8, 10) },
                });
                break;
            case "weather-fog":
                Under(host, ink, Line(2, 13.5, 14, 13.5, ink), Line(4, 16, 12, 16, ink));
                break;
            default:
                host.Children.Add(Icon("", Size, ink));
                break;
        }
    }

    /// <summary>A cloud lifted to the top of the box, with <paramref name="marks"/> in the space under it.</summary>
    private static void Under(Grid host, Brush ink, params UIElement[] marks)
    {
        var cloud = Icon("", 15, ink);
        cloud.VerticalAlignment = VerticalAlignment.Top;
        cloud.Margin = new Thickness(0, -3, 0, 0);
        host.Children.Add(cloud);
        var canvas = new Canvas { Width = Size, Height = Size, IsHitTestVisible = false };
        foreach (var mark in marks)
        {
            canvas.Children.Add(mark);
        }

        host.Children.Add(canvas);
    }

    private static FontIcon Icon(string glyph, double size, Brush ink) => new()
    {
        Glyph = glyph,
        FontFamily = Fonts.Icons,
        FontSize = size,
        Foreground = ink,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    private static Line Line(double x1, double y1, double x2, double y2, Brush ink) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = ink,
        StrokeThickness = Stroke,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static Ellipse Dot(double x, double y, Brush ink)
    {
        var dot = new Ellipse { Width = 2.6, Height = 2.6, Fill = ink };
        Canvas.SetLeft(dot, x - 1.3);
        Canvas.SetTop(dot, y - 1.3);
        return dot;
    }
}
