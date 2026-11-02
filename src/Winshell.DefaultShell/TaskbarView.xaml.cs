using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The taskbar of one screen: Start, Search, the apps, and the clock.</summary>
public sealed partial class TaskbarView : UserControl
{
    private readonly Preferences _preferences;

    public TaskbarView(TaskbarModel model, Preferences preferences, TimeProvider time)
    {
        Model = model;
        _preferences = preferences;
        InitializeComponent();
        Clock.Attach(preferences, time);
        Place();
        _preferences.PropertyChanged += OnPreferences;
    }

    public TaskbarModel Model { get; }

    /// <summary>Drops the clock and the preference subscription. The window is about to close.</summary>
    public void Release()
    {
        _preferences.PropertyChanged -= OnPreferences;
        Clock.Detach();
    }

    private void OnPreferences(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.TaskbarCentered))
            Place();
    }

    /// <summary>Centered apps sit in the middle. Apps on the left stay clear of the clock.</summary>
    private void Place()
    {
        if (_preferences.TaskbarCentered)
        {
            Grid.SetColumn(Apps, 1);
            Grid.SetColumnSpan(Apps, 1);
            Apps.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            Grid.SetColumn(Apps, 0);
            Grid.SetColumnSpan(Apps, 2);
            Apps.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }
}
