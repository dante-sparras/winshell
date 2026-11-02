using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
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
    private readonly PanelWindow _window;
    private readonly TaskbarKeyboard _keyboard = new();
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
        _window = window;
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
        Model.Updated += OnTasksUpdated;
        Model.ActiveWindowChanged += OnActiveWindow;
        PreviewKeyDown += OnPreviewKeyDown;
        GotFocus += OnGotFocus;
        ContextRequested += OnContextRequested;
    }

    public TaskbarModel Model { get; }

    internal StartPopup StartPopup => _start.Popup;

    /// <summary>The live previews for this bar's buttons.</summary>
    internal TaskPreview Previews => _previews.Model;

    /// <summary>Win+T. The keyboard lands on the first app, or the next control when there is none.</summary>
    public void FocusApps()
    {
        _keyboard.Enter(Model.ActiveWindow);
        _window.Focusable = true;
        _window.Focus();
        FocusStop(TaskbarKeyboard.FirstApp(Model.Tasks.Count, StopCount()));
    }

    /// <summary>Win+A. Quick settings flips, with no same-click guard.</summary>
    public void ToggleQuickSettings() => _quick.Toggle();

    /// <summary>Win+N. The notification center flips, with no same-click guard.</summary>
    public void ToggleNotifications() => _clock.Toggle();

    /// <summary>Win+S. Search opens.</summary>
    public void OpenSearch() => Model.RequestSearch();

    /// <summary>Win+number. The preview closes first, as a click on the app does.</summary>
    public void InvokeApp(int index, bool newWindow)
    {
        _previews.Model.Hide();
        Model.InvokeAt(index, newWindow);
    }

    /// <summary>Drops the tray, the clock, quick settings, Start and the preference subscription. The window is about to close.</summary>
    public void Release()
    {
        _preferences.PropertyChanged -= OnPreferences;
        Model.Updated -= OnTasksUpdated;
        Model.ActiveWindowChanged -= OnActiveWindow;
        PreviewKeyDown -= OnPreviewKeyDown;
        GotFocus -= OnGotFocus;
        _start.Release();
        _previews.Release();
        _quick.Release();
        _tray.Release();
        _clock.Release();
    }

    /// <summary>Escape gives the keyboard back. The mode ends first, so that window coming forward does not end it again.</summary>
    private void LeaveKeyboard()
    {
        var back = _keyboard.Leave();
        _window.Focusable = false;
        IsTabStop = true;
        Focus(FocusState.Programmatic);
        IsTabStop = false;
        back?.Activate();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (_keyboard.Active)
            return;
        if (e.OriginalSource is not DependencyObject source || ReferenceEquals(source, this))
            return;
        if (!IsBarStop(source))
            return;
        _keyboard.Enter(Model.ActiveWindow);
        _window.Focusable = true;
    }

    private void OnActiveWindow(object? sender, EventArgs e)
    {
        // Another window came forward. Nothing is activated: it already has the keyboard.
        if (!_keyboard.Active || Model.ActiveWindow is null)
            return;
        _keyboard.Yield();
        _window.Focusable = false;
    }

    private void OnTasksUpdated(object? sender, EventArgs e)
    {
        if (!_keyboard.Active)
            return;
        if (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused)
            return;
        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TaskButton button)
            {
                if (button.Item is { } item && Model.Tasks.Any(task => task.Key == item.Key))
                    return;
                FocusStop(TaskbarKeyboard.FirstApp(Model.Tasks.Count, StopCount()));
                return;
            }
            if (node is Button)
                return;
        }
        // The app that had the keyboard is gone, and focus was not already on Start, Search, the tray or the clock.
        FocusStop(TaskbarKeyboard.FirstApp(Model.Tasks.Count, StopCount()));
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_keyboard.Active || KeysBelongToAMenu(e.OriginalSource as DependencyObject))
            return;
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            LeaveKeyboard();
            return;
        }
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right))
            return;
        e.Handled = true;
        var count = StopCount();
        FocusStop(TaskbarKeyboard.Move(IndexOfFocus(), e.Key == VirtualKey.Left ? -1 : 1, count));
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

    private void FocusStop(int index)
    {
        var count = StopCount();
        if (count <= 0)
            return;
        if ((uint)index >= (uint)count)
            index = 0;
        var stop = StopAt(index);
        if (stop is not null)
        {
            stop.Focus(FocusState.Keyboard);
            return;
        }
        var place = index;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_keyboard.Active)
                return;
            StopAt(place)?.Focus(FocusState.Keyboard);
        });
    }

    /// <summary>Start, Search, the apps, then the tray and the clock that are on the bar.</summary>
    private int StopCount() => 2 + Model.Tasks.Count + CornerStops().Count;

    private Control? StopAt(int index)
    {
        if (index == 0)
            return StartButton;
        if (index == 1)
            return SearchButton;
        var apps = Model.Tasks.Count;
        if (index < 2 + apps)
            return TaskButtonAt(index - 2);
        var corners = CornerStops();
        var at = index - 2 - apps;
        return (uint)at < (uint)corners.Count ? corners[at] : null;
    }

    private TaskButton? TaskButtonAt(int index)
    {
        if ((uint)index >= (uint)Model.Tasks.Count)
            return null;
        var element = TasksHost.TryGetElement(index);
        if (element is null && IsLoaded)
        {
            try
            {
                element = TasksHost.GetOrCreateElement(index);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        return element as TaskButton;
    }

    private int IndexOfFocus()
    {
        var fallback = TaskbarKeyboard.FirstApp(Model.Tasks.Count, StopCount());
        if (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused)
            return fallback;
        if (Contains(StartButton, focused))
            return 0;
        if (Contains(SearchButton, focused))
            return 1;
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (TasksHost.TryGetElement(i) is DependencyObject element && Contains(element, focused))
                return 2 + i;
        }
        var corners = CornerStops();
        for (var i = 0; i < corners.Count; i++)
        {
            if (Contains(corners[i], focused))
                return 2 + Model.Tasks.Count + i;
        }
        return fallback;
    }

    private bool KeysBelongToAMenu(DependencyObject? source)
    {
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (TasksHost.TryGetElement(i) is TaskButton { MenuIsOpen: true })
                return true;
        }
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FlyoutPresenter or MenuFlyoutPresenter)
                return true;
        }
        return false;
    }

    /// <summary>A button of this bar, and not one inside a menu or the hidden-icons flyout.</summary>
    private bool IsBarStop(DependencyObject source)
    {
        var inMenu = false;
        Button? button = null;
        for (DependencyObject? node = source; node is not null && !ReferenceEquals(node, this); node = VisualTreeHelper.GetParent(node))
        {
            if (node is FlyoutPresenter or MenuFlyoutPresenter or Popup)
                inMenu = true;
            if (button is null && node is Button found)
                button = found;
        }
        if (button is null || inMenu)
            return false;
        if (ReferenceEquals(button, StartButton) || ReferenceEquals(button, SearchButton) || button is TaskButton)
            return true;
        return CornerStops().Contains(button);
    }

    /// <summary>The tray and the clock. A flyout's buttons, and a control that is collapsed, are not stops.</summary>
    private List<Button> CornerStops()
    {
        var stops = new List<Button>();
        Collect(Corner, stops);
        return stops;

        static void Collect(DependencyObject node, List<Button> stops)
        {
            if (node is FlyoutPresenter or Popup)
                return;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is UIElement { Visibility: not Visibility.Visible })
                    continue;
                if (child is Button button)
                {
                    if (button.IsTabStop)
                        stops.Add(button);
                    continue;
                }
                Collect(child, stops);
            }
        }
    }

    private static bool Contains(DependencyObject root, DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
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
