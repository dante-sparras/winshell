using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The default shell: a taskbar along the bottom of the screen.</summary>
public sealed class DefaultShellConfiguration : ShellConfiguration
{
    private readonly List<Bar> _bars = [];
    private Shell? _shell;
    private Preferences? _preferences;
    private TaskbarPins? _pins;
    private TaskbarDesktop? _desktop;

    public override void Start(Shell shell)
    {
        _shell = shell;
        _preferences = new Preferences(shell.DataPath("preferences.toml"), shell.Loop, shell.Log);
        _pins = new TaskbarPins(shell.DataPath("taskbar.json"), shell.Loop, shell.Log);
        _desktop = new TaskbarDesktop(shell.WindowList, shell.Apps);
        shell.Screens.CollectionChanged += OnScreens;
        _preferences.PropertyChanged += OnPreferences;
        SyncBars();
    }

    private void OnScreens(object? sender, NotifyCollectionChangedEventArgs e) => SyncBars();

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (_preferences is null)
            return;
        if (e.PropertyName == nameof(Preferences.TaskbarAllScreens))
            SyncBars();
        else if (e.PropertyName == nameof(Preferences.Backdrop))
        {
            foreach (var bar in _bars)
                bar.Window.Backdrop = _preferences.Material;
        }
    }

    /// <summary>One taskbar on every screen, or only the main one. A screen that arrives or leaves follows the same rule.</summary>
    private void SyncBars()
    {
        if (_shell is null || _preferences is null || _pins is null || _desktop is null)
            return;
        var screens = _shell.Screens;
        var main = screens.FirstOrDefault(screen => screen.IsPrimary) ?? screens.FirstOrDefault();
        List<ScreenInfo> wanted = _preferences.TaskbarAllScreens ? [.. screens] : main is null ? [] : [main];
        var ids = wanted.Select(screen => screen.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = _bars.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(_bars[i].Screen.Id))
                continue;
            Close(_bars[i]);
            _bars.RemoveAt(i);
        }
        foreach (var screen in wanted)
        {
            var isMain = main is not null && screen.Id == main.Id;
            var bar = _bars.Find(item => item.Screen.Id == screen.Id);
            if (bar is not null)
            {
                bar.Screen = screen;
                bar.Window.Screen = screen;
                bar.Window.Backdrop = _preferences.Material;
                bar.Model.SetScreen(screen, isMain);
                continue;
            }
            var model = TaskbarModel.Live(_desktop, _pins, _preferences, screen, isMain, path => ProgramStart.Start(path, _shell.Log));
            var view = new TaskbarView(model, _preferences, TimeProvider.System);
            var window = new PanelWindow
            {
                Screen = screen,
                Anchors = Edges.Bottom | Edges.Left | Edges.Right,
                Height = 48,
                ExclusiveZone = 48,
                Backdrop = _preferences.Material,
                Content = view,
            };
            window.Visible = true;
            _bars.Add(new Bar(screen, model, window));
        }
    }

    private static void Close(Bar bar)
    {
        if (bar.Window.Content is TaskbarView view)
            view.Release();
        bar.Model.Dispose();
        bar.Window.Dispose();
    }

    private sealed class Bar(ScreenInfo screen, TaskbarModel model, PanelWindow window)
    {
        public ScreenInfo Screen { get; set; } = screen;

        public TaskbarModel Model { get; } = model;

        public PanelWindow Window { get; } = window;
    }
}
