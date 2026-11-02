using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Winshell.Apps;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The taskbar of one screen: Start, Search, the apps, the tray and the clock.</summary>
public sealed partial class TaskbarView : UserControl
{
    private readonly Preferences _preferences;
    private readonly TrayAreaView _tray;
    private readonly TaskbarClock _clock;
    private readonly QuickSettingsFlyout _quick;
    private readonly StartFlyout _start;
    private readonly TaskPreviewFlyout _previews;

    public TaskbarView(
        TaskbarModel model,
        TrayArea tray,
        PanelWindow window,
        Preferences preferences,
        TimeProvider time,
        NotificationCenter center,
        QuickSettings settings,
        Start start,
        AppsService apps,
        RecentDocuments documents,
        IEventLoop loop,
        Log log)
    {
        Model = model;
        _preferences = preferences;
        InitializeComponent();
        _tray = new TrayAreaView(tray);
        _clock = new TaskbarClock(window, preferences, time, center);
        _quick = new QuickSettingsFlyout(window, _tray.QuickSettingsAnchor, settings, preferences, time);
        _start = new StartFlyout(window, StartButton, model, start, apps, documents, loop, log, preferences, time);
        _previews = new TaskPreviewFlyout(window, model, preferences, time, ButtonFor, AnyButton, StartButton);
        _start.Popup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(StartPopup.IsOpen) or nameof(StartPopup.SearchOpen)))
                return;
            StartFill.Visibility = _start.Popup.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            SearchFill.Visibility = _start.Popup.IsOpen && _start.Popup.SearchOpen ? Visibility.Visible : Visibility.Collapsed;
        };
        Corner.Children.Add(_tray);
        Corner.Children.Add(_clock);
        Place();
        _preferences.PropertyChanged += OnPreferences;
        ContextRequested += OnContextRequested;
    }

    public TaskbarModel Model { get; }

    internal StartPopup StartPopup => _start.Popup;

    /// <summary>The live previews for this bar's buttons.</summary>
    internal TaskPreview Previews => _previews.Model;

    /// <summary>Drops the tray, the clock, quick settings, Start and the preference subscription. The window is about to close.</summary>
    public void Release()
    {
        _preferences.PropertyChanged -= OnPreferences;
        _start.Release();
        _previews.Release();
        _quick.Release();
        _tray.Release();
        _clock.Release();
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.Handled)
            return;
        if (args.OriginalSource is DependencyObject source && InsideButtonOrClock(source))
            return;
        if (!args.TryGetPosition(this, out var point))
            return;
        args.Handled = true;
        var menu = TaskMenuFlyout.Create(TaskMenu.BarName, [TaskMenu.Settings], _ => Model.RequestSettings(), 16);
        menu.ShowAt(this, new FlyoutShowOptions { Position = point, Placement = FlyoutPlacementMode.Top });
    }

    /// <summary>A button or the clock handles its own pointer. Empty bar is everything else.</summary>
    private static bool InsideButtonOrClock(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button or TaskbarClock)
                return true;
        }
        return false;
    }

    private void OnPreferences(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.TaskbarCentered))
            Place();
    }

    private TaskButton? ButtonFor(string key)
    {
        foreach (var button in Buttons(TasksHost))
        {
            if (button.Item?.Key == key)
                return button;
        }
        return null;
    }

    private TaskButton? AnyButton()
    {
        foreach (var button in Buttons(TasksHost))
            return button;
        return null;
    }

    private static IEnumerable<TaskButton> Buttons(DependencyObject node)
    {
        if (node is TaskButton button)
        {
            yield return button;
            yield break;
        }
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in Buttons(VisualTreeHelper.GetChild(node, i)))
                yield return child;
        }
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
