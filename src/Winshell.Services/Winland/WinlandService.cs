using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json;
using Winshell.Windows;
using static Winshell.Winland.WinlandProtocol;

namespace Winshell.Winland;

/// <summary>
/// Winland's state. It reads it with <c>j/</c> queries and keeps it current from the event stream, updating the same
/// objects for each event. Winland adds the Monitor to every event that names a Workspace, and it is used to tell
/// same-numbered Workspaces apart. While Winland is not running the lists are empty; they fill when it starts.
/// </summary>
public sealed class WinlandService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly IWinlandLink _link;
    private readonly List<WinlandMonitor> _monitorList = [];
    private readonly List<WinlandWorkspace> _workspaceList = [];
    private readonly List<WinlandWindow> _windowList = [];
    private WindowListService? _windows;
    private WinlandMonitor? _focusedMonitor;
    private WinlandWorkspace? _focusedWorkspace;
    private WinlandWindow? _activeWindow;
    private bool _requestingMonitors, _requestingWorkspaces, _requestingWindows, _monitorsRequested;
    private bool _disposed;

    // Bumped when the connection is lost, so replies to an older connection's requests are dropped.
    private int _generation;

    internal WinlandService(IEventLoop loop, Log log, IWinlandLink? link = null)
    {
        _loop = loop;
        _log = log;
        _link = link ?? new WinlandPipe(log);
        _link.ConnectionChanged += OnConnectionChanged;
        _link.EventReceived += OnEventReceived;
        _link.Start();
        if (_link.IsConnected)
            RefreshAll();
    }

    /// <summary>Emitted for every line of the event stream, after the objects are updated.</summary>
    public event Action<WinlandEvent>? RawEvent;

    /// <summary>The Monitors, by id.</summary>
    public ObservableCollection<WinlandMonitor> Monitors { get; } = [];

    /// <summary>The Workspaces, by id, then by Monitor where two have the same number.</summary>
    public ObservableCollection<WinlandWorkspace> Workspaces { get; } = [];

    /// <summary>The windows Winland manages, in the order they were first reported.</summary>
    public ObservableCollection<WinlandWindow> Windows { get; } = [];

    public WinlandMonitor? FocusedMonitor => _focusedMonitor;

    public WinlandWorkspace? FocusedWorkspace => _focusedWorkspace;

    public WinlandWindow? ActiveWindow => _activeWindow;

    /// <summary>Winland's pipe. It takes requests and, after an <c>events</c> request, streams events.</summary>
    public string PipePath => _link.PipePath;

    /// <summary>The shell's window list, when one is in use. <see cref="WinlandWindow.Window"/> is resolved from it.</summary>
    internal WindowListService? WindowList => _windows;

    /// <summary>Resolves windows from this list. Null clears them. The list is not created just to answer a window.</summary>
    internal void Attach(WindowListService? windows)
    {
        if (ReferenceEquals(_windows, windows))
            return;
        if (_windows is not null)
            _windows.Windows.CollectionChanged -= OnWindowsChanged;
        _windows = windows;
        if (windows is not null)
            windows.Windows.CollectionChanged += OnWindowsChanged;
        foreach (var window in _windowList)
            window.NotifyWindow();
    }

    internal AppWindow? FindWindow(nint handle) => _windows?.Find(handle);

    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var window in _windowList)
            window.NotifyWindow();
    }

    /// <summary>Sends <c>dispatch</c> and the request to Winland. A failure is logged.</summary>
    public void Dispatch(string request) => Send("dispatch " + request);

    /// <summary>Asks Winland for its Monitors again. Updates the listed ones and removes those it no longer reports.</summary>
    public void RefreshMonitors() => RefreshMonitors(false);

    /// <summary>Asks Winland for its Workspaces again and updates the listed ones.</summary>
    public void RefreshWorkspaces() => RefreshWorkspaces(false);

    /// <summary>Asks Winland for its windows again, adding new ones and updating titles and Workspaces.</summary>
    public void RefreshWindows()
    {
        if (_requestingWindows)
            return;
        _requestingWindows = true;
        Request("j/clients", reply =>
        {
            _requestingWindows = false;
            if (ParseJson(reply) is not { ValueKind: JsonValueKind.Array } json)
            {
                RetryIfConnected(reply, RefreshWindows);
                return;
            }
            foreach (var entry in json.EnumerateArray())
            {
                var address = ParseAddress(Str(entry, "address"));
                if (address == 0)
                    continue;
                var window = FindWindow(address, create: true)!;
                window.SetTitle(Str(entry, "title"));
                var ws = Obj(entry, "workspace");
                if (FindWorkspace(Str(ws, "name"), Str(ws, "monitor"), create: true, Int(ws, "id")) is { } workspace)
                    MoveWindow(window, workspace);
                window.SetLastIpcObject(entry);
            }
        });
    }

    /// <summary>
    /// The Monitor of a screen, matched by its <c>\\.\DISPLAYn</c> name. Before Winland's first Monitor list arrives, it
    /// adds a Monitor for the screen that the list fills in later; after it, null when Winland reports no such Monitor.
    /// </summary>
    public WinlandMonitor? MonitorFor(ScreenInfo? screen)
    {
        if (screen is null)
            return null;
        // Winland names a Monitor as Windows does, \\.\DISPLAY1, which is the screen's id.
        var monitor = _monitorList.FirstOrDefault(m => m.Name == screen.Id || m.Name == screen.Name);
        return monitor ?? (_monitorsRequested ? null : FindMonitor(screen.Id, create: true));
    }

    /// <summary>Sends a request whose reply should be <c>ok</c>, and warns otherwise.</summary>
    internal void Send(string request) => Request(request, reply =>
    {
        if (reply is null)
            _log.Warn("winland", $"Failed to send \"{request}\": Winland is not running");
        else if (reply.Split("\n\n").Any(r => r.Trim() != "ok"))
            _log.Warn("winland", $"\"{request}\" failed with error {reply.Trim()}");
    });

    // Connection

    private void OnConnectionChanged(bool connected) => _loop.Post(() =>
    {
        if (_disposed)
            return;
        if (connected)
            RefreshAll();
        else
            Reset();
        Changed(nameof(PipePath));
    });

    private void OnEventReceived(string line) => _loop.Post(() =>
    {
        if (_disposed)
            return;
        var (name, data) = SplitEvent(line);
        try
        {
            OnEvent(name, data);
        }
        finally
        {
            Recompute();
        }
        RawEvent?.Invoke(new WinlandEvent(name, data));
    });

    private void RefreshAll()
    {
        RefreshMonitors(true);
        RefreshWorkspaces(true);
        RefreshWindows();
        // Events only name the active window when the focus moves; asking once shows it before then.
        Request("j/activewindow", reply =>
        {
            if (_activeWindow is null && ParseJson(reply) is { ValueKind: JsonValueKind.Object } json)
                SetActiveWindow(FindWindow(ParseAddress(Str(json, "address")), create: true));
        });
    }

    /// <summary>Winland went away: everything it reported is gone.</summary>
    private void Reset()
    {
        _generation++;
        _requestingMonitors = _requestingWorkspaces = _requestingWindows = false;
        _monitorsRequested = false;
        foreach (var window in _windowList.ToArray())
            RemoveWindow(window);
        foreach (var workspace in _workspaceList.ToArray())
            RemoveWorkspace(workspace);
        foreach (var monitor in _monitorList.ToArray())
            RemoveMonitor(monitor);
        Recompute();
    }

    private void Request(string command, Action<string?> onReply)
    {
        var generation = _generation;
        _ = _link.RequestAsync(command).ContinueWith(t =>
        {
            var reply = t.IsCompletedSuccessfully ? t.Result : null;
            _loop.Post(() =>
            {
                if (_disposed || generation != _generation)
                    return;
                try
                {
                    onReply(reply);
                }
                finally
                {
                    Recompute();
                }
            });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>A query that got no answer while the stream is up is asked again, so a busy Winland still fills the lists.</summary>
    private void RetryIfConnected(string? reply, Action retry)
    {
        if (reply is not null || !_link.IsConnected)
            return;
        var generation = _generation;
        _loop.Schedule(TimeSpan.FromSeconds(1), () =>
        {
            if (!_disposed && generation == _generation)
                retry();
        });
    }

    private static JsonElement? ParseJson(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(reply);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Events

    private void OnEvent(string name, string data)
    {
        switch (name)
        {
            case "configreloaded":
                RefreshAll();
                break;
            case "monitoraddedv2":
                {
                    var args = ParseArgs(data, 3);
                    var monitor = FindMonitor(args[1], create: true)!;
                    monitor.UpdateInitial(ParseInt(args[0]), args[1], args[2]);
                    RefreshMonitors(false);
                    break;
                }
            case "monitorremoved":
                if (FindMonitor(data, create: false) is { } removed)
                    RemoveMonitor(removed);
                break;
            case "createworkspacev2":
                {
                    // id,name,monitor
                    var args = ParseArgs(data, 3);
                    var existed = FindWorkspace(args[1], args[2], create: false) is not null;
                    var workspace = FindWorkspace(args[1], args[2], create: true, ParseId(args[0], args[1]))!;
                    workspace.SetId(ParseId(args[0], args[1]));
                    if (!existed)
                        RefreshWorkspaces(false);
                    break;
                }
            case "destroyworkspacev2":
                {
                    var args = ParseArgs(data, 3);
                    if (FindWorkspace(args[1], args[2], create: false) is { } workspace)
                        RemoveWorkspace(workspace);
                    if (_monitorList.Any(m => m.ActiveWorkspace is null))
                        RefreshMonitors(false);
                    break;
                }
            case "focusedmon":
                {
                    // monitor,workspace name
                    var args = ParseArgs(data, 2);
                    var monitor = FindMonitor(args[0], create: true)!;
                    var workspace = args[1] == "?" ? null : FindWorkspace(args[1], args[0], create: false);
                    SetFocusedMonitor(monitor);
                    monitor.SetActiveWorkspace(workspace);
                    break;
                }
            case "workspacev2":
                {
                    // id,name,monitor: Winland names the Monitor whose shown Workspace changed.
                    var args = ParseArgs(data, 3);
                    var monitor = args[2].Length > 0 ? FindMonitor(args[2], create: true) : _focusedMonitor;
                    if (monitor is not null)
                        monitor.SetActiveWorkspace(FindWorkspace(args[1], monitor.Name, create: true, ParseId(args[0], args[1])));
                    break;
                }
            case "moveworkspacev2":
                {
                    // id,name,monitor it moved to
                    var args = ParseArgs(data, 3);
                    var monitor = FindMonitor(args[2], create: true)!;
                    var workspace = FindWorkspace(args[1], args[2], create: false)
                        ?? _workspaceList.FirstOrDefault(w => w.Name == args[1] && w.Monitor != monitor)
                        ?? FindWorkspace(args[1], args[2], create: true, ParseId(args[0], args[1]))!;
                    workspace.SetMonitor(monitor);
                    // The Workspace's number can change on its new Monitor, and the Monitors' shown Workspaces with it.
                    RefreshWorkspaces(false);
                    RefreshMonitors(false);
                    break;
                }
            case "fullscreen":
                _focusedWorkspace?.SetHasFullscreen(data == "1");
                RefreshWorkspaces(false);
                break;
            case "openwindow":
                {
                    // address,workspace,monitor,class,title
                    var args = ParseArgs(data, 5);
                    var address = ParseAddress(args[0]);
                    if (address == 0)
                        return;
                    if (FindWorkspace(args[1], args[2], create: false) is not { } workspace)
                    {
                        _log.Debug("winland", $"openwindow for workspace {args[1]} which was not previously tracked");
                        return;
                    }
                    var window = FindWindow(address, create: true)!;
                    window.SetTitle(args[4]);
                    MoveWindow(window, workspace);
                    break;
                }
            case "closewindow":
                if (FindWindow(ParseAddress(data), create: false) is { } closed)
                    RemoveWindow(closed);
                break;
            case "movewindowv2":
                {
                    // address,id,name,monitor
                    var args = ParseArgs(data, 4);
                    if (FindWindow(ParseAddress(args[0]), create: false) is { } window
                        && FindWorkspace(args[2], args[3], create: false) is { } workspace)
                        MoveWindow(window, workspace);
                    break;
                }
            case "windowtitlev2":
                {
                    var args = ParseArgs(data, 2);
                    FindWindow(ParseAddress(args[0]), create: true)?.SetTitle(args[1]);
                    break;
                }
            case "activewindowv2":
                SetActiveWindow(FindWindow(ParseAddress(data), create: true));
                break;
            case "urgent":
                FindWindow(ParseAddress(data), create: true)?.SetUrgent(true);
                break;
        }
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static int ParseId(string id, string name) =>
        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : IdFromName(name);

    // Queries

    private void RefreshMonitors(bool canCreate)
    {
        if (_requestingMonitors)
            return;
        _requestingMonitors = true;
        Request("j/monitors", reply =>
        {
            _requestingMonitors = false;
            if (ParseJson(reply) is not { ValueKind: JsonValueKind.Array } json)
            {
                if (canCreate)
                    RetryIfConnected(reply, () => RefreshMonitors(true));
                return;
            }
            _monitorsRequested = true;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in json.EnumerateArray())
            {
                var name = Str(entry, "name");
                var monitor = FindMonitor(name, create: false);
                if (monitor is null)
                {
                    if (!canCreate)
                        continue;
                    monitor = FindMonitor(name, create: true)!;
                }
                names.Add(name);
                monitor.UpdateInitial(Int(entry, "id") ?? -1, name, Str(entry, "description"));
                monitor.UpdateGeometry(Int(entry, "x") ?? 0, Int(entry, "y") ?? 0, Int(entry, "width") ?? 0, Int(entry, "height") ?? 0, Num(entry, "scale"));
                var active = Obj(entry, "activeWorkspace");
                var activeName = Str(active, "name");
                if (activeName.Length > 0 && monitor.ActiveWorkspace?.Name != activeName)
                    monitor.SetActiveWorkspace(FindWorkspace(activeName, name, create: true, Int(active, "id")));
                monitor.SetLastIpcObject(entry);
                if (Bool(entry, "focused"))
                    SetFocusedMonitor(monitor);
            }
            foreach (var monitor in _monitorList.Where(m => !names.Contains(m.Name)).ToArray())
                RemoveMonitor(monitor);
        });
    }

    private void RefreshWorkspaces(bool canCreate)
    {
        if (_requestingWorkspaces)
            return;
        _requestingWorkspaces = true;
        Request("j/workspaces", reply =>
        {
            _requestingWorkspaces = false;
            if (ParseJson(reply) is not { ValueKind: JsonValueKind.Array } json)
            {
                if (canCreate)
                    RetryIfConnected(reply, () => RefreshWorkspaces(true));
                return;
            }
            var entries = json.EnumerateArray().ToList();
            var unnumbered = UnnumberedIds(entries.Select(e => Str(e, "name")));
            var seen = new HashSet<WinlandWorkspace>();
            foreach (var entry in entries)
            {
                var name = Str(entry, "name");
                var monitorName = Str(entry, "monitor");
                var id = Int(entry, "id") ?? unnumbered.GetValueOrDefault(name, IdFromName(name));
                var workspace = FindWorkspace(name, monitorName, create: false);
                if (workspace is null)
                {
                    if (!canCreate)
                        continue;
                    workspace = FindWorkspace(name, monitorName, create: true, id)!;
                }
                workspace.SetId(id);
                if (monitorName.Length > 0)
                    workspace.SetMonitor(FindMonitor(monitorName, create: true, Int(entry, "monitorID") ?? -1));
                workspace.SetHasFullscreen(Bool(entry, "hasfullscreen"));
                workspace.SetLastIpcObject(entry);
                seen.Add(workspace);
            }
            if (canCreate)
            {
                foreach (var workspace in _workspaceList.Where(w => !seen.Contains(w)).ToArray())
                    RemoveWorkspace(workspace);
            }
        });
    }

    // Lookups

    private WinlandMonitor? FindMonitor(string name, bool create, int id = -1)
    {
        var monitor = _monitorList.FirstOrDefault(m => m.Name == name);
        if (monitor is not null || !create)
            return monitor;
        monitor = new WinlandMonitor();
        monitor.UpdateInitial(id, name, "");
        _monitorList.Add(monitor);
        return monitor;
    }

    /// <summary>
    /// A Workspace by its name and the Monitor it is on. A special workspace belongs to no Monitor and is found by
    /// its name alone; without a Monitor name, the one on the focused Monitor is preferred.
    /// </summary>
    private WinlandWorkspace? FindWorkspace(string name, string monitorName, bool create, int? id = null)
    {
        if (name.Length == 0)
            return null;
        var named = _workspaceList.Where(w => w.Name == name);
        var workspace = IsSpecial(name) ? named.FirstOrDefault()
            : monitorName.Length > 0 ? named.FirstOrDefault(w => w.Monitor?.Name == monitorName) ?? named.FirstOrDefault(w => w.Monitor is null)
            : named.OrderByDescending(w => w.Monitor == _focusedMonitor).FirstOrDefault();
        if (workspace is not null || !create)
            return workspace;
        workspace = new WinlandWorkspace(this);
        workspace.UpdateInitial(id ?? IdFromName(name), name);
        if (monitorName.Length > 0)
            workspace.SetMonitor(FindMonitor(monitorName, create: true));
        _workspaceList.Add(workspace);
        return workspace;
    }

    private WinlandWindow? FindWindow(ulong address, bool create)
    {
        if (address == 0)
            return null;
        var window = _windowList.FirstOrDefault(t => t.RawAddress == address);
        if (window is not null || !create)
            return window;
        window = new WinlandWindow(this);
        window.SetAddress(address);
        _windowList.Add(window);
        return window;
    }

    private static void MoveWindow(WinlandWindow window, WinlandWorkspace workspace)
    {
        if (window.Workspace is { } old && old != workspace)
            old.RemoveWindow(window);
        window.SetWorkspace(workspace);
        workspace.InsertWindow(window);
    }

    private void RemoveWindow(WinlandWindow window)
    {
        if (_activeWindow == window)
            SetActiveWindow(null);
        window.Workspace?.RemoveWindow(window);
        _windowList.Remove(window);
    }

    private void RemoveWorkspace(WinlandWorkspace workspace)
    {
        _workspaceList.Remove(workspace);
        foreach (var monitor in _monitorList.Where(m => m.ActiveWorkspace == workspace))
            monitor.SetActiveWorkspace(null);
        foreach (var window in _windowList.Where(t => t.Workspace == workspace))
            window.SetWorkspace(null);
    }

    private void RemoveMonitor(WinlandMonitor monitor)
    {
        _monitorList.Remove(monitor);
        if (_focusedMonitor == monitor)
            SetFocusedMonitor(null);
        foreach (var workspace in _workspaceList.Where(w => w.Monitor == monitor))
            workspace.SetMonitor(null);
    }

    /// <summary>
    /// Brings the collections and every derived property up to date after a change: the focused Workspace, which
    /// Workspaces are active and focused, which Client is activated and on which Monitor, and urgency.
    /// </summary>
    private void Recompute()
    {
        Follow(Monitors, _monitorList.OrderBy(m => m.Id));
        // Sorted by id, and by Monitor where Winland repeats a number.
        Follow(Workspaces, _workspaceList.OrderBy(w => w.Id).ThenBy(w => w.Monitor?.Id ?? int.MaxValue));
        Follow(Windows, _windowList);

        var focused = _focusedMonitor?.ActiveWorkspace;
        foreach (var monitor in _monitorList)
            monitor.SetFocused(monitor == _focusedMonitor);
        foreach (var workspace in _workspaceList)
            workspace.SetDerived(workspace.Monitor?.ActiveWorkspace == workspace, workspace == focused);
        foreach (var window in _windowList)
        {
            var activated = window == _activeWindow;
            window.SetDerived(activated, window.Workspace?.Monitor);
            if (activated)
                window.SetUrgent(false);
        }
        foreach (var workspace in _workspaceList)
        {
            var urgent = workspace.Windows.Any(t => t.Urgent);
            if (urgent && workspace.Focused)
            {
                foreach (var window in workspace.Windows)
                    window.SetUrgent(false);
                urgent = false;
            }
            workspace.SetUrgent(urgent);
        }
        Set(ref _focusedWorkspace, focused, nameof(FocusedWorkspace));
    }

    private static void Follow<T>(ObservableCollection<T> items, IEnumerable<T> next)
        where T : notnull => items.Sync(next, i => i, i => i, i => i, (_, _) => { });

    private void SetFocusedMonitor(WinlandMonitor? monitor) => Set(ref _focusedMonitor, monitor, nameof(FocusedMonitor));

    private void SetActiveWindow(WinlandWindow? window) => Set(ref _activeWindow, window, nameof(ActiveWindow));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Attach(null);
        _link.ConnectionChanged -= OnConnectionChanged;
        _link.EventReceived -= OnEventReceived;
        _link.Dispose();
    }
}
