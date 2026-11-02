using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// A glyph, a slider and an optional page chevron, as the volume and brightness rows draw them. The slider does not
/// write back a value the row was just given.
/// </summary>
public sealed partial class SliderRow : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderRow), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphLabelProperty = DependencyProperty.Register(
        nameof(GlyphLabel), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty PageLabelProperty = DependencyProperty.Register(
        nameof(PageLabel), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(QuickSettingsSession), typeof(SliderRow), new PropertyMetadata(null));

    public SliderRow() => InitializeComponent();

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string GlyphLabel
    {
        get => (string)GetValue(GlyphLabelProperty);
        set => SetValue(GlyphLabelProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string PageLabel
    {
        get => (string)GetValue(PageLabelProperty);
        set => SetValue(PageLabelProperty, value);
    }

    /// <summary>When set, the slider and the glyph change this session. Otherwise <see cref="Moved"/> fires.</summary>
    public QuickSettingsSession? Session
    {
        get => (QuickSettingsSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public event EventHandler<double>? Moved;

    public event EventHandler? GlyphActivated;

    public event EventHandler? PageActivated;

    /// <summary>A change that only repeats the value already shown, within half a step.</summary>
    public static bool IsEcho(double shown, double proposed, double step) => Math.Abs(proposed - shown) < step / 2;

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var row = (SliderRow)sender;
        var value = (double)e.NewValue;
        if (row.Range.Value != value)
            row.Range.Value = value;
    }

    private void OnRangeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (IsEcho(Value, e.NewValue, Range.StepFrequency))
            return;
        if (Session is { } session)
            session.SetVolume(e.NewValue);
        else
            Moved?.Invoke(this, e.NewValue);
    }

    private void OnGlyphClick(object sender, RoutedEventArgs e)
    {
        if (Session is { } session)
            session.ToggleMute();
        else
            GlyphActivated?.Invoke(this, EventArgs.Empty);
    }

    private void OnPageClick(object sender, RoutedEventArgs e) => PageActivated?.Invoke(this, EventArgs.Empty);

    private Visibility ButtonShown(string label) => string.IsNullOrEmpty(label) ? Visibility.Collapsed : Visibility.Visible;

    private Visibility TextShown(string label) => string.IsNullOrEmpty(label) ? Visibility.Visible : Visibility.Collapsed;
}
