using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Winshell.Kit;

/// <summary>
/// A line of bar text drawn in a fixed width. A line that fits sits still, on the left.
/// A longer line scrolls left at <see cref="TextScroll.Speed"/>, the line followed by a gap and a second copy,
/// so the loop has no seam. The box is what the bar measures: the copies live on a canvas, which does not pass their width up.
/// </summary>
internal sealed class TextMarquee
{
    private readonly Grid _box;
    private readonly Canvas _canvas;
    private readonly TextBlock _line;
    private readonly TextBlock _copy;
    private readonly TextBlock _trim;
    private readonly RectangleGeometry _clip = new();
    private double _natural;
    private string _text = "";
    private string _loopKey = "";

    /// <summary>An empty line. <see cref="Show"/> gives it text and a width.</summary>
    public TextMarquee()
    {
        _line = Line();
        _copy = Line();
        _copy.Visibility = Visibility.Collapsed;
        _trim = Line();
        _trim.TextTrimming = TextTrimming.CharacterEllipsis;
        _trim.Visibility = Visibility.Collapsed;
        _canvas = new Canvas
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Children = { _line, _copy },
        };
        _box = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Clip = _clip,
            Children = { _canvas, _trim },
        };
        _box.SizeChanged += (_, _) => Settle();
        _line.SizeChanged += (_, _) =>
        {
            if (_line.ActualWidth > 0)
            {
                _natural = _line.ActualWidth;
            }

            if (_line.ActualHeight > 0 && Math.Abs(_canvas.Height - _line.ActualHeight) > 0.5)
            {
                _canvas.Height = _line.ActualHeight;
            }

            Settle();
        };
        _box.Loaded += (_, _) =>
        {
            // The visual the loop was started on is not the one on screen until the line is in the tree.
            _loopKey = "";
            Settle();
        };
        _box.Unloaded += (_, _) =>
        {
            // The line comes back with the same words. Stopping here only lets the compositor object go.
            _loopKey = "";
            Motion.Scroll(_canvas, 0, TimeSpan.Zero);
        };
    }

    /// <summary>The fixed-width box the bar lays out.</summary>
    public UIElement Element => _box;

    /// <summary>Draws <paramref name="text"/> in <paramref name="width"/> pixels, in <paramref name="foreground"/>.</summary>
    /// <param name="text">The line.</param>
    /// <param name="width">The width of the box.</param>
    /// <param name="foreground">The ink for both copies and the trimmed line.</param>
    public void Show(string text, double width, Brush foreground)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(foreground);
        _box.Visibility = Visibility.Visible;
        _box.Width = width;
        _trim.Width = width;
        if (!string.Equals(text, _text, StringComparison.Ordinal))
        {
            _text = text;
            _line.Text = text;
            _copy.Text = text;
            _trim.Text = text;
            _line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (_line.DesiredSize.Width > 0)
            {
                _natural = _line.DesiredSize.Width;
            }

            if (_line.DesiredSize.Height > 0)
            {
                _canvas.Height = _line.DesiredSize.Height;
            }
        }

        Ink(foreground);
        Settle();
    }

    /// <summary>Paints both copies and the trimmed line with <paramref name="foreground"/>.</summary>
    /// <param name="foreground">The ink.</param>
    public void Ink(Brush foreground)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        _line.Foreground = foreground;
        _copy.Foreground = foreground;
        _trim.Foreground = foreground;
    }

    /// <summary>Stops the scroll and drops the line. The next <see cref="Show"/> starts again.</summary>
    public void Close() => Halt();

    /// <summary>Stops the compositor animation and forgets the loop, so a later show measures again.</summary>
    private void Halt()
    {
        _loopKey = "";
        _natural = 0;
        _text = "";
        _copy.Visibility = Visibility.Collapsed;
        _trim.Visibility = Visibility.Collapsed;
        _box.Visibility = Visibility.Collapsed;
        Motion.Scroll(_canvas, 0, TimeSpan.Zero);
    }

    /// <summary>Starts, replaces, or stops the loop from the measured line and the box. An unchanged line keeps the loop it has.</summary>
    private void Settle()
    {
        if (_box.Visibility != Visibility.Visible || _text.Length == 0)
        {
            return;
        }

        var boxWidth = _box.ActualWidth > 0 ? _box.ActualWidth : _box.Width;
        var boxHeight = _box.ActualHeight > 0 ? _box.ActualHeight : _canvas.Height;
        if (!double.IsFinite(boxWidth) || boxWidth <= 0 || !double.IsFinite(boxHeight) || boxHeight <= 0)
        {
            return;
        }

        // Only the sides are cut. The line's own height can be a pixel or two short of what it draws, and the box would shave the descenders.
        _clip.Rect = new Rect(0, -boxHeight, boxWidth, boxHeight * 3);

        // Effects off matches Motion.FadeIn: the line stays put. It is trimmed, since it cannot scroll.
        var animations = Motion.Enabled();
        if (animations && _natural <= 0)
        {
            return;
        }

        var scroll = animations
            ? TextScroll.For(_natural, boxWidth, TextScroll.Gap, TextScroll.Speed)
            : TextScroll.Still;
        var key = (animations ? scroll.Runs ? "run" : "still" : "trim")
            + "\u001f"
            + _text
            + "\u001f"
            + Math.Round(scroll.Travel, 1).ToString(CultureInfo.InvariantCulture);
        if (key == _loopKey)
        {
            return;
        }

        _loopKey = key;
        if (!scroll.Runs)
        {
            Motion.Scroll(_canvas, 0, TimeSpan.Zero);
            _copy.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(_line, 0);
            if (animations)
            {
                _line.Opacity = 1;
                _trim.Visibility = Visibility.Collapsed;
                return;
            }

            _line.Opacity = 0;
            _trim.Visibility = Visibility.Visible;
            return;
        }

        _trim.Visibility = Visibility.Collapsed;
        _line.Opacity = 1;
        _copy.Visibility = Visibility.Visible;
        Canvas.SetLeft(_line, 0);
        Canvas.SetLeft(_copy, scroll.Travel);
        Motion.Scroll(_canvas, scroll.Travel, scroll.Duration);
    }

    private static TextBlock Line() => new()
    {
        Style = Paint.Style("BarTextStyle"),
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Left,
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.NoWrap,
    };
}
