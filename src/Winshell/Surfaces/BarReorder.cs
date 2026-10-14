using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Winshell;

/// <summary>
/// A bar item dragged to another place on the bar. The item follows the pointer along the bar, a line shows where
/// it would land, and the release reports that place. <see cref="BarDrop"/> picks the place; this draws it.
/// </summary>
internal sealed class BarReorder
{
    private readonly Grid _root;
    private readonly bool _vertical;
    private readonly double _gap;
    private readonly Func<IReadOnlyList<BarSpan>> _measure;
    private readonly Action<string, BarDropTarget> _dropped;
    private readonly BarDropLine _line;
    private readonly List<UIElement> _raised = [];
    private IReadOnlyList<BarSpan> _spans = [];
    private FrameworkElement? _item;
    private string _id = "";
    private Point _pressed;
    private BarDropTarget? _target;

    // Where the line is shown, or null while it is hidden.
    private double? _marked;
    private bool _away;

    /// <summary>Drags items over <paramref name="root"/>, the cell the bar's sections share.</summary>
    /// <param name="root">The bar's grid. Spans and the pointer are measured in it.</param>
    /// <param name="vertical">True on a bar along the left or right edge.</param>
    /// <param name="gap">The space between two neighbouring items.</param>
    /// <param name="measure">Every shown item as it lies along the bar now.</param>
    /// <param name="dropped">Hears the id and the place when an item is released somewhere new. The item is still where it was let go.</param>
    public BarReorder(Grid root, bool vertical, double gap, Func<IReadOnlyList<BarSpan>> measure, Action<string, BarDropTarget> dropped)
    {
        _root = root;
        _vertical = vertical;
        _gap = gap;
        _measure = measure;
        _dropped = dropped;
        _line = new BarDropLine(vertical);
        Canvas.SetZIndex(_line.Element, 2);
    }

    /// <summary>True while an item is held. The places it can land were measured at the lift, so the bar's items keep their widths until the drop.</summary>
    public bool Held => _item is not null;

    /// <summary>Picks <paramref name="item"/> up. The bar is measured once, here, before anything moves.</summary>
    /// <param name="id">The id the item is shown under.</param>
    /// <param name="item">The slot.</param>
    /// <param name="pressed">Where the press that became this drag landed, in the window.</param>
    /// <param name="args">The move that started the drag.</param>
    public void Lift(string id, FrameworkElement item, Point pressed, PointerRoutedEventArgs args)
    {
        Cancel();
        _id = id;
        _item = item;
        _pressed = pressed;
        _spans = _measure();
        // A later section is drawn over an earlier one, so the item's section comes forward with it.
        for (DependencyObject? node = item; node is UIElement element && !ReferenceEquals(node, _root); node = VisualTreeHelper.GetParent(node))
        {
            Canvas.SetZIndex(element, 1);
            _raised.Add(element);
        }

        _root.Children.Add(_line.Element);
        Move(args);
    }

    /// <summary>The pointer moved with the item held.</summary>
    /// <param name="args">The move.</param>
    public void Move(PointerRoutedEventArgs args)
    {
        if (_item is null)
        {
            return;
        }

        var here = args.GetCurrentPoint(null).Position;
        var local = args.GetCurrentPoint(_root).Position;
        var (along, across) = _vertical ? (local.Y, local.X) : (local.X, local.Y);
        var (length, thickness) = _vertical ? (_root.ActualHeight, _root.ActualWidth) : (_root.ActualWidth, _root.ActualHeight);
        if (BarDrop.Away(across, thickness))
        {
            _target = null;
            _marked = null;
            _line.Hide();
            if (!_away)
            {
                // A release out here drops nothing, and the item goes home to say so.
                _away = true;
                Motion.Slide(_item, 0, animate: true);
            }

            return;
        }

        _away = false;
        Motion.Slide(_item, _vertical ? 0 : here.X - _pressed.X, animate: false, _vertical ? here.Y - _pressed.Y : 0);
        _target = BarDrop.Target(_spans, _id, along, length, _gap);
        if (_target is not { } target)
        {
            _marked = null;
            _line.Hide();
        }
        else if (_marked != target.Mark)
        {
            // The line slides to a gap once. Shown again on every move, its slide would start over each time.
            _marked = target.Mark;
            _line.Show(target.Mark);
        }
    }

    /// <summary>The pointer let go. An item over a new place is reported and stays put; any other slides home.</summary>
    /// <param name="args">The release. Its point decides the place: moves are coalesced, so the last one seen can be far behind it.</param>
    public void Drop(PointerRoutedEventArgs args)
    {
        if (_item is null)
        {
            return;
        }

        Move(args);
        var id = _id;
        var target = _target;
        End(home: target is null);
        if (target is { } place)
        {
            _dropped(id, place);
        }
    }

    /// <summary>Ends a drag that lost the pointer. The item slides home.</summary>
    public void Cancel()
    {
        if (_item is not null)
        {
            End(home: true);
        }
    }

    /// <summary>Slides <paramref name="item"/> back to where the bar laid it out.</summary>
    /// <param name="item">A slot a drag left out of place.</param>
    public static void Home(FrameworkElement item) => Motion.Slide(item, 0, animate: true);

    private void End(bool home)
    {
        var item = _item!;
        _item = null;
        _target = null;
        _marked = null;
        _away = false;
        _line.Hide();
        _root.Children.Remove(_line.Element);
        foreach (var element in _raised)
        {
            Canvas.SetZIndex(element, 0);
        }

        _raised.Clear();
        if (home)
        {
            Home(item);
        }
    }
}

