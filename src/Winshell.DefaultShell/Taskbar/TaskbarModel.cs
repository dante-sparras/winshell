using System.Collections.ObjectModel;
using System.ComponentModel;
using TaskbarTasks = Winshell.DefaultShell.Taskbar.Tasks;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// One taskbar's buttons. It turns the open windows and the pins into <see cref="TaskItem"/>s for its screen, remembers
/// the window each app used last, and does what a click asks. It has no XAML.
/// </summary>
public sealed class TaskbarModel : ObservableObject, IDisposable
{
    private readonly ITaskDesktop _desktop;
    private readonly TaskbarPins _pins;
    private readonly Preferences _preferences;
    private readonly Action<string> _startProgram;
    private readonly List<IOpenWindow> _watched = [];
    private readonly Dictionary<string, IOpenWindow> _lastUsed = [];
    private ScreenInfo _screen;
    private bool _isMain;
    private bool _disposed;

    public TaskbarModel(ITaskDesktop desktop, TaskbarPins pins, Preferences preferences, ScreenInfo screen, bool isMain, Action<string> startProgram)
    {
        _desktop = desktop;
        _pins = pins;
        _preferences = preferences;
        _screen = screen;
        _isMain = isMain;
        _startProgram = startProgram;
        desktop.Changed += OnDesktopChanged;
        desktop.ActiveChanged += OnActiveChanged;
        pins.PropertyChanged += OnPins;
        preferences.PropertyChanged += OnPreferences;
        Resubscribe();
        NoteActive();
        Rebuild();
    }

    /// <summary>The buttons, pinned apps first. A button that did not change stays the same object.</summary>
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    /// <summary>The buttons were rebuilt. One event for the whole change, after every button is in place.</summary>
    public event EventHandler? Updated;

    /// <summary>The window in front, when there is one. Win+T remembers it, and Escape activates it.</summary>
    public IOpenWindow? ActiveWindow => _desktop.Active;

    /// <summary>The window in front changed. The keyboard leaves when another window takes it.</summary>
    public event EventHandler? ActiveWindowChanged;

    /// <summary>Win+number. The app at <paramref name="index"/>, as a click, or a new window when Shift is held. Past the end does nothing.</summary>
    public void InvokeAt(int index, bool newWindow)
    {
        if ((uint)index >= (uint)Tasks.Count)
            return;
        if (newWindow)
            Launch(Tasks[index]);
        else
            Click(Tasks[index]);
    }

    /// <summary>The Start button was clicked. Nothing opens.</summary>
    public event EventHandler? StartRequested;

    /// <summary>The Search button was clicked. Nothing opens.</summary>
    public event EventHandler? SearchRequested;

    public void RequestStart() => StartRequested?.Invoke(this, EventArgs.Empty);

    public void RequestSearch() => SearchRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Taskbar settings was chosen. Nothing opens.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>The bar's own menu asked for settings. Nothing opens.</summary>
    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The rows of this app's menu, from its jump list as it reads now.</summary>
    public IReadOnlyList<TaskMenuRow> MenuRows(TaskItem task) =>
        TaskMenu.Rows(task, _desktop.JumpList(TaskbarTasks.AppUserModelId(task)));

    /// <summary>Does what a row of the menu asks. A destination is only shown: opening it would launch a document, and this menu does not.</summary>
    public void Choose(TaskItem task, string id)
    {
        switch (id)
        {
            case "launch":
                Launch(task);
                break;
            case "pin" when task.Pinned:
                _pins.Unpin(task.Pin);
                break;
            case "pin":
                _pins.Pin(task.PinId);
                break;
            case "close":
                foreach (var window in task.Windows.OfType<IOpenWindow>())
                    window.Close();
                break;
        }
    }

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
            case TaskClick.Minimize { Window: IOpenWindow window }:
                window.Minimize();
                break;
            case TaskClick.Activate { Window: IOpenWindow window }:
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

