using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>One day in the month. Today is filled, a selected day is outlined, and a day from another month is dimmed.</summary>
public sealed partial class CalendarDayButton : Button
{
    private CalendarDay? _day;

    public CalendarDayButton()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CalendarDay? Day
    {
        get => _day;
        set
        {
            if (ReferenceEquals(_day, value))
                return;
            if (_day is not null)
                _day.PropertyChanged -= OnDay;
            _day = value;
            if (_day is not null)
                _day.PropertyChanged += OnDay;
            Apply();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_day is not null)
        {
            _day.PropertyChanged -= OnDay;
            _day.PropertyChanged += OnDay;
        }
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_day is not null)
            _day.PropertyChanged -= OnDay;
    }

    private void OnDay(object? sender, PropertyChangedEventArgs e) => Apply();

    private void OnClick(object sender, RoutedEventArgs e) => _day?.Select();

    private void Apply()
    {
        var day = _day;
        if (day is null)
            return;
        var label = day.Label;
        TodayText.Text = label;
        InMonthText.Text = label;
        OutText.Text = label;
        TodayFill.Visibility = Shown(day.IsToday);
        TodayText.Visibility = Shown(day.IsToday);
        InMonthText.Visibility = Shown(!day.IsToday && day.InMonth);
        OutText.Visibility = Shown(!day.IsToday && !day.InMonth);
        SelectedStroke.Visibility = Shown(day.IsSelected);
        AutomationProperties.SetName(this, day.FullDate);
        AutomationProperties.SetHelpText(this, day.Description);
        AutomationProperties.SetFullDescription(this, day.Description);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
