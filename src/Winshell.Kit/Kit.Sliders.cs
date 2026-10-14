using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Winshell.Kit;

/// <summary>The Kit's sliders and meter: a rounded track on the column, alone, under a label with a figure on the right, or on one line between a mark and its percent.</summary>
public sealed partial class Kit
{
    private const double TrackHeight = 28;
    private const double Groove = 8;
    private const double Thumb = 18;
    private const int SliderPaceMs = 40;
    private const double ThinGroove = 4;
    private const double PercentWidth = 36;

    /// <summary>
    /// A slider under its label and percent. Dragging calls <paramref name="set"/> as it moves, then once more where the pointer lets go.
    /// It follows <paramref name="value"/>, such as a volume key pressed outside the panel, except while the pointer holds it.
    /// </summary>
    /// <param name="label">What it sets.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends the value with <c>--value</c>.</param>
    /// <param name="value">The value now, from <paramref name="minimum"/> to <paramref name="maximum"/>.</param>
    /// <param name="set">Takes a new value.</param>
    /// <param name="minimum">The left end.</param>
    /// <param name="maximum">The right end.</param>
    /// <param name="glyph">A glyph before the label, such as an app's volume. Empty for none.</param>
    /// <returns>The row.</returns>
    public UIElement PanelSlider(string label, string command, Func<double> value, Action<double> set, double minimum = 0, double maximum = 1, string glyph = "")
    {
        var figure = Figure();
        var track = Slider(label, command, value, set, raw => figure.Text = Percent(maximum - minimum <= 0 ? 0 : (raw - minimum) / (maximum - minimum)), minimum, maximum);
        return new StackPanel { Children = { Head(label, figure, glyph), track } };
    }

