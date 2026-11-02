using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Apps;
using Winshell.Input;

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
    private readonly Start _start;
    private readonly Hotkey _hotkey;
    private readonly ChordRegistration _taskbarChords;
    private readonly DesktopChordSession _desktopChords = new();
    private readonly SettingsHost _settings;
    private readonly List<Bar> _bars = [];
    private DesktopChordHost? _desktopHost;

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
        // One quick settings for every bar, and the same layout for Winshell Settings. Bluetooth is read only while a flyout is open.
        var layout = new QuickSettingsLayout(shell.StatePath("quick-settings.toml"), shell.Loop, shell.Log);
        _quick = new QuickSettings(
            _tray.Status,
            shell.Networking,
            shell.Bluetooth,
            shell.Audio,
            shell.Power,
            shell.PowerMode,
            shell.Brightness,
            layout,
            shell.Media);
        _quick.SettingsRequested += uri => ProgramStart.Start(uri, shell.Log);
        // One Start catalog for every bar, so two bars do not both write the default pins.
        var apps = shell.Apps;
        _start = new Start(Path.Combine(shell.ConfigRoot, "launcher.toml"), shell.Loop, shell.Log, query => apps.Search(query).Select(ToStartApp).ToList());
        _start.Use(apps);
        _start.ShowRecommended = preferences.StartRecommended;
        // The Win tap is the key-up. Pressed is that tap, so Released is not wired: it would close what Pressed just opened.
        _hotkey = shell.NewHotkey();
        _hotkey.Pressed += (_, _) => ToggleStart();
        _hotkey.Sequence = preferences.StartHotkey;
        _settings = new SettingsHost(shell, preferences, _start, layout, _hotkey);
        _tray.UpdatesRequested += (_, _) => _settings.Open("winshellUpdates");
        shell.Screens.CollectionChanged += OnScreens;
        preferences.PropertyChanged += OnPreferences;
        Sync();
        // One hotkey for each taskbar chord, for the life of the shell. The same sequence is not registered twice.
        _taskbarChords = TaskbarChords.Register(() => shell.NewHotkey(), new ChordActions(this));
        SyncDesktopChords();
    }

    private static StartApp ToStartApp(AppEntry app) =>
        new(app.Id, app.Name, app.AppId, app.Icon, app.Target, app.Packaged, app.Hidden);

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
        else if (e.PropertyName == nameof(Preferences.StartHotkey))
            _hotkey.Sequence = _preferences.StartHotkey;
        else if (e.PropertyName == nameof(Preferences.StartRecommended))
            _start.ShowRecommended = _preferences.StartRecommended;
        else if (e.PropertyName == nameof(Preferences.DesktopChords))
            SyncDesktopChords();
    }

    /// <summary>One hotkey. An open Start closes. Otherwise the main taskbar's Start opens.</summary>
    private void ToggleStart()
    {
        var popups = new List<StartPopup>(_bars.Count);
        var main = 0;
        foreach (var bar in _bars)
        {
            if (bar.Window.Content is not TaskbarView view)
                continue;
            if (bar.IsMain)
                main = popups.Count;
            popups.Add(view.StartPopup);
        }
        StartHotkey.Toggle(popups, main);
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
                bar.IsMain = isMain;
                bar.Window.Screen = screen;
                bar.Window.Backdrop = _preferences.Material;
                bar.Model.SetScreen(screen, isMain);
                continue;
            }
            var model = new TaskbarModel(_desktop, _pins, _preferences, screen, isMain, path => ProgramStart.Start(path, _shell.Log));
            model.SettingsRequested += OnSettingsRequested;
            var window = new PanelWindow
            {
                Screen = screen,
                Anchors = Edges.Bottom | Edges.Left | Edges.Right,
                Height = 48,
                ReservedSize = 48,
                Backdrop = _preferences.Material,
            };
            window.Content = new TaskbarView(
                model, _tray, window, _preferences, TimeProvider.System, _notifications, _quick,
                _start, _shell.Apps, _shell.RecentDocuments, _shell.Loop, _shell.Log);
            window.Visible = true;
            _bars.Add(new Bar(model, window, isMain));
        }
        _desktopHost?.Retarget();
    }

    /// <summary>
    /// Alt+Tab and the rest exist only while the preference is on. Turning it off gives the keys back and drops the
    /// windows. A resting shell registers none of them.
    /// </summary>
    private void SyncDesktopChords()
    {
        if (!_preferences.DesktopChords)
        {
            _desktopHost?.Dispose();
            _desktopHost = null;
        }
        _desktopChords.Sync(
            _preferences.DesktopChords,
            () => _shell.NewHotkey(),
            () => _desktop.Windows,
            () => _desktop.Active,
            () => MainWindow() is not null);
        if (!_preferences.DesktopChords)
            return;
        _desktopHost ??= new DesktopChordHost(_shell, _desktopChords.Switcher, _preferences, MainWindow, MainView, () => _settings.Open("taskbar"));
        _desktopHost.Retarget();
    }

    private TaskbarView? MainView()
    {
        TaskbarView? first = null;
        foreach (var bar in _bars)
        {
            if (bar.Window.Content is not TaskbarView view)
                continue;
            if (bar.IsMain)
                return view;
            first ??= view;
        }
        return first;
    }

    private PanelWindow? MainWindow()
    {
        foreach (var bar in _bars)
        {
            if (bar.IsMain)
                return bar.Window;
        }
        return _bars.Count == 0 ? null : _bars[0].Window;
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => _settings.Open("taskbar");

    private void Close(Bar bar)
    {
        bar.Model.SettingsRequested -= OnSettingsRequested;
        if (bar.Window.Content is TaskbarView view)
            view.Release();
        bar.Model.Dispose();
        bar.Window.Dispose();
    }

    private sealed class ChordActions(Taskbars bars) : ITaskbarChordActions
    {
        public void FocusApps() => bars.MainView()?.FocusApps();

        public void ToggleQuickSettings() => bars.MainView()?.ToggleQuickSettings();

        public void ToggleNotifications() => bars.MainView()?.ToggleNotifications();

        public void OpenSearch() => bars.MainView()?.OpenSearch();

        public void InvokeApp(int index, bool newWindow) => bars.MainView()?.InvokeApp(index, newWindow);
    }

    private sealed class Bar(TaskbarModel model, PanelWindow window, bool isMain)
    {
        public TaskbarModel Model { get; } = model;

        public PanelWindow Window { get; } = window;

        public bool IsMain { get; set; } = isMain;
    }
}
