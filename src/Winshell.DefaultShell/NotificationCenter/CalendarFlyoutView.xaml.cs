using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The notification center and the calendar. The clock opens it in a popup above itself.</summary>
public sealed partial class CalendarFlyoutView : UserControl
{
    public CalendarFlyoutView(CalendarMonth month, NotificationCenter center, Preferences preferences)
    {
        Month = month;
        Center = center;
        Preferences = preferences;
        InitializeComponent();
        SizeChanged += OnSized;
    }

    public CalendarMonth Month { get; }

    public NotificationCenter Center { get; }

    public Preferences Preferences { get; }

    /// <summary>The taskbar is going away. The center and the preferences stay.</summary>
    public void Release() => Bindings.StopTracking();

    private void OnSized(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Height > 0)
            CalendarScroll.Height = Math.Min(360, e.NewSize.Height * 0.5);
    }

    private void OnWeekNumbers(object sender, RoutedEventArgs e)
    {
        if (WeekBox.IsChecked is bool on)
            Preferences.CalendarWeekNumbers = on;
    }

    private Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
