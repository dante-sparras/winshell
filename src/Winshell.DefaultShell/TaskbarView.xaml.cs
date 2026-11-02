using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The taskbar of one screen: Start, Search, the apps, and the clock.</summary>
public sealed partial class TaskbarView : UserControl
{
    private readonly Preferences _preferences;
    private readonly TimeProvider _time;

    public TaskbarView(TaskbarModel model, Preferences preferences, TimeProvider time)
    {
        Model = model;
        _preferences = preferences;
        _time = time;
        InitializeComponent();
        Clock.Attach(preferences, time);
        Place();
        Model.Tasks.CollectionChanged += OnTasksChanged;
        _preferences.PropertyChanged += OnPreferences;
        Loaded += (_, _) => RefreshTasks();
        Unloaded += (_, _) => Release();
    }

    public TaskbarModel Model { get; }

    /// <summary>Drops the clock and the preference subscription. The window is about to close.</summary>
    public void Release()
    {
        Model.Tasks.CollectionChanged -= OnTasksChanged;
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

    private void OnTaskPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is TaskButton button && (uint)args.Index < (uint)Model.Tasks.Count)
            button.Item = Model.Tasks[args.Index];
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshTasks();

    private void RefreshTasks()
    {
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (TasksHost.TryGetElement(i) is TaskButton button)
                button.Item = Model.Tasks[i];
        }
    }
}
