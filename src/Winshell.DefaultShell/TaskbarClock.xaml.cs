using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The time and the date on the right of the taskbar. A click opens the notification center and calendar.</summary>
public sealed partial class TaskbarClock : Button
{
    private (TaskbarClockModel Model, Preferences Preferences, TimeProvider Time, DispatcherTimer Timer, NotificationCenter Center, CalendarMonth Month, CalendarFlyoutView Flyout)? _run;
    private PanelWindow? _window;
    private PopupWindow? _popup;
    // A click on the clock is outside the popup, so the grab closes it before this click arrives.
    private bool _suppressOpen;

    public TaskbarClock() => InitializeComponent();

    public void Attach(Preferences preferences, TimeProvider time, NotificationCenter center)
    {
        Detach();
        var month = new CalendarMonth(time);
        var flyout = new CalendarFlyoutView(month, center, preferences);
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => Show();
        _run = (new TaskbarClockModel(time, preferences), preferences, time, timer, center, month, flyout);
        flyout.CloseRequested += OnClose;
        preferences.PropertyChanged += OnPreferences;
        center.PropertyChanged += OnCenter;
        Show();
    }

    public void Use(PanelWindow window) => _window = window;

    /// <summary>The popup uses the taskbar's material. A popup that is not open yet picks it up when it opens.</summary>
    public void UseBackdrop()
    {
        if (_popup is not null && _run is { } run)
            _popup.Backdrop = run.Preferences.Material;
    }

    public void Detach()
    {
        if (_run is not { } run)
            return;
        run.Preferences.PropertyChanged -= OnPreferences;
        run.Center.PropertyChanged -= OnCenter;
        run.Flyout.CloseRequested -= OnClose;
        run.Timer.Stop();
        run.Flyout.Release();
        if (_popup is not null)
        {
            _popup.Dismissed -= OnDismissed;
            _popup.Content = null;
            _popup.Dispose();
            _popup = null;
        }
        _run = null;
        _window = null;
        _suppressOpen = false;
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.ClockSeconds))
            Show();
    }

    private void OnCenter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationCenter.Count))
            Show(restart: false);
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressOpen)
            return;
        if (_popup is { Visible: true })
        {
            OnClose();
            return;
        }
        Open();
    }

    private void Open()
    {
        if (_run is not { } run)
            return;
        var popup = EnsurePopup(run);
        if (_window is { Screen: { } screen } window)
        {
            popup.Screen = screen;
            var point = TransformToVisual(null).TransformPoint(new global::Windows.Foundation.Point(0, 0));
            var origin = window.Origin;
            popup.Rect = CalendarFlyoutPlacement.Place(new QRect(origin.X + point.X, origin.Y + point.Y, ActualWidth, ActualHeight), screen.WorkArea);
        }
        popup.Backdrop = run.Preferences.Material;
        var local = run.Time.GetLocalNow().LocalDateTime;
        run.Month.Date = local;
        run.Month.Today();
        popup.Visible = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, run.Flyout.FocusClose);
    }

    private PopupWindow EnsurePopup((TaskbarClockModel Model, Preferences Preferences, TimeProvider Time, DispatcherTimer Timer, NotificationCenter Center, CalendarMonth Month, CalendarFlyoutView Flyout) run)
    {
        if (_popup is not null)
            return _popup;
        var popup = new PopupWindow
        {
            GrabFocus = true,
            Corners = WindowCorners.Round,
            Content = run.Flyout,
        };
        popup.Dismissed += OnDismissed;
        _popup = popup;
        return popup;
    }

    private void OnDismissed()
    {
        _suppressOpen = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _suppressOpen = false);
    }

    private void OnClose()
    {
        if (_popup is not null)
            _popup.Visible = false;
        Focus(FocusState.Programmatic);
    }

    private void Show(bool restart = true)
    {
        if (_run is not { } run)
            return;
        var local = run.Time.GetLocalNow().LocalDateTime;
        run.Model.Refresh(run.Center.Count);
        Readout.Text = run.Model.Text;
        AutomationProperties.SetName(this, run.Model.AccessibleName);
        run.Center.NoteClock();
        if (run.Month.Date.Date != local.Date)
            run.Month.Date = local;
        if (!restart)
            return;
        run.Timer.Stop();
        run.Timer.Interval = TaskbarClockModel.UntilNext(run.Time.GetLocalNow(), run.Preferences.ClockSeconds);
        run.Timer.Start();
    }
}
