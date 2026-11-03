using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
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
    private readonly TaskbarReveal _reveal;
    private readonly TaskPinDrag _drag = new();
    private readonly TaskbarStops _stops;
    private bool _barMenu;

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
        _stops = new TaskbarStops(this, StartButton, SearchButton, TasksHost, Corner);
        FaceHost.SizeChanged += OnFaceHostSize;
        Bar.SizeChanged += OnLaidOut;
        Corner.SizeChanged += OnLaidOut;
        Strip.SizeChanged += OnStripSize;
        PointerWheelChanged += OnWheel;
        _tray = new TrayAreaView(tray);
        _clock = new TaskbarClock(window, preferences, time, center);
        _quick = new QuickSettingsFlyout(window, _tray.QuickSettingsAnchor, settings, preferences, time);
        _start = new StartFlyout(window, StartButton, model, start, apps, documents, loop, log, preferences, time);
        _previews = new TaskPreviewFlyout(window, model, preferences, time, ButtonFor, AnyButton, StartButton);
        _start.Popup.PropertyChanged += OnStart;
        _previews.Model.PropertyChanged += OnPreview;
        _quick.OpenChanged += OnSurface;
        _clock.OpenChanged += OnSurface;
        _tray.OpenChanged += OnSurface;
        _reveal = new TaskbarReveal(time);
        _reveal.PropertyChanged += OnReveal;
        _reveal.SetAutoHide(_preferences.TaskbarAutoHide);
        ApplyBar();
        Corner.Children.Add(_tray);
        Corner.Children.Add(_clock);
        Place();
        _preferences.PropertyChanged += OnPreferences;
        Model.Updated += OnTasksUpdated;
        Model.ActiveWindowChanged += OnActiveWindow;
        PreviewKeyDown += OnPreviewKeyDown;
        GotFocus += OnGotFocus;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
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
        SyncOpen();
        _window.Focus();
        FocusStop(_stops.FirstApp);
    }

    /// <summary>A menu of this bar opened or closed. An auto-hiding bar reads it.</summary>
    internal void SurfacesChanged() => SyncOpen();

    /// <summary>A drag is in progress, so a hover preview does not open.</summary>
    internal bool PinDragActive => _drag.Active;

    /// <summary>This button is the one being dragged.</summary>
    internal bool IsPinDrag(TaskButton button) => _drag.Active && IndexOf(button) == _drag.DragIndex;

    /// <summary>A left press on a pinned app, without Shift.</summary>
    internal bool BeginPinDrag(TaskButton button)
    {
        var index = IndexOf(button);
        if (index < 0 || !_drag.Press(index, Model.Tasks[index].Pinned, leftButton: true, shift: false))
            return false;
        ShowDrop();
        return true;
    }

    /// <summary>The pointer moved. Landing on another pin closes the hover preview.</summary>
    internal void MovePinDrag(TaskButton button, double xInButton)
    {
        var index = IndexOf(button);
        if (index < 0 || index != _drag.DragIndex)
            return;
        if (_drag.Move(TaskStrip.ButtonX(index) + xInButton, PinnedCount()))
            _previews.Model.Hide();
        ShowDrop();
    }

    /// <summary>The press ended. A landing on another pin is saved after this click has been swallowed.</summary>
    internal void EndPinDrag(TaskButton button)
    {
        if (!IsPinDrag(button))
            return;
        var move = _drag.Release(Model.Tasks);
        ShowDrop();
        DispatcherQueue.TryEnqueue(() =>
        {
            _drag.ClearSuppress();
            if (move is { } pin)
                Model.MovePin(pin.Id, pin.Target);
        });
    }

    /// <summary>The press was cancelled. Nothing is reordered.</summary>
    internal void CancelPinDrag(TaskButton button)
    {
        if (!IsPinDrag(button))
            return;
        _drag.Cancel();
        ShowDrop();
    }

    /// <summary>The release reordered, so this click does nothing. A press that stayed put still clicks.</summary>
    internal bool TakeDragClick()
    {
        if (!_drag.SuppressClick)
            return false;
        _drag.ClearSuppress();
        return true;
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
        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        FaceHost.SizeChanged -= OnFaceHostSize;
        Bar.SizeChanged -= OnLaidOut;
        Corner.SizeChanged -= OnLaidOut;
        Strip.SizeChanged -= OnStripSize;
        PointerWheelChanged -= OnWheel;
        _start.Popup.PropertyChanged -= OnStart;
        _previews.Model.PropertyChanged -= OnPreview;
        _quick.OpenChanged -= OnSurface;
        _clock.OpenChanged -= OnSurface;
        _tray.OpenChanged -= OnSurface;
        _reveal.PropertyChanged -= OnReveal;
        _reveal.Dispose();
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
        SyncOpen();
        _window.Focusable = false;
        IsTabStop = true;
        Focus(FocusState.Programmatic);
        IsTabStop = false;
        back?.Activate();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        RevealFocused();
        if (_keyboard.Active)
            return;
        if (e.OriginalSource is not DependencyObject source || ReferenceEquals(source, this))
            return;
        if (!_stops.IsStop(source))
            return;
        _keyboard.Enter(Model.ActiveWindow);
        _window.Focusable = true;
        SyncOpen();
    }

    private void OnActiveWindow(object? sender, EventArgs e)
    {
        // Another window came forward. Nothing is activated: it already has the keyboard.
        if (!_keyboard.Active || Model.ActiveWindow is null)
            return;
        _keyboard.Yield();
        _window.Focusable = false;
        SyncOpen();
    }

    private void OnTasksUpdated(object? sender, EventArgs e)
    {
        // The strip's width follows the apps, and a focused button that moved is scrolled back into view.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Place);
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
                FocusStop(_stops.FirstApp);
                return;
            }
            if (node is Button)
                return;
        }
        // The app that had the keyboard is gone, and focus was not already on Start, Search, the tray or the clock.
        FocusStop(_stops.FirstApp);
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
        var count = _stops.Count;
        FocusStop(TaskbarKeyboard.Move(_stops.Focused(), e.Key == VirtualKey.Left ? -1 : 1, count));
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
        menu.Opened += (_, _) =>
        {
            _barMenu = true;
            SyncOpen();
        };
        menu.Closed += (_, _) =>
        {
            _barMenu = false;
            SyncOpen();
        };
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
        else if (e.PropertyName == nameof(Preferences.TaskbarAutoHide))
            _reveal.SetAutoHide(_preferences.TaskbarAutoHide);
    }

    private void OnStart(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(StartPopup.IsOpen) or nameof(StartPopup.SearchOpen)))
            return;
        StartFill.Visibility = _start.Popup.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        SearchFill.Visibility = _start.Popup.IsOpen && _start.Popup.SearchOpen ? Visibility.Visible : Visibility.Collapsed;
        SyncOpen();
    }

    private void OnPreview(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskPreview.IsOpen))
            SyncOpen();
    }

    private void OnSurface(object? sender, EventArgs e) => SyncOpen();

    private void OnReveal(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ApplyBar();

    /// <summary>
    /// Auto-hide lays the bar on the whole screen and keeps a two-pixel line while it is down, so the pointer at the
    /// bottom edge reaches it and the edge is not reserved. Off, the bar reserves its height again.
    /// </summary>
    private void ApplyBar()
    {
        if (_reveal.ReservesEdge)
        {
            _window.Height = TaskbarReveal.ShownHeight;
            _window.ReservedSize = TaskbarReveal.ShownHeight;
            _window.CoversScreen = false;
            return;
        }
        _window.CoversScreen = true;
        _window.ReservedSize = 0;
        _window.Height = _reveal.WindowHeight;
    }

    private void SyncOpen() => _reveal.SetOpen(new TaskbarSurfaces(
        _start.Popup.IsOpen,
        _quick.IsOpen,
        _clock.IsOpen,
        _previews.Model.IsOpen,
        TaskMenuOpen(),
        _tray.Open,
        _barMenu,
        _keyboard.Active));

    private bool TaskMenuOpen()
    {
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (TasksHost.TryGetElement(i) is TaskButton { MenuIsOpen: true })
                return true;
        }
        return false;
    }

    private void OnFaceHostSize(object sender, SizeChangedEventArgs e)
    {
        if (Face.Width != e.NewSize.Width)
            Face.Width = e.NewSize.Width;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _reveal.SetPointer(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // The event bubbles from a button. The pointer left the bar only when it is outside this control.
        var point = e.GetCurrentPoint(this).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight)
            return;
        _reveal.SetPointer(false);
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
        var count = _stops.Count;
        if (count <= 0)
            return;
        if ((uint)index >= (uint)count)
            index = 0;
        var stop = _stops.At(index);
        if (stop is not null)
        {
            stop.Focus(FocusState.Keyboard);
            RevealApp(index);
            return;
        }
        var place = index;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_keyboard.Active)
                return;
            _stops.At(place)?.Focus(FocusState.Keyboard);
            RevealApp(place);
        });
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

    /// <summary>The apps sit in the middle until they would meet the tray, and what does not fit scrolls.</summary>
    private void Place()
    {
        var content = Bar.ActualWidth;
        if (content <= 0)
            return;
        var natural = TaskStrip.NaturalWidth(Model.Tasks.Count);
        var (x, strip) = TaskStrip.Place(content, Corner.ActualWidth, natural, _preferences.TaskbarCentered);
        var margin = new Thickness(x, 0, 0, 0);
        if (Apps.Margin != margin)
            Apps.Margin = margin;
        if (Strip.Width != strip)
            Strip.Width = strip;
        if (TasksHost.Width != natural)
            TasksHost.Width = natural;
        RevealFocused();
    }

    private void OnLaidOut(object sender, SizeChangedEventArgs e) => Place();

    private void OnStripSize(object sender, SizeChangedEventArgs e) => RevealFocused();

    /// <summary>A downward wheel over the apps moves toward the later ones. The tray and the clock keep their own wheel.</summary>
    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled || Strip.ViewportWidth <= 0 || Strip.ScrollableWidth <= 0)
            return;
        var point = e.GetCurrentPoint(Strip);
        if (point.Properties.IsHorizontalMouseWheel)
            return;
        var at = point.Position;
        if (at.X < 0 || at.Y < 0 || at.X > Strip.ActualWidth || at.Y > Strip.ActualHeight)
            return;
        var content = double.IsFinite(TasksHost.Width) && TasksHost.Width > 0 ? TasksHost.Width : TasksHost.ActualWidth;
        var next = TaskStrip.Wheel(Strip.HorizontalOffset, content, Strip.ViewportWidth, point.Properties.MouseWheelDelta);
        if (Math.Abs(next - Strip.HorizontalOffset) < 0.5)
            return;
        Strip.ChangeView(next, null, null, true);
        e.Handled = true;
    }

    /// <summary>Start is 0 and Search is 1. An app after those is scrolled fully into view.</summary>
    private void RevealApp(int stop)
    {
        var index = stop - 2;
        if ((uint)index < (uint)Model.Tasks.Count)
            RevealIndex(index);
    }

    private void RevealFocused() => RevealIndex(_stops.FocusedApp());

    private int IndexOf(TaskButton button)
    {
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (ReferenceEquals(TasksHost.TryGetElement(i), button))
                return i;
        }
        if (button.Item is not { } item)
            return -1;
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (Model.Tasks[i].Key == item.Key)
                return i;
        }
        return -1;
    }

    /// <summary>Pins are the buttons at the start.</summary>
    private int PinnedCount()
    {
        var count = 0;
        foreach (var task in Model.Tasks)
        {
            if (!task.Pinned)
                break;
            count++;
        }
        return count;
    }

    /// <summary>The line before or after the pin this drag would land on, and the dragged button faded.</summary>
    private void ShowDrop()
    {
        for (var i = 0; i < Model.Tasks.Count; i++)
        {
            if (TasksHost.TryGetElement(i) is TaskButton button)
                button.Opacity = _drag.Moved && i == _drag.DragIndex ? 0.5 : 1;
        }
        if (!_drag.Moved)
        {
            DropMark.Visibility = Visibility.Collapsed;
            return;
        }
        var target = _stops.App(_drag.DropIndex);
        if (target is null)
        {
            DropMark.Visibility = Visibility.Collapsed;
            return;
        }
        var before = _drag.DropIndex < _drag.DragIndex;
        var point = target.TransformToVisual(Bar).TransformPoint(new Point(TaskPinDrag.MarkerOffset(target.ActualWidth, before), 0));
        DropMark.Margin = new Thickness(point.X, point.Y, 0, 0);
        DropMark.Height = target.ActualHeight > 0 ? target.ActualHeight : TaskStrip.Button;
        DropMark.Visibility = Visibility.Visible;
    }

    private void RevealIndex(int index)
    {
        if ((uint)index >= (uint)Model.Tasks.Count || Strip.ViewportWidth <= 0)
            return;
        var next = TaskStrip.Reveal(Strip.HorizontalOffset, Strip.ViewportWidth, TaskStrip.ButtonX(index), TaskStrip.Button);
        if (Math.Abs(next - Strip.HorizontalOffset) < 0.5)
            return;
        Strip.ChangeView(next, null, null, true);
    }
}
