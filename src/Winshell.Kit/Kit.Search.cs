using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Winshell.Kit;

/// <summary>The Kit's search line.</summary>
public sealed partial class Kit
{
    // How Windows composes a caret: it inverts what is under it. The value has no name in the public enum.
    private const ElementCompositeMode Inverts = (ElementCompositeMode)3;
    private Func<char, bool>? _typeAhead;

    /// <summary>
    /// Gives a typed character to the panel's search line while the line does not have the keyboard.
    /// A panel a chord opened is typed into at once, and its window takes the keyboard a moment before the line inside it does.
    /// A letter typed in that moment would be lost. The panel's window hands it over here.
    /// </summary>
    /// <param name="letter">The character that was typed.</param>
    /// <returns>True when the search line took it. False when the panel has no search line, or the line has the keyboard and took it itself.</returns>
    public bool TypeAhead(char letter) => !char.IsControl(letter) && _typeAhead?.Invoke(letter) == true;

    /// <summary>
    /// A line to search with: a glyph and the text, with no box around it. Every change calls <paramref name="changed"/> at once,
    /// so a list under it follows each key. The line follows <paramref name="text"/> when the plugin changes it, such as clearing it.
    /// It takes the keyboard as the panel opens. A list moves its selection from <see cref="IKitPanel.OnKey"/>, so the keyboard stays here.
    /// </summary>
    /// <param name="prompt">The placeholder now.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends its text with <c>--text</c>.</param>
    /// <param name="text">The text the plugin holds. The line is rewritten only when this differs from what it shows.</param>
    /// <param name="changed">Takes the text after each change the person makes.</param>
    /// <param name="glyph">A Segoe Fluent glyph before the text. Empty uses the magnifier.</param>
    /// <param name="focus">False when the line is one row among others, so it neither takes the keyboard as the panel opens nor catches what is typed elsewhere.</param>
    /// <param name="size">The text size, from <see cref="TypeRamp"/>. Zero is the large size of a line that leads its panel, as the Menu's does.</param>
    /// <returns>The row.</returns>
    public UIElement SearchField(Func<string> prompt, string command, Func<string> text, Action<string> changed, string glyph = "", bool focus = true, double size = 0)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(changed);
        var clear = Paint.Clear;
        var box = new TextBox
        {
            FontFamily = Fonts.Text,
            FontSize = size > 0 ? size : TypeRamp.BodyLarge,
            Foreground = Paint.Foreground,
            Background = clear,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsSpellCheckEnabled = false,
            AcceptsReturn = false,
        };

