using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using TaskbarTasks = Winshell.DefaultShell.Taskbar.Tasks;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// One taskbar's buttons. It turns the open windows and the pins into <see cref="TaskItem"/>s for its screen, remembers
/// the window each app used last, and does what a click asks. It has no XAML.
/// </summary>
public sealed class TaskbarModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<ITaskWindow> _windows;
    private readonly INotifyCollectionChanged _windowChanges;
    private readonly Func<string, ITaskApp?> _find;
    private readonly INotifyCollectionChanged? _appChanges;
    private readonly Func<IReadOnlyList<string>> _pins;
    private readonly INotifyPropertyChanged _pinChanges;
    private readonly Func<ITaskWindow?> _activeWindow;
    private readonly INotifyPropertyChanged _activeChanges;
    private readonly Preferences _preferences;
    private readonly Action<string> _startProgram;
    private readonly List<ITaskWindow> _watched = [];
    private readonly Dictionary<string, ITaskWindow> _lastUsed = [];
    private ScreenInfo _screen;
    private bool _isMain;
    private bool _disposed;

    public TaskbarModel(
        IReadOnlyList<ITaskWindow> windows,
        INotifyCollectionChanged windowChanges,
        Func<string, ITaskApp?> find,
        INotifyCollectionChanged? appChanges,
        Func<IReadOnlyList<string>> pins,
        INotifyPropertyChanged pinChanges,
        Func<ITaskWindow?> activeWindow,
        INotifyPropertyChanged activeChanges,
        Preferences preferences,
        ScreenInfo screen,
        bool isMain,
        Action<string> startProgram)
    {
        _windows = windows;
        _windowChanges = windowChanges;
        _find = find;
        _appChanges = appChanges;
        _pins = pins;
        _pinChanges = pinChanges;
        _activeWindow = activeWindow;
        _activeChanges = activeChanges;
        _preferences = preferences;
        _screen = screen;
        _isMain = isMain;
        _startProgram = startProgram;
        _windowChanges.CollectionChanged += OnWindows;
        if (_appChanges is not null)
            _appChanges.CollectionChanged += OnApps;
        _pinChanges.PropertyChanged += OnPins;
        _activeChanges.PropertyChanged += OnActive;
        _preferences.PropertyChanged += OnPreferences;
        Resubscribe();
        NoteActive();
        Rebuild();
    }

    /// <summary>The buttons, pinned apps first. Replaced when the buttons change. A window's title does not replace them.</summary>
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    /// <summary>The Start button was clicked. Nothing opens.</summary>
    public event EventHandler? StartRequested;

    /// <summary>The Search button was clicked. Nothing opens.</summary>
    public event EventHandler? SearchRequested;

    internal static TaskbarModel Live(TaskbarDesktop desktop, TaskbarPins pins, Preferences preferences, ScreenInfo screen, bool isMain, Action<string> startProgram) =>
        new(desktop.Windows, desktop.Windows, desktop.Find, desktop.AppChanges, () => pins.Ids, pins, () => desktop.Active, desktop.ActiveChanged, preferences, screen, isMain, startProgram);

    public void RequestStart() => StartRequested?.Invoke(this, EventArgs.Empty);

    public void RequestSearch() => SearchRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The screen this bar is on, and whether it is the main one. A change rebuilds the buttons.</summary>
    public void SetScreen(ScreenInfo screen, bool isMain)
    {
        var changed = _screen.Id != screen.Id || _isMain != isMain;
        _screen = screen;
        _isMain = isMain;
        if (changed)
            Rebuild();
    }

    /// <summary>A left click: launch, minimize the window in front, or bring one forward.</summary>
    public void Click(TaskItem task)
    {
        _lastUsed.TryGetValue(task.Key, out var last);
        switch (TaskbarTasks.Click(task, last))
        {
            case TaskClick.Launch:
                Launch(task);
                break;
            case TaskClick.Minimize { Window: ITaskControl window }:
                window.Minimize();
                break;
            case TaskClick.Activate { Window: ITaskControl window }:
                window.Activate();
                break;
        }
    }

    /// <summary>A middle click or a Shift click: a new window of the app.</summary>
    public void Launch(TaskItem task)
    {
        if (task.Entry is ITaskLaunch app)
            app.Launch();
        else if (task.Windows is [{ Executable.Length: > 0 } first, ..])
            _startProgram(first.Executable);
        else if (TaskbarTasks.IsProgramPath(task.Pin))
            _startProgram(task.Pin);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _windowChanges.CollectionChanged -= OnWindows;
        if (_appChanges is not null)
            _appChanges.CollectionChanged -= OnApps;
        _pinChanges.PropertyChanged -= OnPins;
        _activeChanges.PropertyChanged -= OnActive;
        _preferences.PropertyChanged -= OnPreferences;
        Unwatch();
    }

    private void OnWindows(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
            return;
        Resubscribe();
        DropClosed();
        Rebuild();
    }

    private void OnApps(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnPins(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnActive(object? sender, PropertyChangedEventArgs e) => NoteActive();

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Preferences.TaskbarAppsOn) or nameof(Preferences.TaskbarAllScreens))
            Rebuild();
    }

    private void OnWindowProperty(object? sender, PropertyChangedEventArgs e)
    {
        // A title changes while someone types. The button reads it from the window. The buttons stay put.
        if (e.PropertyName is nameof(ITaskWindow.AppId)
            or nameof(ITaskWindow.Activated)
            or nameof(ITaskWindow.Minimized)
            or nameof(ITaskWindowView.Attention)
            or nameof(ITaskWindowView.Screens))
            Rebuild();
    }

    private void NoteActive()
    {
        if (_disposed || _activeWindow() is not { } window)
            return;
        _lastUsed[TaskbarTasks.WindowKey(window, _find)] = window;
    }

    private void DropClosed()
    {
        foreach (var key in _lastUsed.Keys.ToArray())
        {
            if (!_windows.Any(window => Equals(window, _lastUsed[key])))
                _lastUsed.Remove(key);
        }
    }

    private void Resubscribe()
    {
        Unwatch();
        foreach (var window in _windows)
        {
            _watched.Add(window);
            if (window is INotifyPropertyChanged source)
                source.PropertyChanged += OnWindowProperty;
        }
    }

    private void Unwatch()
    {
        foreach (var window in _watched)
        {
            if (window is INotifyPropertyChanged source)
                source.PropertyChanged -= OnWindowProperty;
        }
        _watched.Clear();
    }

    /// <summary>"all", or "main" and "where", and every window when this is the only taskbar.</summary>
    private string AppsOn => _preferences.TaskbarAllScreens ? _preferences.AppsOn : "all";

    private bool ShowsAll => AppsOn == "all" || (AppsOn == "main" && _isMain);

    private bool ShowPins => AppsOn == "all" || _isMain;

    private bool Shows(ITaskWindow window) =>
        ShowsAll || (window is ITaskWindowView view && view.Screens.Any(screen => screen.Id == _screen.Id));

    private void Rebuild()
    {
        if (_disposed)
            return;
        var next = TaskbarTasks.Build(_windows, _pins(), _find, Shows, ShowPins);
        if (next.Count == Tasks.Count)
        {
            for (var i = 0; i < next.Count; i++)
                Tasks[i] = next[i];
            return;
        }
        Tasks.Clear();
        foreach (var task in next)
            Tasks.Add(task);
    }

    /// <summary>How a button draws its running line, from the windows it has now.</summary>
    public readonly record struct Indicator(bool Running, bool Active, bool Attention)
    {
        /// <summary>16 while a window of the app is in front, otherwise 6.</summary>
        public int LineWidth => Active ? 16 : 6;

        public static Indicator Of(TaskItem? task)
        {
            if (task is null || task.Windows.Count == 0)
                return default;
            var active = false;
            var attention = false;
            foreach (var window in task.Windows)
            {
                if (window.Activated && !window.Minimized)
                    active = true;
                if (window is ITaskWindowView view && view.Attention)
                    attention = true;
            }
            return new Indicator(true, active, attention);
        }
    }
}
