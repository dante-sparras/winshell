using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Winshell.Kit;

/// <summary>
/// Draws a notification for the bell's panel and for a toast. A toast leads with the program's logo, or the sender's
/// picture, beside the program's name, a bold title, and the body. A card in the panel sits under its program's heading,
/// so it leaves the logo and name out. Either shows the time, which turns into a close button under the pointer,
/// an optional wide image, and the notification's buttons.
/// </summary>
public static class NotificationCards
{
    private const string Close = "";

    /// <summary>The chevron that points down at a stack and turns up over one that is spread out.</summary>
    internal const string Chevron = "";

    /// <summary>The space a framed card keeps under it, which a stack's edge closes to sit against its card.</summary>
    internal const double CardGap = 4;

    /// <summary>The program's logo beside a program's name over its cards.</summary>
    internal const double HeadingLogo = 20;

    // The program's logo at the head of a toast, and the small one beside its name when a sender's picture leads instead.
    private const double LeadLogo = 32;
    private const double InlineLogo = 16;
    private const double Avatar = 40;
    private const double Corner = 24;

    /// <summary>A card. A click, Enter, or Space sends its command. Delete sends its close command.</summary>
    /// <param name="card">What the card shows.</param>
    /// <param name="send">Sends a command to the plugin.</param>
    /// <param name="framed">True draws the card's own fill and outline, as in the panel. A toast is already a frame.</param>
    public static FrameworkElement Card(ToastCard card, Action<string> send, bool framed) => Card(card, send, framed, out _);

