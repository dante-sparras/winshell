using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Winshell.Kit;

/// <summary>
/// A row of bar chips, such as the workspace numbers, with the accent line under the current one.
/// The line slides to the next chip, like the Windows taskbar's running-app line.
/// </summary>
internal sealed class ChipStrip
{
    private const double Edge = 16;
    private const double Line = 3;

    // A window keeps its icon bytes while it is open, so a chip rebuild reuses the decoded image instead of decoding the PNG again.
    // The image goes with the bytes once the window closes. Every caller is on the UI thread.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], BitmapImage> Bitmaps = new();

    private readonly bool _vertical;
    private readonly Action<int> _pick;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = Spacing.RowGap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _marker = new()
    {
        Width = Edge,
        Height = Line,
        CornerRadius = new CornerRadius(Line / 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 2),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private IReadOnlyList<BarChip>? _live;
    private string _drawn = "";
    private bool _empty;

    /// <summary>Creates an empty strip. <paramref name="pick"/> hears a chip's tag when it is clicked.</summary>
    public ChipStrip(bool vertical, Action<int> pick)
    {
        _vertical = vertical;
        _pick = pick;
        // Along a vertical bar the chips stack, and the current one's line runs down its outer side.
        if (vertical)
        {
            _chips.Orientation = Orientation.Vertical;
            _marker.Width = Line;
            _marker.Height = Edge;
            _marker.VerticalAlignment = VerticalAlignment.Top;
            _marker.Margin = new Thickness(0);
        }

        _marker.Background = Paint.Accent;
        Element.Children.Add(_chips);
        Element.Children.Add(_marker);
        _chips.SizeChanged += (_, _) => PlaceMarker(animate: false);
    }

    /// <summary>The chips and their line.</summary>
    public Grid Element { get; } = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>True while the strip has chips on screen.</summary>
    public bool Any => _chips.Children.Count > 0;

    /// <summary>
    /// Moves the highlight when <paramref name="chips"/> are the chips on screen with another one current.
    /// False when they differ in more than that, so <see cref="Show"/> has to draw them.
    /// </summary>
    public bool Retarget(IReadOnlyList<BarChip> chips)
    {
        // Moving the highlight used to rebuild every chip and hash every icon byte.
        // The buttons are already on screen. Swap the fill and leave the icons where they are.
        if (_live is null || chips.Count == 0 || chips.Count != _chips.Children.Count || !ChipBody.Same(_live, chips))
        {
            return false;
        }

        var moved = !ChipBody.SameAccent(_live, chips);
        if (moved)
        {
            Retint(chips);
        }

        _live = chips;
        if (moved)
        {
            PlaceMarker(animate: true);
        }

        return true;
    }

    /// <summary>Shows <paramref name="chips"/>. A move of the current chip only swaps two fills and slides the line.</summary>
    public void Show(IReadOnlyList<BarChip> chips)
    {
        if (Retarget(chips))
        {
            return;
        }

        // An item with no chips publishes none on every face. The strip is already hidden for them.
        if (chips.Count == 0 && _empty)
        {
            return;
        }

        _empty = chips.Count == 0;
        Element.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The mark leaves the text out, and a chip can keep its tag and change its text.
        var mark = ChipMark.Of(chips) + "\n" + string.Join('\n', chips.Select(chip => chip.Text));
        _live = chips.Count > 0 ? chips : null;
        if (mark == _drawn && chips.Count == _chips.Children.Count)
        {
            PlaceMarker(animate: false);
            return;
        }

        _drawn = mark;
        _chips.Children.Clear();
        foreach (var chip in chips)
        {
            var tag = chip.Tag;
            var button = new Button
            {
                Style = Paint.Style("BarButtonStyle"),
                Height = _vertical ? double.NaN : Spacing.BarItem,
                MinWidth = _vertical ? Spacing.BarItemThin : Spacing.BarItem,
                MinHeight = _vertical ? Spacing.BarItemThin : 0,
                Padding = _vertical ? new Thickness(0, 6, 0, 6) : new Thickness(Spacing.PanelGap, 0, Spacing.PanelGap, 0),
                CornerRadius = Corners.Control,
                Background = chip.Accent ? Paint.Selected : Paint.Hit,
                Content = Content(chip),
            };
            button.Click += (_, _) => _pick(tag);

            // The line is placed when the chips change size. New chips of exactly the old size change none,
            // so the current one also places it once it is laid out.
            if (chip.Accent)
            {
                button.Loaded += (_, _) => PlaceMarker(animate: false);
            }

            _chips.Children.Add(button);
        }

        PlaceMarker(animate: false);
    }

    /// <summary>The decoded image for one icon's PNG bytes, kept while the bytes live.</summary>
    public static BitmapImage Bitmap(byte[] png) => Bitmaps.GetValue(png, Decode);

    /// <summary>Puts the line under the current chip. Hidden when no chip is current or the chips are not laid out yet.</summary>
    private void PlaceMarker(bool animate)
    {
        var index = -1;
        if (_live is not null)
        {
            for (var i = 0; i < _live.Count; i++)
            {
                if (_live[i].Accent)
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0 || index >= _chips.Children.Count || _chips.Children[index] is not FrameworkElement chip || chip.ActualWidth <= 0)
        {
            _marker.Visibility = Visibility.Collapsed;
            return;
        }

        var origin = chip.TransformToVisual(Element).TransformPoint(new Point(0, 0));
        var shown = _marker.Visibility == Visibility.Visible;
        _marker.Visibility = Visibility.Visible;
        if (_vertical)
        {
            Motion.Slide(_marker, 0, animate && shown, origin.Y + ((chip.ActualHeight - _marker.Height) / 2));
            return;
        }

        Motion.Slide(_marker, origin.X + ((chip.ActualWidth - _marker.Width) / 2), animate && shown);
    }

    private void Retint(IReadOnlyList<BarChip> chips)
    {
        for (var i = 0; i < chips.Count; i++)
        {
            if (_chips.Children[i] is Button button)
            {
                button.Background = chips[i].Accent ? Paint.Selected : Paint.Hit;
                if (button.Content is StackPanel { Children: [TextBlock number, ..] })
                {
                    number.FontWeight = chips[i].Accent ? FontWeights.SemiBold : FontWeights.Normal;
                    number.Foreground = chips[i].Accent ? Paint.Foreground : Paint.Muted;
                }
            }
        }
    }

    private StackPanel Content(BarChip chip)
    {
        var row = new StackPanel { Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = chip.Text,
            MinWidth = 12,
            TextAlignment = TextAlignment.Center,
            FontWeight = chip.Accent ? FontWeights.SemiBold : FontWeights.Normal,
            Style = Paint.Style("BarTextStyle"),
            Foreground = chip.Accent ? Paint.Foreground : Paint.Muted,
        });
        foreach (var png in chip.Icons)
        {
            row.Children.Add(new Image { Source = Bitmap(png), Width = TypeRamp.Icon, Height = TypeRamp.Icon });
        }

        return row;
    }

    private static BitmapImage Decode(byte[] png)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.SetSource(stream.AsRandomAccessStream());
        return image;
    }
}
