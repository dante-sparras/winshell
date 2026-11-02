using System.Collections.Specialized;
using System.ComponentModel;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The taskbars: one on every screen, or only the main one. A screen that arrives or leaves follows the same rule.</summary>
internal sealed class Taskbars
{
    private readonly Shell _shell;
    private readonly Preferences _preferences;
    private readonly TaskbarPins _pins;
    private readonly TaskbarDesktop _desktop;
    private readonly TrayArea _tray;
    private readonly NotificationCenter _notifications;
    private readonly QuickSettings _quick;
    private readonly List<Bar> _bars = [];

    public Taskbars(Shell shell, Preferences preferences, TaskbarPins pins)
    {
        _shell = shell;
        _preferences = preferences;
        _pins = pins;
        _desktop = new TaskbarDesktop(shell.WindowList, shell.Apps, shell.JumpLists);
        // One tray for every bar. Reading it makes this process the notification area, and a bar closing must not do that again.
        _tray = new TrayArea(shell.SystemTray, new SystemStatus(shell.Networking, shell.Audio, shell.Power, shell.PowerMode), shell.Updates);
        // One center for every bar. Reading the listener never asks Windows for access.
        _notifications = new NotificationCenter(shell.Notifications, preferences, TimeProvider.System);
        // One quick settings for every bar. Bluetooth is read only while a flyout is open.
        _quick = new QuickSettings(
            _tray.Status,
            shell.Networking,
            shell.Bluetooth,
            shell.Audio,
            shell.Power,
            shell.PowerMode,
            shell.Brightness,
            new QuickSettingsLayout(shell.StatePath("quick-settings.toml"), shell.Loop, shell.Log),
            shell.Media);
        _quick.SettingsRequested += uri => ProgramStart.Start(uri, shell.Log);
        shell.Screens.CollectionChanged += OnScreens;
        preferences.PropertyChanged += OnPreferences;
        Sync();
    }

    private void OnScreens(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.TaskbarAllScreens))
            Sync();
        else if (e.PropertyName == nameof(Preferences.Backdrop))
        {
            foreach (var bar in _bars)
                bar.Window.Backdrop = _preferences.Material;
        }
    }

    private void Sync()
    {
        var screens = _shell.Screens;
        var main = screens.FirstOrDefault(screen => screen.IsPrimary) ?? screens.FirstOrDefault();
        List<ScreenInfo> wanted = _preferences.TaskbarAllScreens ? [.. screens] : main is null ? [] : [main];
        var ids = wanted.Select(screen => screen.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = _bars.Count - 1; i >= 0; i--)
        {
            if (_bars[i].Window.Screen is { } shown && ids.Contains(shown.Id))
                continue;
            Close(_bars[i]);
            _bars.RemoveAt(i);
        }
        foreach (var screen in wanted)
        {
            var isMain = main is not null && screen.Id == main.Id;
            var bar = _bars.Find(item => item.Window.Screen?.Id == screen.Id);
            if (bar is not null)
            {
                bar.Window.Screen = screen;
                bar.Window.Backdrop = _preferences.Material;
                bar.Model.SetScreen(screen, isMain);
                continue;
            }
            var model = new TaskbarModel(_desktop, _pins, _preferences, screen, isMain, path => ProgramStart.Start(path, _shell.Log));
            var window = new PanelWindow
            {
                Screen = screen,
                Anchors = Edges.Bottom | Edges.Left | Edges.Right,
                Height = 48,
                ExclusiveZone = 48,
                Backdrop = _preferences.Material,
            };
            window.Content = new TaskbarView(model, _tray, window, _preferences, TimeProvider.System, _notifications, _quick);
            window.Visible = true;
            _bars.Add(new Bar(model, window));
        }
    }

    private static void Close(Bar bar)
    {
        if (bar.Window.Content is TaskbarView view)
            view.Release();
        bar.Model.Dispose();
        bar.Window.Dispose();
    }

    private sealed class Bar(TaskbarModel model, PanelWindow window)
    {
        public TaskbarModel Model { get; } = model;

        public PanelWindow Window { get; } = window;
    }
}
