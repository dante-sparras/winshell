using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Winshell.Apps;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>Start above one taskbar. The catalog is shared. This window is not. Search reads recent documents off the UI thread.</summary>
internal sealed class StartFlyout
{
    private readonly PanelWindow _bar;
    private readonly Button _startButton;
    private readonly TaskbarModel _model;
    private readonly AppsService _apps;
    private readonly RecentDocuments _documents;
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly Preferences _preferences;
    private readonly TimeProvider _time;
    private readonly StartPopup _popup;
    private readonly StartView _view;
    private readonly PopupWindow _window;
    private DateTimeOffset _dismissed;
    private int _generation;
    private bool _released;

    public StartFlyout(
        PanelWindow bar,
        Button startButton,
        TaskbarModel model,
        Start start,
        AppsService apps,
        RecentDocuments documents,
        IEventLoop loop,
        Log log,
        Preferences preferences,
        TimeProvider time)
    {
        _bar = bar;
        _startButton = startButton;
        _model = model;
        _apps = apps;
        _documents = documents;
        _loop = loop;
        _log = log;
        _preferences = preferences;
        _time = time;
        _popup = new StartPopup(start);
        _view = new StartView(_popup);
        _window = new PopupWindow { GrabFocus = true, Corners = WindowCorners.Round, Content = _view };
        _window.Dismissed += OnWindowDismissed;
        _popup.PropertyChanged += OnPopup;
        _popup.Launched += OnLaunched;
        _popup.LinkChosen += OnLink;
        _popup.LocationChosen += OnLocation;
        _popup.Dismissed += OnModelDismissed;
        _model.StartRequested += OnStart;
        _model.SearchRequested += OnSearch;
        _preferences.PropertyChanged += OnPreferences;
    }

    public StartPopup Popup => _popup;

    /// <summary>Drops the window. The bar is about to close, and the shared catalog stays.</summary>
    public void Release()
    {
        _released = true;
        _model.StartRequested -= OnStart;
        _model.SearchRequested -= OnSearch;
        _preferences.PropertyChanged -= OnPreferences;
        _popup.PropertyChanged -= OnPopup;
        _popup.Launched -= OnLaunched;
        _popup.LinkChosen -= OnLink;
        _popup.LocationChosen -= OnLocation;
        _popup.Dismissed -= OnModelDismissed;
        _popup.Close();
        _popup.Release();
        _view.Release();
        _window.Dismissed -= OnWindowDismissed;
        _window.Dispose();
    }

    private void OnStart(object? sender, EventArgs e) => _popup.Toggle(_dismissed, _time.GetUtcNow());

    private void OnSearch(object? sender, EventArgs e) => _popup.OpenSearch();

    private void OnWindowDismissed()
    {
        _dismissed = _time.GetUtcNow();
        _popup.Close();
    }

    private void OnModelDismissed(object? sender, EventArgs e) => _popup.Close();

    private void OnLaunched(object? sender, StartApp app)
    {
        _apps.ById(app.Id)?.Launch();
        _popup.Close();
    }

    private void OnLink(object? sender, string uri)
    {
        ProgramStart.Start(uri, _log);
        _popup.Close();
    }

    private void OnLocation(object? sender, StartApp app)
    {
        var command = StartList.LocationCommand(app);
        if (command.Count >= 2)
            ProgramStart.Start(command[0], _log, command[1]);
        _popup.Close();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.TaskbarCentered) && _popup.IsOpen)
            Place();
        else if (e.PropertyName == nameof(Preferences.Backdrop) && _window.Visible)
            _window.Backdrop = _preferences.Material;
    }

    private void OnPopup(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartPopup.Query))
            SearchFiles();
        if (e.PropertyName is not (nameof(StartPopup.IsOpen) or nameof(StartPopup.SearchOpen)))
            return;
        if (_popup.IsOpen)
            Show();
        else
            Hide();
    }

    private void Show()
    {
        if (_bar.Screen is not { } screen)
            return;
        _window.Screen = screen;
        Place();
        _window.Backdrop = _preferences.Material;
        _view.SetIcons(true);
        if (_window.Visible)
            return;
        _window.Visible = true;
        // Showing a window takes the foreground only when Windows allows it, and a hotkey's hook does not grant that.
        _window.Focus();
        _view.FocusSearch();
    }

    private void Hide()
    {
        _view.SetIcons(false);
        _window.Visible = false;
    }

    private void SearchFiles()
    {
        var query = _popup.Query;
        var generation = ++_generation;
        if (query.Length == 0)
            return;
        var documents = _documents;
        _ = Task.Run(() =>
        {
            var found = documents.Search(query);
            _loop.Post(() =>
            {
                if (_released || generation != _generation)
                    return;
                _popup.SetDocuments(found);
            });
        });
    }

    /// <summary>Above the taskbar: centered on it, or lined up with the Start button.</summary>
    private void Place()
    {
        if (_bar.Screen is not { } screen)
            return;
        var point = _startButton.TransformToVisual(null).TransformPoint(default);
        var origin = _bar.Origin;
        var width = _bar.ActualWidth > 0 ? _bar.ActualWidth : screen.Geometry.Width;
        var height = _bar.ActualHeight > 0 ? _bar.ActualHeight : 48;
        var taskbar = new QRect(origin.X, origin.Y, width, height);
        _window.Rect = StartPlacement.Above(taskbar, screen.WorkArea, _preferences.TaskbarCentered, origin.X + point.X);
    }
}
