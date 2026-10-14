using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Winshell.Kit;

/// <summary>
/// The column a panel's rows stack in, <see cref="Spacing.RowGap"/> apart. A separator's line sits
/// <see cref="Spacing.SeparatorGap"/> from what the row beside it draws, whatever margin that row carries and
/// however deep in a group either of them is, so every line in every panel has the same room (<see cref="RowColumn"/>).
/// The Kit stacks every panel, card, and keyed list in one. A plugin that fills a column of its own makes one too.
/// </summary>
public sealed class Rows : Panel
{
    private static readonly DependencyProperty SeparatorProperty =
        DependencyProperty.RegisterAttached("Separator", typeof(bool), typeof(Rows), new PropertyMetadata(false));

    private readonly List<RowBox> _boxes = [];
    private RowLayout _layout = new([], 0);

    /// <summary>Marks <paramref name="line"/> as a separator's line, so a column gives it the separator gap.</summary>
    /// <typeparam name="TElement">The element's type.</typeparam>
    /// <param name="line">The line, with the separator gap as its own top and bottom margin.</param>
    /// <returns>The same element.</returns>
    internal static TElement Separator<TElement>(TElement line)
        where TElement : FrameworkElement
    {
        line.SetValue(SeparatorProperty, true);
        return line;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var room = new Size(availableSize.Width, double.PositiveInfinity);
        var width = 0d;
        foreach (var child in Children)
        {
            child.Measure(room);
            width = Math.Max(width, child.DesiredSize.Width);
        }

        _layout = Lay();
        return new Size(width, _layout.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Read once, as Lay does: each read of the collection, its count, or a child is a call into the framework.
        var children = Children;
        var count = children.Count;
        if (_layout.Tops.Count != count)
        {
            _layout = Lay();
        }

        for (var i = 0; i < count; i++)
        {
            var child = children[i];
            child.Arrange(new Rect(0, _layout.Tops[i], finalSize.Width, child.DesiredSize.Height));
        }

        return new Size(finalSize.Width, Math.Max(finalSize.Height, _layout.Height));
    }

    private RowLayout Lay()
    {
        // Every layout pass of an open panel comes here, for every column in it. The boxes are refilled where they are.
        _boxes.Clear();
        var children = Children;
        var count = children.Count;
        for (var i = 0; i < count; i++)
        {
            var child = children[i];
            _boxes.Add(new RowBox(child.DesiredSize.Height, Edge(child, top: true), Edge(child, top: false)));
        }

        return RowColumn.Lay(_boxes, Spacing.RowGap, Spacing.SeparatorGap);
    }

    /// <summary>What <paramref name="element"/> shows the row above or below it: through a group, the first or last row that draws.</summary>
    private static RowEdge Edge(UIElement element, bool top)
    {
        if (element is not FrameworkElement framework)
        {
            return default;
        }

        var margin = top ? framework.Margin.Top : framework.Margin.Bottom;
        if ((bool)framework.GetValue(SeparatorProperty))
        {
            return new RowEdge(margin, true);
        }

        if (framework is Rows group)
        {
            // The first row that draws, from the top or from the bottom. Only the rows before it are asked for their size.
            var rows = group.Children;
            var count = rows.Count;
            for (var step = 0; step < count; step++)
            {
                var row = rows[top ? step : count - 1 - step];
                if (row.DesiredSize.Height > 0)
                {
                    var inner = Edge(row, top);
                    return new RowEdge(margin + inner.Margin, inner.Separator);
                }
            }
        }

        return new RowEdge(margin, false);
    }
}