        // The template paints its own fill and underline for each state. These keep the line bare in all of them.
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            box.Resources["TextControlBackground" + state] = clear;
            box.Resources["TextControlBorderBrush" + state] = clear;
        }

        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        box.Resources["TextControlForegroundFocused"] = Paint.Foreground;
        box.Resources["TextControlForegroundPointerOver"] = Paint.Foreground;
        box.Resources["TextControlPlaceholderForeground"] = Paint.Muted;
        box.Resources["TextControlPlaceholderForegroundFocused"] = Paint.Muted;
        box.Resources["TextControlPlaceholderForegroundPointerOver"] = Paint.Muted;

        // The caret is the line's own. The one Windows draws in a text box is in the text's color and cut out where
        // it crosses a letter, so between two letters it reads as one more stem, and it stops blinking after five seconds.
        // Windows gives it no brush, width, or time to set. So that one is cleared, and this one is drawn where it would be.
        var at = new TranslateTransform();
        var caret = new Rectangle
        {
            Fill = Paint.AccentText,
            Width = Motion.CaretWidth(),
            HorizontalAlignment = HorizontalAlignment.Left,
            // One line with no padding, so the caret is as tall as the box.
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransform = at,
        };
        var blink = Blink(caret);
        Shape? cleared = null;
        ScrollViewer? slide = null;
        void PlaceCaret()
        {
            // Windows makes its caret when the box first has the keyboard, and again when the writing direction turns.
            // It is found by how it is drawn, the only shape in a text box that inverts what is under it, and it is filled only once.
            if (cleared is null || VisualTreeHelper.GetParent(cleared) is null)
            {
                cleared = Under<Shape>(box, shape => shape.CompositeMode == Inverts);
                cleared?.Fill = clear;
            }

            // Text longer than the box slides under it, and some of that slide comes after the caret has moved.
            if (slide is null && (slide = Under<ScrollViewer>(box, _ => true)) is not null)
            {
                slide.ViewChanged += (_, _) => PlaceCaret();
            }

            if (box.FocusState == FocusState.Unfocused || box.SelectionLength != 0)
            {
                caret.Visibility = Visibility.Collapsed;
                blink?.Stop();
                return;
            }

            // Windows measures the edge of a letter, and after the last letter there is none: it is that letter's far edge.
            // The edge is measured from the start of the text, not from the box.
            var typed = box.Text.Length;
            var index = Math.Min(box.SelectionStart, typed);
            var edge = typed == 0 ? default
                : index < typed ? box.GetRectFromCharacterIndex(index, false)
                : box.GetRectFromCharacterIndex(typed - 1, true);
            // On a whole pixel of the screen, so a thin caret is a sharp line.
            var scale = box.XamlRoot?.RasterizationScale ?? 1;
            at.X = Math.Round((edge.X - (slide?.HorizontalOffset ?? 0)) * scale) / scale;
            caret.Visibility = Visibility.Visible;

            // Begun again from shown, so a caret that moved is seen where it went.
            blink?.Begin();
        }

        box.GotFocus += (_, _) => PlaceCaret();
        box.LostFocus += (_, _) => PlaceCaret();
        box.SelectionChanged += (_, _) => PlaceCaret();
        box.SizeChanged += (_, _) => PlaceCaret();
        box.Unloaded += (_, _) => blink?.Stop();

        var writing = false;
        Bind(prompt, value =>
        {
            box.PlaceholderText = value;
            AutomationProperties.SetName(box, value);
        });
        void Follow()
        {
            var held = text();
            if (!string.Equals(box.Text, held, StringComparison.Ordinal))
            {
                writing = true;
                box.Text = held;
                box.SelectionStart = held.Length;
                writing = false;
            }
        }

        Bind(text, _ => Follow());
        // TextChanged arrives after the next frame. A key pressed before that frame, such as Enter, would act on the text before it.
        box.TextChanging += (_, _) =>
        {
            if (writing)
            {
                return;
            }

            var typed = box.Text;
            Pressed(command, () => changed(typed))();

            // The plugin may not keep what was typed, as at a question. The line then goes back to what the plugin holds.
            if (!string.Equals(text(), typed, StringComparison.Ordinal))
            {
                box.DispatcherQueue.TryEnqueue(Follow);
            }
        };
        if (focus)
        {
            box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        }

        _typeAhead = !focus ? _typeAhead : letter =>
        {
            if (box.FocusState != FocusState.Unfocused)
            {
                return false;
            }

            // A search does not start with a space. The chord that opened the panel may still be held, and Space is on it.
            if (char.IsWhiteSpace(letter) && box.Text.Length == 0)
            {
                return true;
            }

            // The line has just opened, so the letter goes at its end. The plugin hears it here: a line that has not loaded reports nothing.
            var typed = box.Text + letter;
            writing = true;
            box.Text = typed;
            box.SelectionStart = typed.Length;
            writing = false;
            Pressed(command, () => changed(typed))();
            Follow();
            _ = box.Focus(FocusState.Programmatic);
            return true;
        };

        var mark = Glyph(glyph.Length > 0 ? glyph : Glyphs.Named("search"), TypeRamp.Icon, Paint.Muted);
        var line = new Grid { ColumnSpacing = Spacing.ControlGap, MinHeight = Spacing.Tile };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(mark);
        // The caret's place is measured from the box's corner, so the two share a cell that is exactly the box.
        var well = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { box, caret } };
        Grid.SetColumn(well, 1);
        line.Children.Add(well);

        // A click on the glyph or beside the text lands on the line, not the box. The keyboard goes back to the box.
        line.Background = Paint.Hit;
        line.PointerPressed += (_, _) => box.Focus(FocusState.Pointer);
        return Control(line, new PanelControl(
            command,
            () => new PanelLine("field", box.PlaceholderText, box.Text.Length == 0 ? null : box.Text, null, command),
            (_, sent) =>
            {
                if (sent is null)
                {
                    return;
                }

                // The plugin hears a summon here, once, and not again from the box.
                writing = true;
                box.Text = sent;
                box.SelectionStart = sent.Length;
                writing = false;
                Pressed(command, () => changed(sent))();

                // As with typing: text the plugin did not keep does not stay in the line.
                Follow();
            }));
    }

    /// <summary>The first <typeparamref name="T"/> drawn under <paramref name="node"/> that is <paramref name="wanted"/>, such as a part of a control's template.</summary>
    /// <returns>The element. Null when there is none, or none yet.</returns>
    private static T? Under<T>(DependencyObject node, Func<T, bool> wanted)
        where T : class
    {
        if (node is T found && wanted(found))
        {
            return found;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (Under(VisualTreeHelper.GetChild(node, i), wanted) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    /// <summary>Shows and hides <paramref name="caret"/> at the pace Windows has set, for as long as it runs. The compositor keeps the time.</summary>
    /// <returns>The blink, stopped. Null when Windows says a caret does not blink.</returns>
    private static Storyboard? Blink(UIElement caret)
    {
        if (Motion.CaretBeat() is not { } beat)
        {
            return null;
        }

        var frames = new DoubleAnimationUsingKeyFrames { Duration = beat + beat, RepeatBehavior = RepeatBehavior.Forever };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = beat, Value = 0 });
        Storyboard.SetTarget(frames, caret);
        Storyboard.SetTargetProperty(frames, "Opacity");
        var blink = new Storyboard();
        blink.Children.Add(frames);
        return blink;
    }
}
