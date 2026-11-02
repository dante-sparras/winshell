using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The taskbar of one screen: Start, Search, the apps, the tray and the clock.</summary>
public sealed partial class TaskbarView : UserControl
{
    private readonly Preferences _preferences;
    private readonly TrayAreaView _tray;
    private readonly TaskbarClock _clock;
    private PanelWindow? _panel;
    private PopupWindow? _popup;
    private QuickSettingsPopup? _flyout;
    private QuickSettingsView? _quick;

    public TaskbarView(TaskbarModel model, TrayArea tray, PanelWindow window, Preferences preferences, TimeProvider time, NotificationCenter center)
    {
        Model = model;
        _preferences = preferences;
        InitializeComponent();
        _tray = new TrayAreaView(tray);
        _clock = new TaskbarClock(window, preferences, time, center);
        Corner.Children.Add(_tray);
        Corner.Children.Add(_clock);
        Place();
        _preferences.PropertyChanged += OnPreferences;
        ContextRequested += OnContextRequested;
    }

    public TaskbarModel Model { get; }

    /// <summary>The flyout for this bar. Quick settings is shared; the window is not.</summary>
    public void BindQuickSettings(PanelWindow panel, QuickSettings settings)
    {
        _panel = panel;
        _quick = new QuickSettingsView(settings.CreatePopup());
        _flyout = _quick.Popup;
        _popup = new PopupWindow
        {
            Backdrop = _preferences.Material,
            Corners = WindowCorners.Round,
            GrabFocus = true,
            Content = _quick,
        };
        _popup.Dismissed += OnDismissed;
        _flyout.PropertyChanged += OnFlyout;
        panel.PropertyChanged += OnPanel;
        _tray.QuickSettingsClicked += OnQuickSettings;
        _tray.SizeChanged += OnAnchorMoved;
        _tray.QuickSettingsAnchor.SizeChanged += OnAnchorMoved;
        _quick.SizeChanged += OnAnchorMoved;
        SizeChanged += OnAnchorMoved;
    }

    /// <summary>Drops the tray, the clock, the flyout and the preference subscription. The window is about to close.</summary>
    public void Release()
    {
        _preferences.PropertyChanged -= OnPreferences;
        SizeChanged -= OnAnchorMoved;
        _tray.QuickSettingsClicked -= OnQuickSettings;
        _tray.SizeChanged -= OnAnchorMoved;
        _tray.QuickSettingsAnchor.SizeChanged -= OnAnchorMoved;
        if (_panel is not null)
            _panel.PropertyChanged -= OnPanel;
        if (_quick is not null)
        {
            _quick.SizeChanged -= OnAnchorMoved;
            _quick.Release();
        }
        if (_flyout is not null)
        {
            _flyout.PropertyChanged -= OnFlyout;
            _flyout.Hide();
        }
        if (_popup is not null)
        {
            _popup.Dismissed -= OnDismissed;
            _popup.Dispose();
        }
        _panel = null;
        _popup = null;
        _flyout = null;
        _quick = null;
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
        else if (e.PropertyName == nameof(Preferences.Backdrop) && _popup is not null)
            _popup.Backdrop = _preferences.Material;
    }

    /// <summary>
    /// The pointer down has already posted the dismiss. This click is the release, so a flyout that dismiss closed
    /// opens after that dismiss has run.
    /// </summary>
    private void OnQuickSettings(object? sender, EventArgs e)
    {
        if (_flyout is { IsOpen: true })
            _flyout.Hide();
        else
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, OpenQuickSettings);
    }

    private void OpenQuickSettings()
    {
        if (_flyout is not { IsOpen: false } || _popup is null || _quick is null || _panel is null)
            return;
        PlacePopup();
        _flyout.Show();
        _popup.Visible = true;
        _quick.Focus(FocusState.Programmatic);
    }

    private void OnDismissed() => _flyout?.Hide();

    private void OnFlyout(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsPopup.IsOpen) && _flyout is { IsOpen: false } && _popup is not null)
            _popup.Visible = false;
    }

    private void OnAnchorMoved(object sender, SizeChangedEventArgs e)
    {
        if (_flyout is { IsOpen: true })
            PlacePopup();
    }

    private void OnPanel(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelWindow.Origin) && _flyout is { IsOpen: true })
            PlacePopup();
    }

    /// <summary>Above the quick settings button, lined up with its right edge.</summary>
    private void PlacePopup()
    {
        if (_popup is null || _quick is null || _panel is null)
            return;
        var anchor = _tray.QuickSettingsAnchor;
        var point = anchor.TransformToVisual(null).TransformPoint(default);
        var origin = _panel.Origin;
        var button = new QRect(origin.X + point.X, origin.Y + point.Y, anchor.ActualWidth, anchor.ActualHeight);
        var height = _quick.ActualHeight;
        if (height <= 0)
        {
            _quick.Measure(new Size(QuickSettingsPlacement.Width, double.PositiveInfinity));
            height = _quick.DesiredSize.Height;
        }
        _popup.Rect = QuickSettingsPlacement.Above(button, height);
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
