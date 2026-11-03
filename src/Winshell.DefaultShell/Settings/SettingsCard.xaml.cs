using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Winshell.DefaultShell;

/// <summary>One setting, as Windows 11 Settings draws it: a glyph, a title, a description and a control.</summary>
public sealed partial class SettingsCard : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsCard), new PropertyMetadata("", OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty TrailContentProperty = DependencyProperty.Register(
        nameof(TrailContent), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ClickableProperty = DependencyProperty.Register(
        nameof(Clickable), typeof(bool), typeof(SettingsCard), new PropertyMetadata(false));

    public static readonly DependencyProperty ChevronGlyphProperty = DependencyProperty.Register(
        nameof(ChevronGlyph), typeof(string), typeof(SettingsCard), new PropertyMetadata("\uE76C"));

    private bool _stacked;

    public SettingsCard() => InitializeComponent();

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? TrailContent
    {
        get => GetValue(TrailContentProperty);
        set => SetValue(TrailContentProperty, value);
    }

    public bool Clickable
    {
        get => (bool)GetValue(ClickableProperty);
        set => SetValue(ClickableProperty, value);
    }

    public string ChevronGlyph
    {
        get => (string)GetValue(ChevronGlyphProperty);
        set => SetValue(ChevronGlyphProperty, value);
    }

    public void Apply(SettingsEntry entry, string? description = null, bool clickable = false, string? chevron = null)
    {
        Glyph = entry.Glyph;
        Title = entry.Title;
        Description = description ?? entry.Description;
        Clickable = clickable;
        if (chevron is not null)
            ChevronGlyph = chevron;
    }

    private static void OnTitleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        AutomationProperties.SetName((SettingsCard)sender, e.NewValue as string ?? "");

    private Visibility DescriptionShown(string text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private Visibility ChevronShown(bool clickable) => clickable ? Visibility.Visible : Visibility.Collapsed;

    // The controls sit on the right until the title would have less than 160px, and then under the text.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0)
            return;
        var trail = TrailWidth();
        var textX = string.IsNullOrEmpty(Glyph) ? 16d : 52d;
        var endX = width - 16 - (Clickable ? 28 : 0);
        var stacked = trail > 0 && endX - textX - trail - 16 < 160;
        if (stacked == _stacked)
            return;
        _stacked = stacked;
        if (stacked)
        {
            Grid.SetColumn(TrailHost, 1);
            Grid.SetRow(TrailHost, 1);
            TrailHost.Margin = new Thickness(0, 8, 0, 0);
            TrailHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            Grid.SetColumn(TrailHost, 2);
            Grid.SetRow(TrailHost, 0);
            TrailHost.Margin = new Thickness(0);
            TrailHost.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }

    private double TrailWidth()
    {
        if (TrailHost.Content is not UIElement element)
            return 0;
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize.Width;
    }
}
