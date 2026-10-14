using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell.Kit;

/// <summary>The Kit's choices and figures: a tab strip or row of chips, a stepper, and labeled figures.</summary>
public sealed partial class Kit
{
    /// <summary>
    /// Buttons that share the width: a segmented tab strip, or a row of chips with the chosen one filled in the accent.
    /// A click calls <paramref name="press"/> with that chip. The chosen one follows <paramref name="items"/>.
    /// With <paramref name="fit"/>, each chip is as wide as its own label.
    /// </summary>
    /// <param name="items">The chips now: label, glyph name, command, and whether each is chosen.</param>
    /// <param name="press">What a click on a chip does.</param>
    /// <param name="label">A quiet label over the chips. Empty draws none.</param>
    /// <param name="tabs">True for a tab strip, false for chips.</param>
    /// <param name="fit">True when each chip is as wide as its label, with the padding a button that stands alone uses. False shares the width. A tab strip keeps its own padding either way.</param>
    /// <returns>The row.</returns>
    public UIElement ButtonGroup(Func<IReadOnlyList<PanelChip>> items, Action<PanelChip> press, string label = "", bool tabs = false, bool fit = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(press);
        var strip = new Grid { ColumnSpacing = tabs ? Spacing.LabelGap : Spacing.PanelGap };
        var shown = new List<PanelChip>();
        var buttons = new List<Button>();
        var faces = new List<ChipFace>();

        // The buttons are laid again when the chips change, on a later refresh. The ones they replace are forgotten then.
        var made = Open(_made);

        void Lay(IReadOnlyList<PanelChip> chips)
        {
            strip.Children.Clear();
            strip.ColumnDefinitions.Clear();
            buttons.Clear();
            faces.Clear();
            Empty(made);
            var outer = _made;
            _made = made;
            try
            {
                Fill(chips);
            }
            finally
            {
                _made = outer;
            }
        }

        void Fill(IReadOnlyList<PanelChip> chips)
        {
            for (var i = 0; i < chips.Count; i++)
            {
                // Star columns share the strip. Auto columns let each chip be as wide as its label, so the padding is not eaten.
                strip.ColumnDefinitions.Add(new ColumnDefinition { Width = fit ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
                var chip = chips[i];
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    CornerRadius = Corners.Control,
                };
                if (tabs)
                {
                    button.Style = Paint.Style("BarButtonStyle");
                    button.MinHeight = 32;
                    button.Padding = new Thickness(Spacing.RowGap + Spacing.LabelGap, Spacing.RowGap, Spacing.RowGap + Spacing.LabelGap, Spacing.RowGap);
                }
                else
                {
                    button.MinHeight = 36;
                    // A chip that shares the width only needs a little inset; the spare room is the padding.
                    // One that fits its label uses the padding a button that stands alone uses, on both sides.
                    button.Padding = fit
                        ? new Thickness(Spacing.ControlPaddingX, Spacing.ControlPaddingY, Spacing.ControlPaddingX, Spacing.ControlPaddingY)
                        : new Thickness(Spacing.RowGap, Spacing.ControlPaddingY, Spacing.RowGap, Spacing.ControlPaddingY);
                    button.BorderThickness = new Thickness(1);
                    // The Windows button paints a grey fill and edge of its own under the pointer, over whatever the chip holds.
                    // These brushes are the chip's own, so Decorate can color them: a wash for an outlined chip, the accent dimmed for the chosen one.
                    foreach (var key in ChipStates)
                    {
                        button.Resources[key] = new SolidColorBrush();
                    }
                }

                AutomationProperties.SetName(button, chip.Label);
                // The chip as it is at the click. The one the button was laid from may have been chosen or let go since.
                var at = i;
                var run = Pressed(chip.Command, () => press(shown[at]));
                button.Click += (_, _) => run();
                Control(button, new PanelControl(chip.Command, () => new PanelLine(tabs ? "tab" : "option", chip.Label, Command: chip.Command, Selected: Chosen(button)), (_, _) => run()));
                Grid.SetColumn(button, i);
                strip.Children.Add(button);
                buttons.Add(button);
                faces.Add(Face(button, chip));
            }
        }

        bool Differs(IReadOnlyList<PanelChip> chips)
        {
            if (chips.Count != shown.Count)
            {
                return true;
            }

            for (var i = 0; i < chips.Count; i++)
            {
                if (chips[i].Label != shown[i].Label || chips[i].Command != shown[i].Command || chips[i].Glyph != shown[i].Glyph)
                {
                    return true;
                }
            }

            return false;
        }

        void Mark(IReadOnlyList<PanelChip> chips)
        {
            if (Differs(chips))
            {
                Lay(chips);
            }

            shown.Clear();
            shown.AddRange(chips);
            for (var i = 0; i < chips.Count; i++)
            {
                Decorate(buttons[i], faces[i], chips[i], tabs);
            }
        }

        Bind(items, Mark, new ChipsComparer());
        var body = tabs
            ? (UIElement)new Border
            {
                Background = Paint.Hit,
                BorderBrush = Paint.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = Corners.Overlay,
                Padding = new Thickness(Spacing.LabelGap + 1),
                Child = strip,
            }
            : strip;

        // A labeled row of chips is its own group, so it keeps clear of the row above, and a tab strip of the page under it.
        // Both margins are the row's own, where the column can see them beside a separator.
        var stack = new StackPanel
        {
            Spacing = Spacing.PanelGap,
            Margin = new Thickness(0, label.Length > 0 ? Spacing.PanelGap : 0, 0, tabs ? Spacing.PanelGap : 0),
        };
        if (label.Length > 0)
        {
            stack.Children.Add(PanelSectionHeader(label));
        }

        stack.Children.Add(body);
        return Group(stack, () => new PanelLine(tabs ? "tabs" : "choice", label.Length == 0 ? null : label));
    }

    /// <summary>
    /// A label and a value with back and forward buttons. A button calls <paramref name="step"/> with -1 or 1.
    /// With no <paramref name="step"/> it shows the value alone, muted.
    /// </summary>
    /// <param name="label">What the value is.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends -1 or 1 with <c>--value</c>. Empty for a read-only value.</param>
    /// <param name="value">The value as text now.</param>
    /// <param name="step">Moves the value back or forward. Null for a read-only value.</param>
    /// <returns>The row.</returns>
    public UIElement NumberField(string label, string command, Func<string> value, Action<int>? step = null)
    {
        var grid = new Grid { MinHeight = 40, ColumnSpacing = Spacing.ControlGap };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Paint.Style("PanelTextStyle"),
            Foreground = Paint.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        var shown = new TextBlock
        {
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Caption,
            FontWeight = FontWeights.SemiBold,
            Foreground = step is null ? Paint.Muted : Paint.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
        };
        Bind(value, text => shown.Text = text);
        if (step is null || command.Length == 0)
        {
            Grid.SetColumn(shown, 1);
            grid.Children.Add(shown);
            return Control(grid, new PanelControl("", () => new PanelLine("stepper", label, shown.Text)));
        }

        shown.HorizontalAlignment = HorizontalAlignment.Center;
        shown.TextAlignment = TextAlignment.Center;
        var control = new Grid { MinWidth = 148, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        control.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        control.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        control.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = StepButton(Glyphs.Back, label + " back", Pressed(command, () => step(-1)));
        var forward = StepButton(Glyphs.Forward, label + " forward", Pressed(command, () => step(1)));
        control.Children.Add(back);
        Grid.SetColumn(shown, 1);
        control.Children.Add(shown);
        Grid.SetColumn(forward, 2);
        control.Children.Add(forward);
        var frame = new Border
        {
            BorderBrush = Paint.Stroke,
            BorderThickness = new Thickness(1),
            Background = Paint.Hit,
            CornerRadius = Corners.Control,
            VerticalAlignment = VerticalAlignment.Center,
            Child = control,
        };
        Grid.SetColumn(frame, 1);
        grid.Children.Add(frame);
        return Control(grid, new PanelControl(
            command,
            () => new PanelLine("stepper", label, shown.Text, Command: command),
            (delta, _) => Pressed(command, () => step(delta is < 0 ? -1 : 1))()));
    }

    /// <summary>Labeled figures, two to a line, on a card, such as a battery's size, cycles, and health.</summary>
    /// <param name="figures">The figures now. Their labels may change too, such as time left or time to full.</param>
    /// <returns>The row.</returns>
    public UIElement Figures(Func<IReadOnlyList<PanelFigure>> figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        var lines = new StackPanel { Spacing = Spacing.RowGap + Spacing.LabelGap };
        var labels = new List<TextBlock>();
        var values = new List<TextBlock>();
        Bind(
            figures,
            next =>
            {
                if (next.Count != labels.Count)
                {
                    lines.Children.Clear();
                    labels.Clear();
                    values.Clear();
                    for (var i = 0; i < next.Count; i += 2)
                    {
                        var line = new Grid();
                        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        line.Children.Add(Fact(labels, values));
                        if (i + 1 < next.Count)
                        {
                            var right = Fact(labels, values);
                            right.Margin = new Thickness(Spacing.ControlGap + Spacing.RowGap, 0, 0, 0);
                            Grid.SetColumn(right, 1);
                            line.Children.Add(right);
                        }

                        lines.Children.Add(line);
                    }
                }

                for (var i = 0; i < next.Count; i++)
                {
                    labels[i].Text = next[i].Label;
                    values[i].Text = next[i].Value;
                }
            },
            new FiguresComparer());
        var card = new Border
        {
            Background = Paint.Hover,
            BorderBrush = Paint.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = Corners.Overlay,
            Padding = new Thickness(Spacing.RowPaddingX, Spacing.PanelGap + Spacing.LabelGap, Spacing.RowPaddingX, Spacing.PanelGap + Spacing.LabelGap),
            Child = lines,
        };
        return Control(card, new PanelControl("", () => new PanelLine(
            "figures",
            Rows: labels.Select((label, i) => new PanelLine("figure", label.Text, values[i].Text)).ToList())));
    }

    private static bool Chosen(Button button) => button.Tag is true;

    /// <summary>What the Windows button template paints on a chip under the pointer and while pressed.</summary>
    private static readonly string[] ChipStates =
    [
        "ButtonBackgroundPointerOver",
        "ButtonBackgroundPressed",
        "ButtonBorderBrushPointerOver",
        "ButtonBorderBrushPressed",
    ];

    /// <summary>Gives one of a chip's own state brushes the color of <paramref name="like"/>. The template holds the brush, so it follows at once.</summary>
    private static void Shade(Button button, string key, Brush like)
    {
        if (button.Resources.TryGetValue(key, out var held) && held is SolidColorBrush brush && like is SolidColorBrush source)
        {
            brush.Color = source.Color;
        }
    }

    /// <summary>A chip's label and glyph. They are built once, and a change of the chosen chip recolors them where they are.</summary>
    private sealed record ChipFace(TextBlock Label, FontIcon? Glyph);

    /// <summary>Gives <paramref name="button"/> its label, and its glyph when the chip names one.</summary>
    private static ChipFace Face(Button button, PanelChip chip)
    {
        var label = new TextBlock
        {
            Text = chip.Label,
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Caption,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var glyph = Glyphs.Named(chip.Glyph);
        if (glyph.Length == 0)
        {
            button.Content = label;
            return new ChipFace(label, null);
        }

        var mark = Glyph(glyph, TypeRamp.Caption, Paint.Muted);
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.RowGap + Spacing.LabelGap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { mark, label },
        };
        return new ChipFace(label, mark);
    }

    private static void Decorate(Button button, ChipFace face, PanelChip chip, bool tabs)
    {
        button.Tag = chip.Selected;
        var label = face.Label;
        label.FontWeight = chip.Selected ? FontWeights.SemiBold : FontWeights.Normal;
        Brush glyphInk;
        if (tabs)
        {
            label.Foreground = chip.Selected ? Paint.Foreground : Paint.Muted;
            glyphInk = chip.Selected ? Paint.AccentText : Paint.Muted;
            button.Background = chip.Selected ? Paint.Selected : Paint.Hit;
        }
        else
        {
            label.Foreground = chip.Selected ? Paint.OnAccent : Paint.Foreground;
            glyphInk = label.Foreground;
            button.Background = chip.Selected ? Paint.Accent : Paint.Hit;
            button.BorderBrush = chip.Selected ? Paint.Accent : Paint.Stroke;
            var over = chip.Selected ? Paint.AccentHover : Paint.Hover;
            var down = chip.Selected ? Paint.AccentPressed : Paint.Pressed;
            Shade(button, "ButtonBackgroundPointerOver", over);
            Shade(button, "ButtonBackgroundPressed", down);
            Shade(button, "ButtonBorderBrushPointerOver", chip.Selected ? over : Paint.Stroke);
            Shade(button, "ButtonBorderBrushPressed", chip.Selected ? down : Paint.Stroke);
        }

        if (face.Glyph is { } mark)
        {
            mark.Foreground = glyphInk;
        }
    }

    private static Button StepButton(string glyph, string label, Action press)
    {
        var button = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 10, FontFamily = Fonts.Icons, Foreground = Paint.Muted },
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => press();
        return button;
    }

    private static Grid Fact(List<TextBlock> labels, List<TextBlock> values)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Caption,
            Foreground = Paint.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock
        {
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Body,
            FontWeight = FontWeights.SemiBold,
            Foreground = Paint.Foreground,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(label);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        labels.Add(label);
        values.Add(value);
        return grid;
    }

    /// <summary>Two chip lists match when every chip matches.</summary>
    private sealed class ChipsComparer : IEqualityComparer<IReadOnlyList<PanelChip>>
    {
        public bool Equals(IReadOnlyList<PanelChip>? left, IReadOnlyList<PanelChip>? right) =>
            left is not null && right is not null && left.SequenceEqual(right);

        public int GetHashCode(IReadOnlyList<PanelChip> obj) => obj.Count;
    }

    /// <summary>Two figure lists match when every figure matches.</summary>
    private sealed class FiguresComparer : IEqualityComparer<IReadOnlyList<PanelFigure>>
    {
        public bool Equals(IReadOnlyList<PanelFigure>? left, IReadOnlyList<PanelFigure>? right) =>
            left is not null && right is not null && left.SequenceEqual(right);

        public int GetHashCode(IReadOnlyList<PanelFigure> obj) => obj.Count;
    }
}
