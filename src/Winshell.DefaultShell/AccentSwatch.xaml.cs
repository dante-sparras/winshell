using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell.DefaultShell;

/// <summary>One Windows accent color. The swatch is a radio button, and the focus ring is the system's.</summary>
public sealed partial class AccentSwatch : UserControl
{
    public AccentSwatch(string color, string name)
    {
        InitializeComponent();
        Color = color;
        AutomationProperties.SetName(Choice, name);
        Swatch.Background = new SolidColorBrush(SettingsCatalog.AccentColor(color));
        Check.Foreground = new SolidColorBrush(SettingsCatalog.LightGlyph(color) ? Colors.Black : Colors.White);
    }

    public string Color { get; }

    public bool IsChosen
    {
        get => Choice.IsChecked == true;
        set => Choice.IsChecked = value;
    }

    public event EventHandler? Picked;

    public void FocusChoice() => Choice.Focus(FocusState.Keyboard);

    public bool Holds(DependencyObject source)
    {
        for (DependencyObject? item = source; item is not null; item = VisualTreeHelper.GetParent(item))
        {
            if (ReferenceEquals(item, this))
                return true;
        }
        return false;
    }

    private void OnChecked(object sender, RoutedEventArgs e)
    {
        ShowCheck(true);
        Picked?.Invoke(this, EventArgs.Empty);
    }

    private void OnUnchecked(object sender, RoutedEventArgs e) => ShowCheck(false);

    private void ShowCheck(bool chosen)
    {
        Check.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        Swatch.BorderThickness = new Thickness(chosen ? 2 : 0);
    }
}
