using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Apps;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>One row of a taskbar menu. A destination, and the row that says the jump list cannot be read, are not actions.</summary>
public readonly record struct TaskMenuRow(string Id, string Label, string Glyph, string Icon, bool Enabled);

/// <summary>
/// The rows of the menu a task button opens, in the order Windows 11 shows them: the jump list, the app's name,
/// pin or unpin, then close when it has windows.
/// </summary>
public static class TaskMenu
{
    private const string PinGlyph = "\uE718";
    private const string UnpinGlyph = "\uE77A";
    private const string CloseGlyph = "\uE8BB";

    /// <summary>What Narrator calls the menu a right click on an empty part of the bar opens.</summary>
    public const string BarName = "Taskbar";

    /// <summary>The only row of that menu.</summary>
    public static TaskMenuRow Settings { get; } = new("taskbarSettings", "Taskbar settings", "\uE713", "", true);

    /// <summary>The rows for <paramref name="task"/> from the jump list as it stands now.</summary>
    public static IReadOnlyList<TaskMenuRow> Rows(TaskItem task, AppJumpList jump)
    {
        var rows = new List<TaskMenuRow>();
        if (!jump.Available)
            rows.Add(new TaskMenuRow("unavailable", "Jump list can't be read", "", "", false));
        else
        {
            var names = jump.Destinations;
            for (var i = 0; i < names.Count; i++)
                rows.Add(new TaskMenuRow("dest:" + i, names[i], "", "", false));
        }
        rows.Add(new TaskMenuRow("launch", Tasks.Name(task), "", task.Icon, true));
        // One slot, so the row stays put while the app is pinned or unpinned.
        rows.Add(task.Pinned
            ? new TaskMenuRow("pin", "Unpin from taskbar", UnpinGlyph, "", true)
            : new TaskMenuRow("pin", "Pin to taskbar", PinGlyph, "", true));
        if (task.Windows.Count > 0)
            rows.Add(new TaskMenuRow("close", task.Windows.Count == 1 ? "Close window" : "Close all windows", CloseGlyph, "", true));
        return rows;
    }

    /// <summary>Does what the row asks. A destination is only shown: opening it would launch a document, and this menu does not.</summary>
    public static void Invoke(TaskbarModel model, TaskItem task, string id)
    {
        if (id == "unavailable" || id.StartsWith("dest:", StringComparison.Ordinal))
            return;
        switch (id)
        {
            case "launch":
                model.Launch(task);
                break;
            case "pin" when task.Pinned:
                model.Unpin(task);
                break;
            case "pin":
                model.Pin(task);
                break;
            case "close":
                model.Close(task);
                break;
        }
    }
}

/// <summary>
/// The menu open for one app. Its rows follow that app and its jump list, and it closes when the app leaves the bar.
/// </summary>
public sealed class OpenTaskMenu : IDisposable
{
    private readonly TaskbarModel _model;
    private readonly string _key;
    private readonly List<INotifyPropertyChanged> _watched = [];
    private AppJumpList? _jump;
    private IReadOnlyList<TaskMenuRow> _rows = [];
    private string _name = "";
    private bool _open = true;
    private bool _refreshing;

    public OpenTaskMenu(TaskbarModel model, TaskItem task)
    {
        _model = model;
        _key = task.Key;
        _model.Tasks.CollectionChanged += OnTasks;
        Refresh(reread: true);
    }

    /// <summary>The app this menu was opened for.</summary>
    public string Key => _key;

    /// <summary>False once the app has left the bar, or the menu was closed.</summary>
    public bool IsOpen => _open;

    /// <summary>What Narrator calls the menu: the app's name.</summary>
    public string Name => _name;

    public IReadOnlyList<TaskMenuRow> Rows => _rows;

    /// <summary>The rows or the name changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The menu closed.</summary>
    public event EventHandler? Closed;

    public void Invoke(string id)
    {
        // A click can arrive as the menu closes. The app is still on the bar, so the row still acts.
        if (Find() is { } task)
            TaskMenu.Invoke(_model, task, id);
    }

    public void Dispose() => Close();

    private void OnTasks(object? sender, NotifyCollectionChangedEventArgs e) => Refresh(reread: true);

    private void OnJump(object? sender, PropertyChangedEventArgs e)
    {
        // The list filled in, or a later read updated it. Read what it holds now.
        if (!_refreshing)
            Refresh(reread: false);
    }

    private void OnWatched(object? sender, PropertyChangedEventArgs e)
    {
        if (!_refreshing)
            Refresh(reread: false);
    }

    private void Refresh(bool reread)
    {
        if (!_open || _refreshing)
            return;
        _refreshing = true;
        try
        {
            var task = Find();
            if (task is null)
            {
                Close();
                return;
            }
            var jump = reread || _jump is null ? _model.JumpList(task) : _jump;
            Follow(jump);
            Watch(task);
            var rows = TaskMenu.Rows(task, jump);
            var name = Tasks.Name(task);
            if (name == _name && _rows.SequenceEqual(rows))
                return;
            _name = name;
            _rows = rows;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private TaskItem? Find()
    {
        foreach (var task in _model.Tasks)
        {
            if (task.Key == _key)
                return task;
        }
        return null;
    }

    private void Follow(AppJumpList jump)
    {
        if (ReferenceEquals(_jump, jump))
            return;
        if (_jump is not null)
            _jump.PropertyChanged -= OnJump;
        _jump = jump;
        _jump.PropertyChanged += OnJump;
    }

    private void Watch(TaskItem task)
    {
        foreach (var source in _watched)
            source.PropertyChanged -= OnWatched;
        _watched.Clear();
        Watch(task.Entry);
        foreach (var window in task.Windows)
            Watch(window);
    }

    private void Watch(object? instance)
    {
        if (instance is not INotifyPropertyChanged source)
            return;
        source.PropertyChanged += OnWatched;
        _watched.Add(source);
    }

    private void Close()
    {
        if (!_open)
            return;
        _open = false;
        _model.Tasks.CollectionChanged -= OnTasks;
        if (_jump is not null)
            _jump.PropertyChanged -= OnJump;
        _jump = null;
        foreach (var source in _watched)
            source.PropertyChanged -= OnWatched;
        _watched.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