    /// <summary>A pinned app was dragged onto another. The pins list puts it before that one when moving back, and after it when moving on.</summary>
    public void MovePin(string id, string target) => _pins.Move(id, target);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _desktop.Changed -= OnDesktopChanged;
        _desktop.ActiveChanged -= OnActiveChanged;
        _pins.PropertyChanged -= OnPins;
        _preferences.PropertyChanged -= OnPreferences;
        Unwatch();
    }

    private void OnDesktopChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        Resubscribe();
        DropClosed();
        Rebuild();
    }

    private void OnPins(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnActiveChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        NoteActive();
        ActiveWindowChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Preferences.TaskbarAppsOn) or nameof(Preferences.TaskbarAllScreens))
            Rebuild();
    }

    private void OnWindowProperty(object? sender, PropertyChangedEventArgs e)
    {
        // A title, or a window coming forward, does not move the buttons. The button reads those itself.
        if (e.PropertyName is nameof(ITaskWindow.AppId) or nameof(ITaskWindow.Icon) or nameof(IOpenWindow.Screens))
            Rebuild();
    }

    private void NoteActive()
    {
        if (_disposed || _desktop.Active is not { } window)
            return;
        if (Tasks.FirstOrDefault(task => task.Windows.Contains(window)) is { } owner)
            _lastUsed[owner.Key] = window;
    }

    private void DropClosed()
    {
        var windows = _desktop.Windows;
        foreach (var key in _lastUsed.Keys.ToArray())
        {
            if (!windows.Any(window => Equals(window, _lastUsed[key])))
                _lastUsed.Remove(key);
        }
    }

    private void Resubscribe()
    {
        Unwatch();
        foreach (var window in _desktop.Windows)
        {
            _watched.Add(window);
            window.PropertyChanged += OnWindowProperty;
        }
    }

    private void Unwatch()
    {
        foreach (var window in _watched)
            window.PropertyChanged -= OnWindowProperty;
        _watched.Clear();
    }

    /// <summary>"all", or "main" and "where", and every window when this is the only taskbar.</summary>
    private string AppsOn => _preferences.TaskbarAllScreens ? _preferences.AppsOn : "all";

    private bool ShowsAll => AppsOn == "all" || (AppsOn == "main" && _isMain);

    private bool ShowPins => AppsOn == "all" || _isMain;

    private bool Shows(ITaskWindow window) =>
        ShowsAll || (window is IOpenWindow open && open.Screens.Any(screen => screen.Id == _screen.Id));

    private void Rebuild()
    {
        if (_disposed)
            return;
        Place(TaskbarTasks.Build(_desktop.Windows, _pins.Ids, _desktop.Find, Shows, ShowPins));
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts <paramref name="next"/> into <see cref="Tasks"/> by <see cref="TaskItem.Key"/>. A button that left is removed,
    /// one that arrived is inserted, and one that moved is moved. A button is replaced only when its pin, app, icon or
    /// windows changed, so the others stay the same object.
    /// </summary>
    private void Place(IReadOnlyList<TaskItem> next)
    {
        var keep = new HashSet<string>(next.Count, StringComparer.Ordinal);
        foreach (var task in next)
            keep.Add(task.Key);
        for (var i = Tasks.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Tasks[i].Key))
                Tasks.RemoveAt(i);
        }
        for (var i = 0; i < next.Count; i++)
        {
            var task = next[i];
            var at = IndexOf(task.Key);
            if (at < 0)
            {
                Tasks.Insert(i, task);
                continue;
            }
            if (at != i)
                Tasks.Move(at, i);
            if (!Same(Tasks[i], task))
                Tasks[i] = task;
        }
    }

    private int IndexOf(string key)
    {
        for (var i = 0; i < Tasks.Count; i++)
        {
            if (Tasks[i].Key == key)
                return i;
        }
        return -1;
    }

    /// <summary>The windows are a new list on every build, so the button's own list is compared in order.</summary>
    private static bool Same(TaskItem current, TaskItem next) =>
        current.Pin == next.Pin
        && Equals(current.Entry, next.Entry)
        && current.Icon == next.Icon
        && current.PinId == next.PinId
        && SameWindows(current.Windows, next.Windows);

    private static bool SameWindows(IReadOnlyList<ITaskWindow> current, IReadOnlyList<ITaskWindow> next)
    {
        if (current.Count != next.Count)
            return false;
        for (var i = 0; i < current.Count; i++)
        {
            if (!Equals(current[i], next[i]))
                return false;
        }
        return true;
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
                if (window is IOpenWindow { Attention: true })
                    attention = true;
            }
            return new Indicator(true, active, attention);
        }
    }
}
