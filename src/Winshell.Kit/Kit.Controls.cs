using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell.Kit;

/// <summary>The Kit's shared controls. Each lines up on the panel's one column and reads its sizes and brushes from the tokens.</summary>
public sealed partial class Kit
{
    /// <summary>A line of body text that wraps.</summary>
    /// <param name="value">The text now.</param>
    /// <returns>The row.</returns>
    public UIElement Text(Func<string> value) => Text(value, value);

    /// <summary>
    /// A line of body text that wraps. <paramref name="reading"/> is what <c>winoma desktop</c> prints, so a row can show a secret the reading leaves out.
    /// </summary>
    /// <param name="shown">The text on screen.</param>
    /// <param name="reading">The text <c>winoma desktop</c> prints.</param>
    /// <returns>The row.</returns>
    public UIElement Text(Func<string> shown, Func<string> reading)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(reading);
        var block = Body();
        Bind(shown, text => block.Text = text);
        return Control(block, new PanelControl("", () => new PanelLine("text", reading())));
    }

    /// <summary>A line of body text that does not change.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The row.</returns>
    public UIElement Text(string value)
    {
        // Written once. A getter here would be read on every refresh of the panel to find the same text.
        var block = Body();
        block.Text = value;
        return Control(block, new PanelControl("", () => new PanelLine("text", block.Text)));
    }

    private static TextBlock Body() => new()
    {
        Style = Paint.Style("PanelTextStyle"),
        Foreground = Paint.Foreground,
        Margin = new Thickness(0, Spacing.LabelGap, 0, Spacing.LabelGap),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A small line in a tone, such as a status or a warning. Normal is muted.</summary>
    /// <param name="value">The text now.</param>
    /// <param name="tone">Its color.</param>
    /// <returns>The row.</returns>
    public UIElement Note(Func<string> value, PanelTone tone = PanelTone.Normal)
    {
        var block = new TextBlock
        {
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Caption,
            Foreground = Paint.Ink(tone),
            TextWrapping = TextWrapping.Wrap,
        };
        Bind(value, text =>
        {
            block.Text = text;
            block.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        return Control(block, new PanelControl("", () => new PanelLine("note", block.Text)));
    }

    /// <summary>A heading that is only a name, in the display face.</summary>
    /// <param name="title">The name.</param>
    /// <returns>The row.</returns>
    public UIElement PanelTitle(string title) => Control(Title(title), new PanelControl("", () => new PanelLine("heading", title)));

    /// <summary>
    /// The top of a panel: a tile with a glyph in the accent, the name, a muted status, and an optional control on the right.
    /// </summary>
    /// <param name="glyph">A Segoe Fluent glyph.</param>
    /// <param name="title">The name.</param>
    /// <param name="status">The status line now. Empty hides it.</param>
    /// <param name="trailing">A control on the right, such as a switch. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement PanelHero(string glyph, string title, Func<string> status, UIElement? trailing = null)
    {
        var tile = Tile();
        ((FontIcon)tile.Child).Glyph = glyph;
        return Hero(tile, null, title, status, trailing);
    }

    /// <summary>The top of a panel whose glyph follows its state, such as a speaker that shows muted.</summary>
    /// <param name="glyph">The Segoe Fluent glyph now.</param>
    /// <param name="title">The name.</param>
    /// <param name="status">The status line now. Empty hides it.</param>
    /// <param name="trailing">A control on the right, such as a switch. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement PanelHero(Func<string> glyph, string title, Func<string> status, UIElement? trailing = null)
    {
        var tile = Tile();
        Bind(glyph, mark => ((FontIcon)tile.Child).Glyph = mark);
        return Hero(tile, null, title, status, trailing);
    }

    /// <summary>The top of a panel whose glyph and name follow its state, such as the network it is connected to.</summary>
    /// <param name="glyph">The Segoe Fluent glyph now.</param>
    /// <param name="title">The name now.</param>
    /// <param name="status">The status line now. Empty hides it.</param>
    /// <param name="trailing">A control on the right, such as a switch. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement PanelHero(Func<string> glyph, Func<string> title, Func<string> status, UIElement? trailing = null)
    {
        var tile = Tile();
        Bind(glyph, mark => ((FontIcon)tile.Child).Glyph = mark);
        return Hero(tile, title, "", status, trailing);
    }

    /// <summary>
    /// The top of a panel whose tile carries a mark of its own. A mark with colors of its own, such as a provider's logo,
    /// sits on the selected fill. One drawn in <see cref="Paint.AccentText"/>, such as the weather now, keeps the glyph tile's wash.
    /// </summary>
    /// <param name="mark">What the tile shows.</param>
    /// <param name="title">The name.</param>
    /// <param name="status">The status line now. Empty hides it.</param>
    /// <param name="trailing">A control on the right, such as icon buttons. Null for none.</param>
    /// <param name="accent">True when the mark is drawn in the accent, as a glyph is.</param>
    /// <returns>The row.</returns>
    public UIElement PanelHero(FrameworkElement mark, string title, Func<string> status, UIElement? trailing = null, bool accent = false)
    {
        var tile = Tile();
        if (!accent)
        {
            tile.Background = Paint.Selected;
        }

        tile.Child = mark;
        return Hero(tile, null, title, status, trailing);
    }

    /// <summary>The heading row. A name that follows the panel's state comes as <paramref name="title"/>; one that does not is written once.</summary>
    private UIElement Hero(Border tile, Func<string>? title, string fixedTitle, Func<string> status, UIElement? trailing)
    {
        var line = new TextBlock
        {
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Caption,
            Foreground = Paint.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Bind(status, text =>
        {
            line.Text = text;
            line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        });
        var name = Title(fixedTitle);
        if (title is not null)
        {
            Bind(title, text => name.Text = text);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, line } };
        var grid = new Grid { ColumnSpacing = Spacing.ControlGap, Margin = new Thickness(0, 0, 0, Spacing.PanelGap) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(tile);
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);
        if (trailing is not null)
        {
            Grid.SetColumn((FrameworkElement)trailing, 2);
            grid.Children.Add(trailing);
        }

        return Group(grid, () => new PanelLine("heading", name.Text, Detail: line.Text.Length == 0 ? null : line.Text));
    }

    /// <summary>A quiet label over a group of rows.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The row.</returns>
    public UIElement PanelSectionHeader(string label)
    {
        var block = new TextBlock
        {
            Text = label,
            Style = Paint.Style("PanelTextStyle"),
            FontSize = TypeRamp.Caption,
            FontWeight = FontWeights.SemiBold,
            Foreground = Paint.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, Spacing.LabelGap),
        };
        return Control(block, new PanelControl("", () => new PanelLine("section", label)));
    }

    /// <summary>
    /// The hairline between groups. It sits <see cref="Spacing.SeparatorGap"/> from what the rows beside it draw: a column
    /// of rows takes their margins out of the gap (<see cref="Rows"/>), and anywhere else the line's own margin is the gap.
    /// </summary>
    /// <returns>The row.</returns>
    public UIElement PanelSeparator() => Rows.Separator(new Border
    {
        Height = 1,
        Margin = new Thickness(0, Spacing.SeparatorGap, 0, Spacing.SeparatorGap),
        Background = Paint.Stroke,
    });

    /// <summary>The faint rounded card every panel uses, holding its own rows.</summary>
    /// <param name="rows">What the card holds.</param>
    /// <returns>The row.</returns>
    public UIElement Card(params UIElement[] rows) => new Border
    {
        Background = Paint.Hover,
        BorderBrush = Paint.Stroke,
        BorderThickness = new Thickness(1),
        CornerRadius = Corners.Overlay,
        Padding = Spacing.Card,
        Child = Stack(rows),
    };

    /// <summary>
    /// A group of a page: a quiet label over a card that holds the group's rows, clear of what is above it.
    /// A page of these needs no separators.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="rows">What the card holds.</param>
    /// <returns>The label and the card.</returns>
    public UIElement PanelSection(string label, params UIElement[] rows) => new Rows
    {
        Margin = new Thickness(0, Spacing.PanelGap, 0, 0),
        Children = { PanelSectionHeader(label), Card(rows) },
    };

    /// <summary>
    /// The headline figure for the right of a heading, such as a temperature, a volume, or a charge:
    /// a large number with its unit smaller and level with the top of the number.
    /// </summary>
    /// <param name="label">What the figure measures, for the CLI.</param>
    /// <param name="figure">The number and its unit now.</param>
    /// <param name="quiet">True while the figure is not in effect, such as a muted volume, which dims the number. Null for never.</param>
    /// <returns>The figure.</returns>
    public FrameworkElement HeroFigure(string label, Func<(string Number, string Unit)> figure, Func<bool>? quiet = null)
    {
        ArgumentNullException.ThrowIfNull(figure);
        var number = Headline(TypeRamp.TitleLarge);
        var unit = Headline(TypeRamp.Subtitle);
        unit.Foreground = Paint.Muted;
        unit.Margin = new Thickness(Spacing.LabelGap, 0, 0, 0);
        Bind(figure, now => (number.Text, unit.Text) = now);
        if (quiet is not null)
        {
            Bind(quiet, dim => number.Foreground = dim ? Paint.Muted : Paint.Foreground);
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { number, unit } };
        return Control(line, new PanelControl("", () => new PanelLine("figure", label, number.Text + unit.Text)));
    }

    /// <summary>A full-width button.</summary>
    /// <param name="label">What it says.</param>
    /// <param name="command">Its command name, for the CLI.</param>
    /// <param name="press">What a click does.</param>
    /// <returns>The row.</returns>
    public UIElement Button(string label, string command, Action press)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 40,
            FontSize = TypeRamp.Body,
            FontWeight = FontWeights.SemiBold,
            CornerRadius = Corners.Control,
            Background = Paint.Selected,
            Foreground = Paint.Foreground,
            BorderBrush = Paint.Stroke,
            BorderThickness = new Thickness(1),
        };
        var run = Pressed(command, press);
        button.Click += (_, _) => run();
        return Control(button, new PanelControl(command, () => new PanelLine("button", label, Command: command), (_, _) => run()));
    }

    /// <summary>
    /// A row in a list: an optional glyph, a label, an optional detail line, the selected fill when chosen. A click presses it.
    /// </summary>
    /// <param name="label">What it says.</param>
    /// <param name="command">Its command name, for the CLI.</param>
    /// <param name="press">What a click does.</param>
    /// <param name="selected">True while it is the chosen one. Null for a list without a choice.</param>
    /// <param name="glyph">A Segoe Fluent glyph before the label. Empty for none.</param>
    /// <param name="detail">A muted line under the label now. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement ListItem(string label, string command, Action press, Func<bool>? selected = null, string glyph = "", Func<string>? detail = null)
    {
        // With no glyph there is no glyph column, so the label starts on the row's padding like any other row's text.
        var body = new Grid { ColumnSpacing = glyph.Length > 0 ? Spacing.ControlGap : 0, VerticalAlignment = VerticalAlignment.Center };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (glyph.Length > 0)
        {
            body.Children.Add(Glyph(glyph, TypeRamp.Icon, Paint.Foreground));
        }

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(new TextBlock
        {
            Text = label,
            Style = Paint.Style("PanelTextStyle"),
            Foreground = Paint.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        TextBlock? under = null;
        if (detail is not null)
        {
            under = new TextBlock
            {
                Style = Paint.Style("PanelTextStyle"),
                FontSize = TypeRamp.Caption,
                Foreground = Paint.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            lines.Children.Add(under);
            Bind(detail, text =>
            {
                under.Text = text;
                under.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        Grid.SetColumn(lines, 1);
        body.Children.Add(lines);
        var row = new Button
        {
            Style = Paint.Style("PanelRowStyle"),
            Background = Paint.Hit,
            CornerRadius = Corners.Control,
            Content = body,
        };
        AutomationProperties.SetName(row, label);
        var chosen = false;
        if (selected is not null)
        {
            Bind(selected, on =>
            {
                chosen = on;
                row.Background = on ? Paint.Selected : Paint.Hit;
            });
        }

        var run = Pressed(command, press);
        row.Click += (_, _) => run();
        return Control(row, new PanelControl(
            command,
            () => new PanelLine("option", label, Detail: under is { Text.Length: > 0 } ? under.Text : null, Command: command, Selected: selected is null ? null : chosen),
            (_, _) => run()));
    }

    /// <summary>A label and a switch flush with the column's right edge. Turning it calls <paramref name="set"/>.</summary>
    /// <param name="label">What the switch controls.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends 1 for on.</param>
    /// <param name="on">True while it is on.</param>
    /// <param name="set">Turns it on or off.</param>
    /// <returns>The row.</returns>
    public UIElement ToggleSwitch(string label, string command, Func<bool> on, Action<bool> set)
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
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var toggle = Switch(label, command, on, set);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    /// <summary>A switch on its own, for the right side of a heading or a card. Turning it calls <paramref name="set"/>.</summary>
    /// <param name="label">What it controls, for the CLI and screen readers.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends 1 for on.</param>
    /// <param name="on">True while it is on.</param>
    /// <param name="set">Turns it on or off.</param>
    /// <returns>The switch.</returns>
    public FrameworkElement Switch(string label, string command, Func<bool> on, Action<bool> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var toggle = new ToggleSwitch
        {
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            // The template keeps a column after the knob for its label. With no label, that gap would hold the knob off the column's edge.
            Margin = new Thickness(0, 0, -Spacing.ControlGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(toggle, label);
        var writing = false;
        Bind(on, value =>
        {
            writing = true;
            toggle.IsOn = value;
            writing = false;
        });
        toggle.Toggled += (_, _) =>
        {
            if (!writing)
            {
                var value = toggle.IsOn;
                Pressed(command, () => set(value))();
            }
        };
        return Control(toggle, new PanelControl(
            command,
            () => new PanelLine("switch", label, Command: command, Selected: toggle.IsOn),
            (value, _) => Pressed(command, () => set(value is >= 0.5))()));
    }

    /// <summary>
    /// A line of text to type, with a button beside it. Enter or the button calls <paramref name="submit"/> with the text.
    /// What the person typed is never rewritten by a refresh; only the status line under it follows its getter.
    /// </summary>
    /// <param name="prompt">The placeholder.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends its text with <c>--text</c>.</param>
    /// <param name="submit">Takes the typed text.</param>
    /// <param name="status">A quiet line under the field now: progress or an error. Null for none.</param>
    /// <param name="submitLabel">What the button says.</param>
    /// <param name="focus">True when the field takes the keyboard as the panel opens. A field low on a page leaves it alone.</param>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="secret">True for a key or password: the text is hidden, can be peeked at, and is never printed for the CLI.</param>
    /// <returns>The row.</returns>
    public UIElement TextField(string prompt, string command, Action<string> submit, Func<string>? status = null, string submitLabel = "Search", bool focus = true, string text = "", bool secret = false)
    {
        ArgumentNullException.ThrowIfNull(submit);
        Control box;
        Func<string> typed;
        Action<string> type;
        if (secret)
        {
            var password = new PasswordBox { PlaceholderText = prompt, Password = text, PasswordRevealMode = PasswordRevealMode.Peek };
            typed = () => password.Password;
            type = value => password.Password = value;
            box = password;
        }
        else
        {
            var plain = new TextBox { PlaceholderText = prompt, Text = text };
            typed = () => plain.Text;
            type = value => plain.Text = value;
            box = plain;
        }

        box.FontFamily = Fonts.Text;
        box.MinHeight = 36;
        box.CornerRadius = Corners.Control;
        box.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(box, prompt);
        var button = new Button
        {
            Content = submitLabel,
            MinWidth = 84,
            MinHeight = 36,
            Margin = new Thickness(Spacing.PanelGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = Paint.Style("BarButtonStyle"),
        };
        void Submit() => Pressed(command, () => submit(typed()))();
        button.Click += (_, _) => Submit();
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                Submit();
                args.Handled = true;
            }
        };
        if (focus)
        {
            box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        }

        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(box);
        Grid.SetColumn(button, 1);
        line.Children.Add(button);
        var stack = new StackPanel { Spacing = Spacing.LabelGap * 3, Margin = new Thickness(0, Spacing.LabelGap, 0, Spacing.RowGap) };
        stack.Children.Add(line);
        TextBlock? under = null;
        if (status is not null)
        {
            under = new TextBlock
            {
                FontSize = TypeRamp.Caption,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Paint.Muted,
            };
            stack.Children.Add(under);
            Bind(status, value =>
            {
                under.Text = value;
                under.Visibility = value.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        return Control(stack, new PanelControl(
            command,
            () => new PanelLine("field", prompt, secret || typed().Length == 0 ? null : typed(), under is { Text.Length: > 0 } ? under.Text : null, command),
            (_, sent) =>
            {
                if (sent is not null)
                {
                    type(sent);
                }

                Submit();
            }));
    }

    /// <summary>A small square button with a glyph, such as a month step or a settings gear.</summary>
    /// <param name="glyph">A Segoe Fluent glyph.</param>
    /// <param name="label">What it does, for the tooltip and the CLI.</param>
    /// <param name="command">Its command name, for the CLI.</param>
    /// <param name="press">What a click does.</param>
    /// <returns>The button.</returns>
    public FrameworkElement IconButton(string glyph, string label, string command, Action press)
    {
        var button = new Button
        {
            Style = Paint.Style("BarButtonStyle"),
            Width = 32,
            Height = 32,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = Corners.Control,
            Background = Paint.Hit,
            VerticalAlignment = VerticalAlignment.Center,
            Content = Glyph(glyph, TypeRamp.Caption, Paint.Muted),
        };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        var run = Pressed(command, press);
        button.Click += (_, _) => run();
        return Control(button, new PanelControl(command, () => new PanelLine("button", label, Command: command), (_, _) => run()));
    }

    /// <summary>A Segoe Fluent glyph at <paramref name="size"/>.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="size">A size from <see cref="TypeRamp"/>.</param>
    /// <param name="brush">A brush from <see cref="Paint"/>.</param>
    /// <returns>The glyph.</returns>
    public static FontIcon Glyph(string glyph, double size, Brush brush) => new()
    {
        Glyph = glyph,
        FontFamily = Fonts.Icons,
        FontSize = size,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Title(string text) => new()
    {
        Text = text,
        FontFamily = Fonts.Display,
        FontSize = TypeRamp.Subtitle,
        FontWeight = FontWeights.SemiBold,
        Foreground = Paint.Foreground,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBlock Headline(double size) => new()
    {
        FontFamily = Fonts.Display,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Foreground = Paint.Foreground,
        TextLineBounds = TextLineBounds.Tight,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private static Border Tile() => new()
    {
        Width = Spacing.Tile,
        Height = Spacing.Tile,
        CornerRadius = Corners.Overlay,
        Background = Paint.Tint,
        BorderBrush = Paint.Stroke,
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new FontIcon
        {
            FontSize = TypeRamp.IconLarge,
            FontFamily = Fonts.Icons,
            Foreground = Paint.AccentText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };
}
