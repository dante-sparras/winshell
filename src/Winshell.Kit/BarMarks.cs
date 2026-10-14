using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Winshell.Kit;

/// <summary>The small drawings a bar item uses where Segoe Fluent has no glyph: the menu mark, a graphics card, video memory, and a meter's track.</summary>
internal static class BarMarks
{
    private const double Box = 16;

    // The weight of a Segoe Fluent line at the bar's icon size, so a drawn mark sits beside a glyph as one of them.
    private const double Line = 1.25;

    // A graphics card seen from its side: the bracket, then the board.
    private const string Card = "M 1.5,1.5 V 14.5 M 5.5,3.5 H 13.5 A 1.5,1.5 0 0 1 15,5 V 11 A 1.5,1.5 0 0 1 13.5,12.5 H 5.5 A 1.5,1.5 0 0 1 4,11 V 5 A 1.5,1.5 0 0 1 5.5,3.5 Z";

    /// <summary>The menu mark: four rounded squares.</summary>
    public static Grid Menu()
    {
        var ink = Paint.Foreground;
        var canvas = new Canvas { Width = Box, Height = Box };
        canvas.Children.Add(Square(0, 0, ink));
        canvas.Children.Add(Square(9, 0, ink));
        canvas.Children.Add(Square(0, 9, ink));
        canvas.Children.Add(Square(9, 9, ink));
        return new Grid
        {
            Width = Box,
            Height = Box,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Viewbox { Width = Box, Height = Box, Child = canvas },
            },
        };
    }

    /// <summary>The icon for one readout, or the muted label when the name has no mark.</summary>
    public static FrameworkElement Readout(BarReadout readout)
    {
        FrameworkElement? mark = readout.Glyph switch
        {
            "gpu" => GraphicsCard(),
            "vram" => VideoMemory(),
            _ => FontMark(Glyphs.Named(readout.Glyph)),
        };
        if (mark is null)
        {
            return new TextBlock
            {
                Text = readout.Label,
                Style = Paint.Style("BarTextStyle"),
                Foreground = Paint.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        AutomationProperties.SetName(mark, readout.Label);
        return mark;
    }

    /// <summary>
    /// A meter as a short rounded track, like the Windows 11 progress bar, with the second window as a thin tick across it.
    /// </summary>
    public static Grid Track(BarMeter meter)
    {
        const double width = 36;
        const double height = 4;
        var fill = Math.Clamp(meter.Fill, 0, 1) * width;
        var track = new Grid { Width = width, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = Paint.Selected,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (fill > 0)
        {
            track.Children.Add(new Border
            {
                Width = Math.Max(height, fill),
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
                Background = AgentMarks.Fill(meter.Tone),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (meter.Tick >= 0)
        {
            track.Children.Add(new Border
            {
                Width = 2,
                Height = 10,
                CornerRadius = new CornerRadius(1),
                Background = Paint.Foreground,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Math.Clamp((Math.Clamp(meter.Tick, 0, 1) * width) - 1, 0, width - 2), 0, 0, 0),
            });
        }

        return track;
    }

    /// <summary>A Segoe Fluent glyph at the bar's icon size, in the muted ink. Null when <paramref name="glyph"/> is empty.</summary>
    private static FontIcon? FontMark(string glyph)
    {
        if (glyph.Length == 0)
        {
            return null;
        }

        return new FontIcon
        {
            FontFamily = Fonts.Icons,
            FontSize = TypeRamp.Icon,
            Glyph = glyph,
            Width = TypeRamp.Icon,
            Height = TypeRamp.Icon,
            Foreground = Paint.Foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    /// <summary>A graphics card in outline, with its fan. Segoe Fluent has no GPU glyph.</summary>
    private static Grid GraphicsCard()
    {
        var ink = Paint.Foreground;
        var canvas = new Canvas { Width = Box, Height = Box, IsHitTestVisible = false };
        canvas.Children.Add(Outline(Card + " M 12,8 A 2.5,2.5 0 1 0 7,8 A 2.5,2.5 0 1 0 12,8 Z", ink));
        return MarkBox(canvas);
    }

    /// <summary>The same card holding two memory chips in place of the fan, so video memory reads as the card's own and not as the RAM glyph.</summary>
    private static Grid VideoMemory()
    {
        var ink = Paint.Foreground;
        var canvas = new Canvas { Width = Box, Height = Box, IsHitTestVisible = false };
        canvas.Children.Add(Outline(Card, ink));
        canvas.Children.Add(Block(6, 6, 3, 4, 0.5, ink));
        canvas.Children.Add(Block(10, 6, 3, 4, 0.5, ink));
        return MarkBox(canvas);
    }

    private static Microsoft.UI.Xaml.Shapes.Path Outline(string data, Brush ink) => new()
    {
        Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data),
        Stroke = ink,
        StrokeThickness = Line,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        IsHitTestVisible = false,
    };

    private static Grid MarkBox(Canvas canvas) => new()
    {
        Width = Box,
        Height = Box,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
        Children = { canvas },
    };

    private static Rectangle Block(double x, double y, double width, double height, double radius, Brush ink)
    {
        var block = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Fill = ink,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        return block;
    }

    private static Rectangle Square(double x, double y, Brush ink)
    {
        var square = new Rectangle
        {
            Width = 7,
            Height = 7,
            RadiusX = 1.2,
            RadiusY = 1.2,
            Fill = ink,
        };
        Canvas.SetLeft(square, x);
        Canvas.SetTop(square, y);
        return square;
    }
}
