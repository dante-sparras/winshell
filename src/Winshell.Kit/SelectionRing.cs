using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Winshell.Kit;

/// <summary>
/// The outline around the control the arrow keys are on. The host lays <see cref="Element"/> over a panel's rows,
/// in a parent that scrolls with them, and <see cref="Kit.Mark"/> says which control it goes around.
/// It follows that control as the rows are laid out again, and is hidden when the control leaves the panel.
/// The outline is two lines: the accent, and inside it the color text has on an accent fill. A chosen chip is filled with the accent,
/// so there the inner line is the one that shows, and on any other control the accent is.
/// </summary>
public sealed class SelectionRing
{
    private const double Weight = 2;

    private readonly Border _inner = new()
    {
        BorderBrush = Paint.OnAccent,
        BorderThickness = new Thickness(Weight),
        CornerRadius = Inside(Corners.Control),
        IsHitTestVisible = false,
    };

    private readonly Border _ring = new()
    {
        BorderBrush = Paint.Accent,
        BorderThickness = new Thickness(Weight),
        CornerRadius = Corners.Control,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    // A canvas asks for no room of its own, so the ring never makes the rows' parent larger than the rows.
    private readonly Canvas _layer = new() { IsHitTestVisible = false };
    private FrameworkElement? _around;
    private bool _following;

    /// <summary>Creates a ring that is around nothing.</summary>
    public SelectionRing()
    {
        _ring.Child = _inner;
        _layer.Children.Add(_ring);
    }

    /// <summary>The corners of the line drawn just inside a line with <paramref name="outer"/> corners, so the two run side by side around a bend.</summary>
    private static CornerRadius Inside(CornerRadius outer) => new(
        Math.Max(0, outer.TopLeft - Weight),
        Math.Max(0, outer.TopRight - Weight),
        Math.Max(0, outer.BottomRight - Weight),
        Math.Max(0, outer.BottomLeft - Weight));

    /// <summary>The layer the outline is drawn on. It takes no pointer input and no room, and shows nothing while nothing is selected.</summary>
    public FrameworkElement Element => _layer;

    /// <summary>Puts the ring around <paramref name="control"/>, or hides it for null.</summary>
    /// <param name="control">The selected control's element.</param>
    public void Show(UIElement? control)
    {
        _around = control as FrameworkElement;
        if (_around is not null)
        {
            // Read again each time, so a Host look that changed the accent since the last selection is the one drawn.
            _ring.BorderBrush = Paint.Accent;
            _inner.BorderBrush = Paint.OnAccent;
        }

        Follow(_around is not null);
        Place();
    }

    /// <summary>Hides the ring and lets go of the control, as when the panel closes.</summary>
    public void Hide() => Show(null);

    /// <summary>Hears every layout pass only while there is a control to follow, so a panel with no selection pays nothing.</summary>
    private void Follow(bool follow)
    {
        if (follow == _following)
        {
            return;
        }

        _following = follow;
        if (follow)
        {
            _layer.LayoutUpdated += OnLayout;
        }
        else
        {
            _layer.LayoutUpdated -= OnLayout;
        }
    }

    private void OnLayout(object? sender, object args) => Place();

    private void Place()
    {
        if (_around is not { } around || Bounds(around, _layer) is not { } bounds)
        {
            _ring.Visibility = Visibility.Collapsed;
            return;
        }

        // Written only when they moved, since this runs on every layout pass.
        if (Math.Abs(Canvas.GetLeft(_ring) - bounds.X) > 0.5)
        {
            Canvas.SetLeft(_ring, bounds.X);
        }

        if (Math.Abs(Canvas.GetTop(_ring) - bounds.Y) > 0.5)
        {
            Canvas.SetTop(_ring, bounds.Y);
        }

        // A ring that has no size yet reads NaN, and no comparison with NaN is true. Asked this way round, it gets its first size.
        if (!(Math.Abs(_ring.Width - bounds.Width) < 0.5))
        {
            _ring.Width = bounds.Width;
        }

        if (!(Math.Abs(_ring.Height - bounds.Height) < 0.5))
        {
            _ring.Height = bounds.Height;
        }

        var corner = around is Control { CornerRadius: var own } && own.TopLeft > 0 ? own : Corners.Control;
        if (_ring.CornerRadius != corner)
        {
            _ring.CornerRadius = corner;
            _inner.CornerRadius = Inside(corner);
        }

        _ring.Visibility = Visibility.Visible;
    }

    /// <summary>Where <paramref name="around"/> is drawn on <paramref name="layer"/>. Null when it is not on screen.</summary>
    private static Rect? Bounds(FrameworkElement around, UIElement layer)
    {
        if (around.ActualWidth <= 0 || around.ActualHeight <= 0 || around.Visibility == Visibility.Collapsed
            || VisualTreeHelper.GetParent(around) is null || VisualTreeHelper.GetParent(layer) is null)
        {
            return null;
        }

        try
        {
            return around.TransformToVisual(layer).TransformBounds(new Rect(0, 0, around.ActualWidth, around.ActualHeight));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The control left the panel between the refresh that selected it and this layout pass.
            return null;
        }
    }
}
