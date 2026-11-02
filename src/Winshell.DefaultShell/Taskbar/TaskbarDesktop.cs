using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Apps;
using Winshell.Windows;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The open windows and the app index one taskbar reads.</summary>
public interface ITaskDesktop
{
    /// <summary>The open windows, in the order they opened.</summary>
    IReadOnlyList<IOpenWindow> Windows { get; }

    /// <summary>The window in front, when there is one.</summary>
    IOpenWindow? Active { get; }

    /// <summary>The app index entry for an id, an app id, or a name.</summary>
    ITaskApp? Find(string name);

    /// <summary>The jump list for an AppUserModelID. The same object comes back for an id, and a later read updates it.</summary>
    AppJumpList JumpList(string appId);

    /// <summary>The window list or the app index changed.</summary>
    event EventHandler? Changed;

    /// <summary>The window in front changed.</summary>
    event EventHandler? ActiveChanged;
}

/// <summary>
/// The shell's windows and app index, as <see cref="ITaskDesktop"/>. A fresh <see cref="TaskWindow"/> compares equal
/// to another for the same window, so nothing here keeps the adapters.
/// </summary>
internal sealed class TaskbarDesktop : ITaskDesktop
{
    private readonly WindowListService _windows;
    private readonly AppsService _apps;
    private readonly JumpListService _jumps;

    public TaskbarDesktop(WindowListService windows, AppsService apps, JumpListService jumps)
    {
        _windows = windows;
        _apps = apps;
        _jumps = jumps;
        windows.Windows.CollectionChanged += OnWindows;
        apps.Apps.CollectionChanged += OnApps;
        windows.PropertyChanged += OnActiveWindow;
    }

    public IReadOnlyList<IOpenWindow> Windows => [.. _windows.Windows.Select(static window => new TaskWindow(window))];

    public IOpenWindow? Active => _windows.ActiveWindow is { } window ? new TaskWindow(window) : null;

    public ITaskApp? Find(string name) => _apps.Find(name) is { } entry ? new TaskApp(entry) : null;

    public AppJumpList JumpList(string appId) => _jumps.ForApp(appId);

    public event EventHandler? Changed;

    public event EventHandler? ActiveChanged;

    private void OnWindows(object? sender, NotifyCollectionChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnApps(object? sender, NotifyCollectionChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnActiveWindow(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowListService.ActiveWindow))
            ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
