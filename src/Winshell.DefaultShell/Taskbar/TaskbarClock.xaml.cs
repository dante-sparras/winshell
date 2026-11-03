using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The time and the date on the right of the taskbar. A click opens the notification center and calendar.</summary>
public sealed partial class TaskbarClock : Button
{
    private readonly PanelWindow _bar;
    private readonly Preferences _preferences;
    private readonly TimeProvider _time;
    private readonly NotificationCenter _center;
    private readonly TaskbarClockModel _model;
    private readonly CalendarMonth _month;
    private readonly CalendarFlyoutView _flyout;
    private readonly PopupWindow _popup;
    private readonly DispatcherTimer _timer = new();
    private DateTimeOffset _dismissed;

    public TaskbarClock(PanelWindow bar, Preferences preferences, TimeProvider time, NotificationCenter center)
    {
        _bar = bar;
        _preferences = preferences;
        _time = time;
        _center = center;
        _model = new TaskbarClockModel(time, preferences);
        _month = new CalendarMonth(time);
        _flyout = new CalendarFlyoutView(_month, center, preferences);
        _popup = new PopupWindow { GrabFocus = true, Corners = WindowCorners.Round, Content = _flyout };
        InitializeComponent();
        _popup.Dismissed += OnDismissed;
        _flyout.CloseRequested += OnClose;
        _timer.Tick += (_, _) => Tick();
        preferences.PropertyChanged += OnPreferences;
        center.PropertyChanged += OnCenter;
        Tick();
    }

    /// <summary>The notification center and calendar are open, so an auto-hiding taskbar stays up.</summary>
    internal bool IsOpen => _popup.Visible;

    /// <summary>The notification center and calendar opened or closed.</summary>
    internal event EventHandler? OpenChanged;

    /// <summary>Drops the popup and what the clock follows. The bar is about to close, and the center stays.</summary>
    public void Release()
    {
        _preferences.PropertyChanged -= OnPreferences;
        _center.PropertyChanged -= OnCenter;
        _timer.Stop();
        _flyout.Release();
        _popup.Dispose();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.ClockSeconds))
            Tick();
    }

    private void OnCenter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationCenter.Count))
            Show();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (_popup.Visible)
            OnClose();
        else if (!TaskbarClockModel.ClosedBySameClick(_dismissed, _time.GetUtcNow()))
            Open();
    }

    /// <summary>Win+N. It flips, including the click that just closed it. A clock that is not on screen yet does nothing.</summary>
    public void Toggle()
    {
        if (_popup.Visible)
            OnClose();
        else if (IsLoaded)
            Open();
    }

    private void Open()
    {
        if (_bar.Screen is not { } screen)
            return;
        var point = TransformToVisual(null).TransformPoint(default);
        var origin = _bar.Origin;
        _popup.Screen = screen;
        _popup.Rect = CalendarFlyoutPlacement.Place(new Rect(origin.X + point.X, origin.Y + point.Y, ActualWidth, ActualHeight), screen.WorkArea);
        _popup.Backdrop = _preferences.Material;
        _month.Date = _time.GetLocalNow().LocalDateTime;
        _month.Today();
        _popup.Visible = true;
        OpenChanged?.Invoke(this, EventArgs.Empty);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, _flyout.FocusClose);
    }

    private void OnDismissed()
    {
        _dismissed = _time.GetUtcNow();
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClose()
    {
        _popup.Visible = false;
        OpenChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        Show();
        _timer.Stop();
        _timer.Interval = TaskbarClockModel.UntilNext(_time.GetLocalNow(), _preferences.ClockSeconds);
        _timer.Start();
    }

    private void Show()
    {
        var local = _time.GetLocalNow().LocalDateTime;
        _model.Refresh(_center.Count);
        Readout.Text = _model.Text;
        AutomationProperties.SetName(this, _model.AccessibleName);
        _center.NoteClock();
        if (_month.Date.Date != local.Date)
            _month.Date = local;
    }
}
