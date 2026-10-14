using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.Kit;

/// <summary>
/// The accent line that shows where a dragged bar item would land. The host puts it on the bar and moves it
/// from one gap to the next as the pointer travels.
/// </summary>
public sealed class BarDropLine
{
    private const double Line = 2;

    private readonly bool _vertical;
    private bool _shown;

    /// <summary>Creates the line, hidden.</summary>
    /// <param name="vertical">True on a bar along the left or right edge, where the line lies across it.</param>
    public BarDropLine(bool vertical)
    {
        _vertical = vertical;
        Element = new Border
        {
            Background = Paint.Accent,
            CornerRadius = Corners.Circle(Line),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Width = vertical ? double.NaN : Line,
            Height = vertical ? Line : double.NaN,
            HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Stretch,
            Margin = vertical ? new Thickness(Spacing.RowGap, 0, Spacing.RowGap, 0) : new Thickness(0, Spacing.RowGap, 0, Spacing.RowGap),
        };
    }

    /// <summary>The line. It belongs in the cell the bar's sections share, so it is measured from the bar's start.</summary>
    public FrameworkElement Element { get; }

    /// <summary>Shows the line centred on <paramref name="along"/>. A line already on screen slides there.</summary>
    /// <param name="along">The point along the bar, from the bar's start.</param>
    public void Show(double along)
    {
        var at = along - (Line / 2);
        Element.Visibility = Visibility.Visible;
        Motion.Slide(Element, _vertical ? 0 : at, animate: _shown, _vertical ? at : 0);
        _shown = true;
    }

    /// <summary>Hides the line. The next <see cref="Show"/> places it without a slide.</summary>
    public void Hide()
    {
        Element.Visibility = Visibility.Collapsed;
        _shown = false;
    }
}
