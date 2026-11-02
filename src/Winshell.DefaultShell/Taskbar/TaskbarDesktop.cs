using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Apps;
using Winshell.Windows;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The open windows and the app index, wrapped so <see cref="Tasks"/> can read them. An adapter is kept for as long as
/// its window is open, so the one used last still compares equal after the buttons are built again.
/// </summary>
internal sealed class TaskbarDesktop : IDisposable
{
    private readonly WindowListService _windows;
    private readonly AppsService _apps;
    private readonly Dictionary<AppWindow, TaskWindow> _byWindow = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public TaskbarDesktop(WindowListService windows, AppsService apps)
    {
        _windows = windows;
        _apps = apps;
        windows.Windows.CollectionChanged += OnWindows;
        Resync();
    }

    public ObservableCollection<ITaskWindow> Windows { get; } = [];

    public INotifyCollectionChanged AppChanges => _apps.Apps;

    public INotifyPropertyChanged ActiveChanged => _windows;

    public ITaskWindow? Active => _windows.ActiveWindow is { } window && _byWindow.TryGetValue(window, out var adapter) ? adapter : null;

    public ITaskApp? Find(string name) => _apps.Find(name) is { } entry ? new TaskApp(entry) : null;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _windows.Windows.CollectionChanged -= OnWindows;
    }

    private void OnWindows(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
            return;
        if (e.Action == NotifyCollectionChangedAction.Move && e.OldItems?.Count == 1)
        {
            Windows.Move(e.OldStartingIndex, e.NewStartingIndex);
            return;
        }
        Resync();
    }

    private void Resync()
    {
        var next = _windows.Windows;
        var keep = new HashSet<AppWindow>(ReferenceEqualityComparer.Instance);
        foreach (var window in next)
            keep.Add(window);
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            var adapter = (TaskWindow)Windows[i];
            if (keep.Contains(adapter.Window))
                continue;
            _byWindow.Remove(adapter.Window);
            Windows.RemoveAt(i);
        }
        for (var i = 0; i < next.Count; i++)
        {
            var window = next[i];
            if (!_byWindow.TryGetValue(window, out var adapter))
            {
                adapter = new TaskWindow(window);
                _byWindow[window] = adapter;
                Windows.Insert(i, adapter);
                continue;
            }
            var at = Windows.IndexOf(adapter);
            if (at != i)
                Windows.Move(at, i);
        }
    }
}