    /// <summary>
    /// A slider on its own: the track and its thumb, for a row that draws its own label and figure, such as an app's volume beside its logo.
    /// It drags and follows <paramref name="value"/> as <see cref="PanelSlider"/> does.
    /// </summary>
    /// <param name="label">What it sets, for the CLI and screen readers.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends the value with <c>--value</c>.</param>
    /// <param name="value">The value now, from <paramref name="minimum"/> to <paramref name="maximum"/>.</param>
    /// <param name="set">Takes a new value.</param>
    /// <param name="shown">Told the value under the thumb each time the thumb moves, so the row's figure keeps up with a drag. Null for none.</param>
    /// <param name="minimum">The left end.</param>
    /// <param name="maximum">The right end.</param>
    /// <returns>The slider.</returns>
    public FrameworkElement Slider(string label, string command, Func<double> value, Action<double> set, Action<double>? shown = null, double minimum = 0, double maximum = 1)
    {
        ArgumentNullException.ThrowIfNull(set);
        var track = new Grid { Height = TrackHeight, Background = Paint.Hit };
        var fill = Bar(Groove, Corners.Control, Paint.Accent);
        var thumb = new Border
        {
            Width = Thumb,
            Height = Thumb,
            CornerRadius = Corners.Circle(Thumb),
            Background = Paint.Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        fill.HorizontalAlignment = HorizontalAlignment.Left;
        track.Children.Add(Bar(Groove, Corners.Control, Paint.Track));
        track.Children.Add(fill);
        track.Children.Add(thumb);
        AutomationProperties.SetName(track, label);
        var current = 0.0;
        var sent = 0.0;
        var held = false;
        double Ratio(double raw) => maximum - minimum <= 0 ? 0 : Math.Clamp((raw - minimum) / (maximum - minimum), 0, 1);
        double Raw(double ratio) => minimum + (ratio * (maximum - minimum));
        void Draw()
        {
            shown?.Invoke(Raw(current));
            var width = track.ActualWidth;
            if (width <= 0)
            {
                return;
            }

            var at = width * current;
            fill.Width = Math.Max(Groove, at);
            thumb.Margin = new Thickness(Math.Clamp(at - (Thumb / 2), 0, Math.Max(0, width - Thumb)), 0, 0, 0);
        }

        track.SizeChanged += (_, _) => Draw();
        Bind(value, raw =>
        {
            if (held)
            {
                return;
            }

            current = Ratio(raw);
            sent = current;
            Draw();
        });
        // The thumb follows the pointer at once. The value is sent at most once per pace, since each send runs the plugin
        // and refreshes the whole panel, and a fast drag raises a pointer event for nearly every step.
        Microsoft.UI.Dispatching.DispatcherQueueTimer? pace = null;
        var waiting = false;
        void Send()
        {
            sent = current;
            var raw = Raw(current);
            Pressed(command, () => set(raw))();
        }

        // The timer holds its tick, and the tick holds this slider and the timer: a ring through Windows that the collector
        // cannot see past. The end of a drag lets the timer go, so a slider that was dragged leaves with its panel.
        void Rest()
        {
            waiting = false;
            pace?.Stop();
            pace = null;
        }

        void Move(PointerRoutedEventArgs args, bool final)
        {
            current = Math.Clamp(args.GetCurrentPoint(track).Position.X / Math.Max(1, track.ActualWidth), 0, 1);
            Draw();
            if (final)
            {
                Rest();
                Send();
                return;
            }

            if (Math.Abs(current - sent) < 0.02)
            {
                return;
            }

            if (pace is { IsRunning: true })
            {
                waiting = true;
                return;
            }

            Send();
            if (pace is null)
            {
                pace = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                pace.Interval = TimeSpan.FromMilliseconds(SliderPaceMs);
                pace.IsRepeating = false;
                pace.Tick += (timer, _) =>
                {
                    if (waiting && held)
                    {
                        waiting = false;
                        Send();
                        timer.Start();
                    }
                };
            }

            pace.Start();
        }

        track.PointerPressed += (_, args) =>
        {
            // A right or middle press on the track is not a drag.
            if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && !args.GetCurrentPoint(track).Properties.IsLeftButtonPressed)
            {
                return;
            }

            held = true;
            track.CapturePointer(args.Pointer);
            Move(args, final: false);
            args.Handled = true;
        };
        track.PointerMoved += (_, args) =>
        {
            if (held && args.GetCurrentPoint(track).Properties.IsLeftButtonPressed)
            {
                Move(args, final: false);
            }
        };
        track.PointerReleased += (_, args) =>
        {
            if (held)
            {
                Move(args, final: true);
            }

            held = false;
            track.ReleasePointerCapture(args.Pointer);
        };
        track.PointerCaptureLost += (_, _) =>
        {
            // A drag that loses the pointer still ends where the thumb is.
            Rest();
            if (held && current != sent)
            {
                Send();
            }

            held = false;
        };
        return Control(track, new PanelControl(
            command,
            () => new PanelLine("slider", label, Number(Raw(current)), Command: command, Minimum: minimum, Maximum: maximum),
            (pressed, _) =>
            {
                if (pressed is double raw)
                {
                    Pressed(command, () => set(Math.Clamp(raw, minimum, maximum)))();
                }
            }));
    }

    /// <summary>
    /// A level on one line, for a card: a glyph, the slider, and its percent. With <paramref name="live"/>, a thin meter under
    /// the slider shows the level as it is measured, such as what a microphone hears.
    /// </summary>
    /// <param name="glyph">A Segoe Fluent glyph that says what the slider sets.</param>
    /// <param name="label">What the slider sets, for the CLI and screen readers.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends the value with <c>--value</c>.</param>
    /// <param name="value">The level now, from 0 to 1.</param>
    /// <param name="set">Takes a new level.</param>
    /// <param name="live">The measured level now, from 0 to 1. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement LevelSlider(string glyph, string label, string command, Func<double> value, Action<double> set, Func<double>? live = null)
    {
        var mark = Glyph(glyph, TypeRamp.Icon, Paint.Foreground);
        mark.Width = Spacing.Mark;
        return LevelSlider(mark, label, command, value, set, live);
    }