    /// <summary>
    /// A card whose time line can be written again. The time is the one part of a card that changes by itself, once a minute,
    /// and a card made again for it reads and decodes its picture again.
    /// </summary>
    /// <param name="card">What the card shows.</param>
    /// <param name="send">Sends a command to the plugin.</param>
    /// <param name="framed">True draws the card's own fill and outline, as in the panel. A toast is already a frame.</param>
    /// <param name="time">Writes the time line.</param>
    public static FrameworkElement Card(ToastCard card, Action<string> send, bool framed, out Action<string> time)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(send);
        var when = Line(card.Time, TypeRamp.Caption, Paint.Muted);
        time = text => when.Text = text;
        return Card(card, send, framed, when);
    }

    private static FrameworkElement Card(ToastCard card, Action<string> send, bool framed, TextBlock time)
    {
        time.HorizontalAlignment = HorizontalAlignment.Right;
        time.VerticalAlignment = VerticalAlignment.Center;
        var close = card.Dismiss.Length > 0 ? CloseButton("Dismiss", () => send(card.Dismiss)) : null;
        // The corner is as tall as the close button, and is lifted so the time sits on the first line of text.
        var corner = new Grid
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = Corner,
            MinHeight = Corner,
            Margin = new Thickness(0, card.ShowApp ? -4 : -2, 0, 0),
        };
        corner.Children.Add(time);
        if (close is not null)
        {
            close.Visibility = Visibility.Collapsed;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            corner.Children.Add(close);
        }

        var avatar = card.Picture.Length > 0 && Decode(card.Picture, (int)Avatar) is { } face
            ? new Ellipse { Width = Avatar, Height = Avatar, Fill = new ImageBrush { ImageSource = face, Stretch = Stretch.UniformToFill } }
            : null;
        var text = new StackPanel { Spacing = Spacing.LabelGap };
        if (card.ShowApp)
        {
            // A sender's picture takes the lead, so the program's logo moves beside its name.
            var source = new Grid { ColumnSpacing = 6 };
            source.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            source.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (avatar is not null)
            {
                source.Children.Add(Logo(card.AppIcon, InlineLogo));
            }

            var app = Line(card.App, TypeRamp.Caption, Paint.Muted);
            app.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(app, avatar is null ? 0 : 1);
            Grid.SetColumnSpan(app, avatar is null ? 2 : 1);
            source.Children.Add(app);
            text.Children.Add(source);
        }

        var title = Line(card.Title, TypeRamp.BodyStrong, Paint.Foreground);
        title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxLines = 2;
        text.Children.Add(title);
        if (card.Body.Length > 0)
        {
            var words = Line(card.Body, TypeRamp.Body, Paint.Muted);
            words.TextWrapping = TextWrapping.Wrap;
            words.MaxLines = 4;
            text.Children.Add(words);
        }

        // Only the columns that hold something are made, so text with nothing before it starts at the card's edge.
        FrameworkElement? lead = avatar is not null ? avatar : card.ShowApp ? Logo(card.AppIcon, LeadLogo) : null;
        var content = new Grid { ColumnSpacing = Spacing.ControlGap };
        if (lead is not null)
        {
            lead.VerticalAlignment = VerticalAlignment.Top;
            lead.Margin = new Thickness(0, 2, 0, 0);
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(lead);
        }

        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, content.ColumnDefinitions.Count - 1);
        content.Children.Add(text);
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(corner, content.ColumnDefinitions.Count - 1);
        content.Children.Add(corner);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(content);
        // As wide as the panel, which is the widest a card is drawn.
        if (card.Image.Length > 0 && Decode(card.Image, ToastCard.PanelWidth) is { } wide)
        {
            body.Children.Add(new Border
            {
                CornerRadius = Corners.Control,
                MaxHeight = 180,
                Child = new Image { Source = wide, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Stretch },
            });
        }

        if (card.Actions.Count > 0)
        {
            var actions = new Grid { ColumnSpacing = 8 };
            for (var index = 0; index < card.Actions.Count; index++)
            {
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var action = card.Actions[index];
                var button = new Button
                {
                    Content = Line(action.Label, TypeRamp.Body, Paint.Foreground),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    MinHeight = 32,
                    Padding = new Thickness(8, 5, 8, 6),
                    CornerRadius = Corners.Control,
                    Background = Paint.Hover,
                    BorderBrush = Paint.Stroke,
                    BorderThickness = new Thickness(1),
                };
                AutomationProperties.SetName(button, action.Label);
                var command = action.Command;
                button.Click += (_, _) => send(command);
                Grid.SetColumn(button, index);
                actions.Children.Add(button);
            }

            body.Children.Add(actions);
        }

        var frame = new Border
        {
            Padding = framed ? Spacing.Card : new Thickness(0),
            CornerRadius = Corners.Overlay,
            Background = framed ? Paint.Hover : Paint.Clear,
            BorderBrush = framed ? Paint.Stroke : Paint.Clear,
            BorderThickness = new Thickness(framed ? 1 : 0),
            BackgroundTransition = new BrushTransition { Duration = UiMotion.Faster },
            Child = body,
        };
        var host = new ContentControl
        {
            Content = frame,
            IsTabStop = card.Command.Length > 0,
            UseSystemFocusVisuals = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Margin = framed ? new Thickness(0, 0, 0, CardGap) : new Thickness(0),
        };
        AutomationProperties.SetName(host, card.App + ", " + card.Title + (card.Body.Length > 0 ? ", " + card.Body : ""));
        host.PointerEntered += (_, _) =>
        {
            if (framed)
            {
                frame.Background = Paint.Selected;
            }

            if (close is not null)
            {
                // The time keeps its room while it is unseen. Collapsed, it let the text beside it wrap again, and the card changed height under the pointer.
                time.Opacity = 0;
                close.Visibility = Visibility.Visible;
            }
        };
        host.PointerExited += (_, _) =>
        {
            if (framed)
            {
                frame.Background = Paint.Hover;
            }

            if (close is not null)
            {
                close.Visibility = Visibility.Collapsed;
                time.Opacity = 1;
            }
        };
        host.Tapped += (_, args) =>
        {
            if (card.Command.Length == 0 || args.OriginalSource is DependencyObject source && InButton(source, host))
            {
                return;
            }

            args.Handled = true;
            send(card.Command);
        };
        host.KeyDown += (_, args) =>
        {
            if (args.Key is VirtualKey.Enter or VirtualKey.Space && card.Command.Length > 0)
            {
                args.Handled = true;
                send(card.Command);
            }
            else if (args.Key == VirtualKey.Delete && card.Dismiss.Length > 0)
            {
                args.Handled = true;
                send(card.Dismiss);
            }
        };
        return host;
    }

    /// <summary>
    /// A program in the panel: its heading, with the button that clears it, and the edges of the stack under its newest card.
    /// </summary>
    /// <param name="app">The program's name.</param>
    /// <param name="icon">The program's logo, as image bytes. Null draws a bell.</param>
    /// <param name="clear">Clears the program's notifications.</param>
    /// <param name="fold">Spreads the program's stack out, or stacks it again.</param>
    /// <returns>The group. It draws no stack until <see cref="NotificationGroup.Show"/> gives it one.</returns>
    public static NotificationGroup Group(string app, byte[]? icon, Action clear, Action fold)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(clear);
        ArgumentNullException.ThrowIfNull(fold);
        return new NotificationGroup(app, icon, clear, fold);
    }

    /// <summary>The controls at the right of the bell panel's heading: Clear all, and the bell that turns do not disturb on and off.</summary>
    /// <param name="clearAll">Removes every notification.</param>
    /// <param name="silence">Turns do not disturb on or off.</param>
    /// <returns>The controls. They draw their state once <see cref="NotificationHeading.Show"/> gives it.</returns>
    public static NotificationHeading Heading(Action clearAll, Action silence)
    {
        ArgumentNullException.ThrowIfNull(clearAll);
        ArgumentNullException.ThrowIfNull(silence);
        return new NotificationHeading(clearAll, silence);
    }

    /// <summary>A quiet line in the middle of the panel.</summary>
    /// <param name="value">The line.</param>
    /// <returns>The line.</returns>
    public static FrameworkElement Placeholder(string value) => new TextBlock
    {
        Text = value,
        Style = Paint.Style("PanelTextStyle"),
        FontSize = TypeRamp.Body,
        Foreground = Paint.Muted,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 28, 0, 28),
    };

    /// <summary>What the bell's panel shows when Windows is holding nothing: a faint bell over a quiet line.</summary>
    /// <param name="value">The line.</param>
    /// <param name="say">Changes the line.</param>
    /// <returns>The bell and the line.</returns>
    public static FrameworkElement Empty(string value, out Action<string> say)
    {
        var line = Line(value, TypeRamp.Body, Paint.Muted);
        line.HorizontalAlignment = HorizontalAlignment.Center;
        var column = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) };
        column.Children.Add(new FontIcon { Glyph = Glyphs.Named("bell"), FontFamily = Fonts.Icons, FontSize = 28, Foreground = Paint.Muted, Opacity = 0.5 });
        column.Children.Add(line);
        say = text => line.Text = text;
        return column;
    }

    /// <summary>
    /// Decodes an image file at <paramref name="width"/> or less. Null when the file is gone, empty, larger than
    /// <see cref="ToastCard.LargestPicture"/>, or does not decode. The file is read here, when the picture is drawn,
    /// so nothing holds its bytes for a notification that is not on screen.
    /// </summary>
    /// <param name="path">The image file's full path.</param>
    /// <param name="width">The widest it is drawn, in device-independent pixels.</param>
    /// <returns>The image, or null.</returns>
    public static BitmapImage? Decode(string path, int width)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > ToastCard.LargestPicture)
            {
                return null;
            }

            var image = new BitmapImage { DecodePixelWidth = width, DecodePixelType = DecodePixelType.Logical };
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            image.SetSource(stream.AsRandomAccessStream());
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes image bytes at <paramref name="width"/> or less. Null when they do not decode.
    /// The width is in device-independent pixels: the decoder scales it by the display's scale itself, so twice the drawn
    /// width decodes four times the pixels the picture shows.
    /// </summary>
    /// <param name="bytes">The image file.</param>
    /// <param name="width">The widest it is drawn, in device-independent pixels.</param>
    /// <returns>The image, or null.</returns>
    public static BitmapImage? Decode(byte[] bytes, int width)
    {
        try
        {
            var image = new BitmapImage { DecodePixelWidth = width, DecodePixelType = DecodePixelType.Logical };
            using var stream = new MemoryStream(bytes);
            image.SetSource(stream.AsRandomAccessStream());
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A program's logo at <paramref name="size"/>. A program with none gets a bell on a small tile in the accent's wash.</summary>
    /// <param name="icon">The logo's image bytes, or null.</param>
    /// <param name="size">The edge in device-independent pixels.</param>
    /// <returns>The logo.</returns>
    public static FrameworkElement Logo(byte[]? icon, double size)
    {
        if (icon is { Length: > 0 } && Decode(icon, (int)size) is { } source)
        {
            return new Image { Source = source, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Round(size / 4)),
            Background = Paint.Tint,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = Glyphs.Named("bell"),
                FontFamily = Fonts.Icons,
                FontSize = Math.Round(size * 0.56),
                Foreground = Paint.AccentText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>One line of panel text that ends in an ellipsis when it runs out of room.</summary>
    internal static TextBlock Line(string text, double size, Brush brush) => new()
    {
        Text = text,
        Style = Paint.Style("PanelTextStyle"),
        FontSize = size,
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextLineBounds = TextLineBounds.Full,
    };

    internal static Button CloseButton(string name, Action click)
    {
        var button = CornerButton(new FontIcon { Glyph = Close, FontFamily = Fonts.Icons, FontSize = 10, Foreground = Paint.Foreground }, click);
        Name(button, name);
        return button;
    }

    /// <summary>A small square button for a card's or a heading's corner.</summary>
    internal static Button CornerButton(FontIcon icon, Action click)
    {
        var button = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Width = Corner,
            Height = Corner,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = Corners.Control,
            Background = Paint.Hit,
            VerticalAlignment = VerticalAlignment.Center,
            Content = icon,
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>What a screen reader calls <paramref name="button"/>, which is also its hover text.</summary>
    internal static void Name(Button button, string name)
    {
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
    }

    private static bool InButton(DependencyObject source, DependencyObject host)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, host); node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The right of the bell panel's heading. Clear all shows while there is anything to clear. The bell turns do not disturb
/// on and off, and wears the accent's wash and a struck-through bell while it is on.
/// </summary>
public sealed class NotificationHeading
{
    private readonly FontIcon _bell;

    internal NotificationHeading(Action clearAll, Action silence)
    {
        ClearAll = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Content = NotificationCards.Line("Clear all", TypeRamp.Caption, Paint.Foreground),
            Height = 32,
            MinWidth = 0,
            Padding = new Thickness(Spacing.ControlPaddingX, 0, Spacing.ControlPaddingX, 0),
            CornerRadius = Corners.Control,
            Background = Paint.Hit,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(ClearAll, "Clear all");
        ClearAll.Click += (_, _) => clearAll();
        _bell = new FontIcon { FontFamily = Fonts.Icons, FontSize = 16, Foreground = Paint.Foreground };
        Silence = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Content = _bell,
            Width = 32,
            Height = 32,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = Corners.Control,
            Background = Paint.Hit,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Silence.Click += (_, _) => silence();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing.RowGap, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(ClearAll);
        row.Children.Add(Silence);
        Element = row;
    }

    /// <summary>Both controls, for the heading's right.</summary>
    public FrameworkElement Element { get; }

    /// <summary>The button that removes every notification.</summary>
    public Button ClearAll { get; }

    /// <summary>The bell that turns do not disturb on and off.</summary>
    public Button Silence { get; }

    /// <summary>Draws whether there is anything to clear, and whether do not disturb is on.</summary>
    /// <param name="any">True while Windows is holding a notification.</param>
    /// <param name="silenced">True while do not disturb is on.</param>
    public void Show(bool any, bool silenced)
    {
        ClearAll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _bell.Glyph = Glyphs.Named(silenced ? "bell-off" : "bell");
        _bell.Foreground = silenced ? Paint.AccentText : Paint.Foreground;
        Silence.Background = silenced ? Paint.Tint : Paint.Hit;
        NotificationCards.Name(Silence, silenced ? "Do not disturb is on" : "Do not disturb");
    }
}

/// <summary>
/// One program in the bell's panel. Its heading has the program's logo and name, a chevron while it has more than one
/// notification, and the clear button. A stacked program shows its newest card with the edges of the rest under it,
/// and the first edge says how many there are. That edge and the chevron spread the stack out. The chevron stacks it again.
/// </summary>
public sealed class NotificationGroup
{
    private readonly string _app;
    private readonly Button _fold;
    private readonly FontIcon _chevron;
    private readonly Button _more;
    private readonly TextBlock _count;
    private readonly Border _second;
    private readonly StackPanel _edge;
    private bool _drawn;

    internal NotificationGroup(string app, byte[]? icon, Action clear, Action fold)
    {
        _app = app;
        // The buttons sit where each card's own close button does, inside the card's border and padding.
        var heading = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 10, Spacing.Card.Right + 1, 4) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(NotificationCards.Logo(icon, NotificationCards.HeadingLogo));
        var name = NotificationCards.Line(app, TypeRamp.BodyStrong, Paint.Foreground);
        name.FontWeight = FontWeights.SemiBold;
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        heading.Children.Add(name);
        _chevron = new FontIcon { Glyph = NotificationCards.Chevron, FontFamily = Fonts.Icons, FontSize = 10, Width = 16, Height = 16, Foreground = Paint.Foreground };
        _fold = NotificationCards.CornerButton(_chevron, fold);
        _fold.Visibility = Visibility.Collapsed;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(_fold);
        buttons.Children.Add(NotificationCards.CloseButton("Clear notifications from " + app, clear));
        Grid.SetColumn(buttons, 2);
        heading.Children.Add(buttons);
        Heading = heading;

        // Each edge is one card further down the stack: narrower by the card's corner, so its sides meet the flat of the edge above,
        // and fainter, so the stack falls away under the card.
        var corner = Corners.Overlay.BottomLeft;
        var inset = Math.Max(8, corner);
        _count = NotificationCards.Line("", TypeRamp.Caption, Paint.Muted);
        _count.VerticalAlignment = VerticalAlignment.Center;
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(_count);
        label.Children.Add(new FontIcon { Glyph = NotificationCards.Chevron, FontFamily = Fonts.Icons, FontSize = 8, Foreground = Paint.Muted, VerticalAlignment = VerticalAlignment.Center });
        _more = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Content = label,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(0, 0, Math.Max(0, corner - 1), Math.Max(0, corner - 1)),
            Background = Paint.Hover,
        };
        _more.Click += (_, _) => fold();
        var first = new Border
        {
            Margin = new Thickness(inset, 0, inset, 0),
            BorderBrush = Paint.Stroke,
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(0, 0, corner, corner),
            Opacity = 0.8,
            Child = _more,
        };
        _second = new Border
        {
            Height = 6,
            Margin = new Thickness(inset * 2, 0, inset * 2, 0),
            Background = Paint.Hover,
            BorderBrush = Paint.Stroke,
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(0, 0, Math.Min(corner, 6), Math.Min(corner, 6)),
            Opacity = 0.5,
        };
        _edge = new StackPanel { Margin = new Thickness(0, -NotificationCards.CardGap, 0, NotificationCards.CardGap), Visibility = Visibility.Collapsed };
        _edge.Children.Add(first);
        _edge.Children.Add(_second);
    }

    /// <summary>The program's logo and name, the chevron, and the clear button.</summary>
    public FrameworkElement Heading { get; }

    /// <summary>Puts the stack's edges under <paramref name="cards"/>, against the last card.</summary>
    /// <param name="cards">The program's cards, each a framed <see cref="NotificationCards.Card(ToastCard, Action{string}, bool)"/>.</param>
    /// <returns>The cards with the edges under them.</returns>
    public FrameworkElement Cards(UIElement cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var column = new StackPanel();
        column.Children.Add(cards);
        column.Children.Add(_edge);
        return column;
    }

    /// <summary>Draws <paramref name="stack"/>: the chevron's turn, how many cards the edges stand for, and whether there are edges at all.</summary>
    /// <param name="stack">The program's stack now.</param>
    public void Show(NotificationStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        // The first drawing is the panel opening, which moves nothing.
        var moves = _drawn && Motion.Enabled();
        _drawn = true;
        _fold.Visibility = stack.Folds ? Visibility.Visible : Visibility.Collapsed;
        NotificationCards.Name(_fold, (stack.Expanded ? "Stack notifications from " : "Show all notifications from ") + _app);
        Motion.Turn(_chevron, stack.Expanded ? 180 : 0, moves);
        var stacked = stack.Hidden > 0;
        if (stacked)
        {
            _count.Text = stack.Label;
            AutomationProperties.SetName(_more, stack.Label + " from " + _app);
            _second.Visibility = stack.Hidden > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (stacked && moves && _edge.Visibility == Visibility.Collapsed)
        {
            Motion.FadeIn(_edge, UiMotion.Fast);
        }

        _edge.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
    }
}