    /// <summary>A level on one line whose mark is the plugin's own, such as a speaker that is the mute button.</summary>
    /// <param name="mark">What stands before the slider, <see cref="Spacing.Mark"/> wide.</param>
    /// <param name="label">What the slider sets, for the CLI and screen readers.</param>
    /// <param name="command">Its command name, for the CLI. A summon sends the value with <c>--value</c>.</param>
    /// <param name="value">The level now, from 0 to 1.</param>
    /// <param name="set">Takes a new level.</param>
    /// <param name="live">The measured level now, from 0 to 1. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement LevelSlider(FrameworkElement mark, string label, string command, Func<double> value, Action<double> set, Func<double>? live = null)
    {
        ArgumentNullException.ThrowIfNull(mark);
        var figure = Figure();
        var row = new Grid { ColumnSpacing = Spacing.ControlGap };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Mark) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PercentWidth) });
        row.Children.Add(mark);
        var slider = Slider(label, command, value, set, level => figure.Text = Percent(level));
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        Grid.SetColumn(figure, 2);
        row.Children.Add(figure);
        if (live is not null)
        {
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var meter = (FrameworkElement)Meter(live);
            Grid.SetColumn(meter, 1);
            Grid.SetRow(meter, 1);
            row.Children.Add(meter);
        }

        return row;
    }

    /// <summary>
    /// A read-only level, such as a battery or a microphone. With a label it has a line over it and a figure on the right;
    /// without one it is a thin bar, such as the live microphone level under its slider.
    /// </summary>
    /// <param name="value">The fill now, from 0 to 1.</param>
    /// <param name="label">What it measures. Empty draws the thin bar alone.</param>
    /// <param name="detail">The figure on the right now, such as a percent. Null for none.</param>
    /// <returns>The row.</returns>
    public UIElement Meter(Func<double> value, string label = "", Func<string>? detail = null)
    {
        var thin = label.Length == 0 && detail is null;
        var height = thin ? ThinGroove : Groove;
        var track = new Grid
        {
            Height = thin ? ThinGroove + (Spacing.RowGap * 1.5) : TrackHeight,
            Margin = thin ? new Thickness(0, 0, 0, Spacing.LabelGap) : new Thickness(0),
            Background = Paint.Hit,
        };
        var corner = thin ? Corners.Circle(ThinGroove) : Corners.Control;
        var fill = Bar(height, corner, Paint.Accent);
        fill.HorizontalAlignment = HorizontalAlignment.Left;
        track.Children.Add(Bar(height, corner, Paint.Track));
        track.Children.Add(fill);
        var current = 0.0;
        void Draw()
        {
            if (track.ActualWidth > 0)
            {
                var next = current <= 0 ? 0 : Math.Max(height, track.ActualWidth * current);
                // A fill that has no width yet reads NaN, and no comparison with NaN is true. Asked this way round, it gets its first width.
                if (!(Math.Abs(fill.Width - next) < 0.5))
                {
                    fill.Width = next;
                }
            }
        }

        track.SizeChanged += (_, _) => Draw();
        Bind(value, ratio =>
        {
            current = Math.Clamp(ratio, 0, 1);
            Draw();
        });
        if (thin)
        {
            return Control(track, new PanelControl("", () => new PanelLine("level", Value: Number(current))));
        }

        var figure = Figure();
        if (detail is not null)
        {
            Bind(detail, text => figure.Text = text);
        }

        var row = new StackPanel { Children = { Head(label, figure, ""), track } };
        return Control(row, new PanelControl("", () => new PanelLine("level", label, Number(current), figure.Text.Length == 0 ? null : figure.Text)));
    }

    private static TextBlock Figure() => new()
    {
        Style = Paint.Style("PanelTextStyle"),
        FontSize = TypeRamp.Caption,
        Foreground = Paint.Muted,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Grid Head(string label, TextBlock figure, string glyph)
    {
        var head = new Grid { ColumnSpacing = glyph.Length > 0 ? Spacing.ControlGap : 0, Margin = new Thickness(0, 0, 0, Spacing.LabelGap) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph.Length > 0)
        {
            head.Children.Add(Glyph(glyph, TypeRamp.Icon, Paint.Foreground));
        }

        var name = new TextBlock
        {
            Text = label,
            Style = Paint.Style("PanelTextStyle"),
            Foreground = Paint.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        Grid.SetColumn(figure, 2);
        head.Children.Add(figure);
        return head;
    }

    private static Border Bar(double height, CornerRadius corner, Microsoft.UI.Xaml.Media.Brush brush) => new()
    {
        Height = height,
        CornerRadius = corner,
        Background = brush,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    private static string Percent(double ratio) =>
        Math.Round(Math.Clamp(ratio, 0, 1) * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";

    private static string Number(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
}
