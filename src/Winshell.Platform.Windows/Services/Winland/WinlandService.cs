using System.Globalization;
using System.Text.Json;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Shell;
using static Winshell.Winland.WinlandProtocol;

namespace Winshell.Platform.Windows.Services.Winland;

/// <summary>
/// The <c>Winland</c> singleton, backed by Winland. It reads Winland's state with <c>j/</c> queries and
/// keeps it current from the event stream, updating the same objects for each event. Winland
/// adds the Monitor to every event that names a Workspace, and it is used to tell same-numbered Workspaces apart.
/// While Winland is not running the lists are empty; they fill when it starts.
/// </summary>
[QmlSignal("rawEvent", "event")]
public sealed class WinlandService : QObject
{
    private readonly QmlEngine _engine;
    private readonly List<WinlandMonitor> _monitorList = [];
    private readonly List<WinlandWorkspace> _workspaceList = [];
    private readonly List<WinlandWindow> _toplevelList = [];
    private readonly WinlandEvent _event = new();
    private WinlandMonitor? _focusedMonitor;
    private WinlandWorkspace? _focusedWorkspace;
    private WinlandWindow? _activeToplevel;
    private bool _requestingMonitors, _requestingWorkspaces, _requestingToplevels, _monitorsRequested;

    // Bumped when the connection is lost, so replies to an older connection's requests are dropped.
    private int _generation;

    public WinlandService(QmlEngine engine)
    {
        _engine = engine;
        WinlandConnection.RegisterLog(_engine.Log);
        WinlandConnection.ConnectionChanged += OnConnectionChanged;
        WinlandConnection.EventReceived += OnEventReceived;
        WinlandConnection.EnsureStarted();
        if (WinlandConnection.IsConnected)
            RefreshAll();
    }

    [Qml] public ObjectModel Monitors { get; } = new();

    [Qml] public ObjectModel Workspaces { get; } = new();

    [Qml] public ObjectModel Toplevels { get; } = new();

    [Qml] public WinlandMonitor? FocusedMonitor { get { Track(); return _focusedMonitor; } }

    [Qml] public WinlandWorkspace? FocusedWorkspace { get { Track(); return _focusedWorkspace; } }

    [Qml] public WinlandWindow? ActiveToplevel { get { Track(); return _activeToplevel; } }

    /// <summary>Winland has no Lua mode.</summary>
    [Qml] public bool UsingLua => false;

    /// <summary>Winland's pipe. It takes requests and, after an <c>events</c> request, streams events.</summary>
    [Qml] public string RequestSocketPath { get { Track(); return WinlandConnection.PipePath; } }

    [Qml] public string EventSocketPath { get { Track(); return WinlandConnection.PipePath; } }

    internal WinlandMonitor? RawFocusedMonitor => _focusedMonitor;

    internal WindowListService? WindowListService => _engine.FindNativeSingleton<WindowListService>();

    [Qml]
    public void Dispatch(string request) => Send("dispatch " + request);

    [Qml] public void RefreshMonitors() => RefreshMonitors(false);

    [Qml] public void RefreshWorkspaces() => RefreshWorkspaces(false);

    [Qml]
    public void RefreshToplevels()
    {
        if (_requestingToplevels)
            return;
        _requestingToplevels = true;
        Request("j/clients", reply =>
        {
            _requestingToplevels = false;
            if (ParseJson(reply) is not { ValueKind: JsonValueKind.Array } json)
            {
                RetryIfConnected(reply, RefreshToplevels);
                return;
            }
            foreach (var entry in json.EnumerateArray())
            {
                var address = ParseAddress(Str(entry, "address"));
                if (address == 0)
                    continue;
                var toplevel = FindToplevel(address, create: true)!;
                toplevel.SetTitle(Str(entry, "title"));
                var ws = Obj(entry, "workspace");
                if (FindWorkspace(Str(ws, "name"), Str(ws, "monitor"), create: true, Int(ws, "id")) is { } workspace)
                    MoveToplevel(toplevel, workspace);
                toplevel.SetLastIpcObject(ToQml(entry));
            }
        });
    }

    [Qml]
    public WinlandMonitor? MonitorFor(ShellScreen? screen)
    {
        if (screen is null)
            return null;
        // Winland names a Monitor as Windows does, \\.\DISPLAY1, which is the screen's id.
        var monitor = _monitorList.FirstOrDefault(m => m.RawName == screen.Id || m.RawName == screen.Name);
        return monitor ?? (_monitorsRequested ? null : FindMonitor(screen.Id, create: true));
    }

    /// <summary>Sends a request whose reply should be <c>ok</c>, and warns otherwise.</summary>
    internal void Send(string request) => Request(request, reply =>
    {
        if (reply is null)
            _engine.Log.Warn("winland", $"Failed to send \"{request}\": Winland is not running");
        else if (reply.Split("\n\n").Any(r => r.Trim() != "ok"))
            _engine.Log.Warn("winland", $"\"{request}\" failed with error {reply.Trim()}");
    });

    // Connection

    private void OnConnectionChanged(bool connected) => _engine.Loop.Post(() =>
    {
        if (IsDestroyed)
            return;
        if (connected)
            RefreshAll();
        else
            Reset();
        NotifyChanged("requestSocketPath");
        NotifyChanged("eventSocketPath");
    });

    private void OnEventReceived(string line) => _engine.Loop.Post(() =>
    {
        if (IsDestroyed)
            return;
        var (name, data) = SplitEvent(line);
        _event.Set(name, data);
        try
        {
            OnEvent(name, data);
        }
        finally
        {
            Recompute();
        }
        Emit("rawEvent", _event);
    });

    private void RefreshAll()
    {
        RefreshMonitors(true);
        RefreshWorkspaces(true);
        RefreshToplevels();
        // Events only name the active window when the focus moves; asking once shows it before then.
        Request("j/activewindow", reply =>
        {
            if (_activeToplevel is null && ParseJson(reply) is { ValueKind: JsonValueKind.Object } json)
                SetActiveToplevel(FindToplevel(ParseAddress(Str(json, "address")), create: true));
        });
    }

    /// <summary>Winland went away: everything it reported is gone.</summary>
    private void Reset()
    {
        _generation++;
        _requestingMonitors = _requestingWorkspaces = _requestingToplevels = false;
        _monitorsRequested = false;
        foreach (var toplevel in _toplevelList.ToArray())
            RemoveToplevel(toplevel);
        foreach (var workspace in _workspaceList.ToArray())
            RemoveWorkspace(workspace);
        foreach (var monitor in _monitorList.ToArray())
            RemoveMonitor(monitor);
        Recompute();
    }

    private void Request(string command, Action<string?> onReply)
    {
        var generation = _generation;
        WinlandConnection.RequestAsync(command).ContinueWith(t =>
        {
            var reply = t.IsCompletedSuccessfully ? t.Result : null;
            _engine.Loop.Post(() =>
            {
                if (IsDestroyed || generation != _generation)
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
        }, TaskScheduler.Default);
    }

    /// <summary>A query that got no answer while the stream is up is asked again, so a busy Winland still fills the lists.</summary>
    private void RetryIfConnected(string? reply, Action retry)
    {
        if (reply is not null || !WinlandConnection.IsConnected)
            return;
        var generation = _generation;
        _engine.Loop.Schedule(TimeSpan.FromSeconds(1), () =>
        {
            if (!IsDestroyed && generation == _generation)
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
                if (_monitorList.Any(m => m.ActiveWorkspaceRaw is null))
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
                    monitor.SetActiveWorkspace(FindWorkspace(args[1], monitor.RawName, create: true, ParseId(args[0], args[1])));
                break;
            }
            case "moveworkspacev2":
            {
                // id,name,monitor it moved to
                var args = ParseArgs(data, 3);
                var monitor = FindMonitor(args[2], create: true)!;
                var workspace = FindWorkspace(args[1], args[2], create: false)
                    ?? _workspaceList.FirstOrDefault(w => w.RawName == args[1] && w.RawMonitor != monitor)
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
                    _engine.Log.Debug("winland", $"openwindow for workspace {args[1]} which was not previously tracked");
                    return;
                }
                var toplevel = FindToplevel(address, create: true)!;
                toplevel.SetTitle(args[4]);
                MoveToplevel(toplevel, workspace);
                break;
            }
            case "closewindow":
                if (FindToplevel(ParseAddress(data), create: false) is { } closed)
                    RemoveToplevel(closed);
                break;
            case "movewindowv2":
            {
                // address,id,name,monitor
                var args = ParseArgs(data, 4);
                if (FindToplevel(ParseAddress(args[0]), create: false) is { } toplevel
                    && FindWorkspace(args[2], args[3], create: false) is { } workspace)
                    MoveToplevel(toplevel, workspace);
                break;
            }
            case "windowtitlev2":
            {
                var args = ParseArgs(data, 2);
                FindToplevel(ParseAddress(args[0]), create: true)?.SetTitle(args[1]);
                break;
            }
            case "activewindowv2":
                SetActiveToplevel(FindToplevel(ParseAddress(data), create: true));
                break;
            case "urgent":
                FindToplevel(ParseAddress(data), create: true)?.SetUrgent(true);
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
                if (activeName.Length > 0 && monitor.ActiveWorkspaceRaw?.RawName != activeName)
                    monitor.SetActiveWorkspace(FindWorkspace(activeName, name, create: true, Int(active, "id")));
                monitor.SetLastIpcObject(ToQml(entry));
                if (Bool(entry, "focused"))
                    SetFocusedMonitor(monitor);
            }
            foreach (var monitor in _monitorList.Where(m => !names.Contains(m.RawName)).ToArray())
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
                workspace.SetLastIpcObject(ToQml(entry));
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
        var monitor = _monitorList.FirstOrDefault(m => m.RawName == name);
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
        var named = _workspaceList.Where(w => w.RawName == name);
        var workspace = IsSpecial(name) ? named.FirstOrDefault()
            : monitorName.Length > 0 ? named.FirstOrDefault(w => w.RawMonitor?.RawName == monitorName) ?? named.FirstOrDefault(w => w.RawMonitor is null)
            : named.OrderByDescending(w => w.RawMonitor == _focusedMonitor).FirstOrDefault();
        if (workspace is not null || !create)
            return workspace;
        workspace = new WinlandWorkspace(this);
        workspace.UpdateInitial(id ?? IdFromName(name), name);
        if (monitorName.Length > 0)
            workspace.SetMonitor(FindMonitor(monitorName, create: true));
        _workspaceList.Add(workspace);
        return workspace;
    }

    private WinlandWindow? FindToplevel(ulong address, bool create)
    {
        if (address == 0)
            return null;
        var toplevel = _toplevelList.FirstOrDefault(t => t.RawAddress == address);
        if (toplevel is not null || !create)
            return toplevel;
        toplevel = new WinlandWindow(this);
        toplevel.SetAddress(address);
        _toplevelList.Add(toplevel);
        return toplevel;
    }

    private static void MoveToplevel(WinlandWindow toplevel, WinlandWorkspace workspace)
    {
        if (toplevel.RawWorkspace is { } old && old != workspace)
            old.RemoveToplevel(toplevel);
        toplevel.SetWorkspace(workspace);
        workspace.InsertToplevel(toplevel);
    }

    private void RemoveToplevel(WinlandWindow toplevel)
    {
        if (_activeToplevel == toplevel)
            SetActiveToplevel(null);
        toplevel.RawWorkspace?.RemoveToplevel(toplevel);
        _toplevelList.Remove(toplevel);
        Toplevels.Remove(toplevel);
        toplevel.Destroy();
    }

    private void RemoveWorkspace(WinlandWorkspace workspace)
    {
        _workspaceList.Remove(workspace);
        Workspaces.Remove(workspace);
        foreach (var monitor in _monitorList.Where(m => m.ActiveWorkspaceRaw == workspace))
            monitor.SetActiveWorkspace(null);
        foreach (var toplevel in _toplevelList.Where(t => t.RawWorkspace == workspace))
            toplevel.SetWorkspace(null);
        workspace.Destroy();
    }

    private void RemoveMonitor(WinlandMonitor monitor)
    {
        _monitorList.Remove(monitor);
        Monitors.Remove(monitor);
        if (_focusedMonitor == monitor)
            SetFocusedMonitor(null);
        foreach (var workspace in _workspaceList.Where(w => w.RawMonitor == monitor))
            workspace.SetMonitor(null);
        // Destroyed a little later, since events can still name it.
        _engine.Loop.Post(monitor.Destroy);
    }

    /// <summary>
    /// Brings the models and every derived property up to date after a change: the focused Workspace, which
    /// Workspaces are active and focused, which Client is activated and on which Monitor, and urgency.
    /// </summary>
    private void Recompute()
    {
        if (IsDestroyed)
            return;
        Monitors.Set(_monitorList.OrderBy(m => m.RawId).Cast<object?>().ToList());
        // Sorted by id, and by Monitor where Winland repeats a number.
        Workspaces.Set(_workspaceList.OrderBy(w => w.RawId).ThenBy(w => w.RawMonitor?.RawId ?? int.MaxValue).Cast<object?>().ToList());
        Toplevels.Set(_toplevelList.Cast<object?>().ToList());

        var focused = _focusedMonitor?.ActiveWorkspaceRaw;
        foreach (var monitor in _monitorList)
            monitor.SetFocused(monitor == _focusedMonitor);
        foreach (var workspace in _workspaceList)
            workspace.SetDerived(workspace.RawMonitor?.ActiveWorkspaceRaw == workspace, workspace == focused);
        foreach (var toplevel in _toplevelList)
        {
            var activated = toplevel == _activeToplevel;
            toplevel.SetDerived(activated, toplevel.RawWorkspace?.RawMonitor);
            if (activated)
                toplevel.SetUrgent(false);
        }
        foreach (var workspace in _workspaceList)
        {
            var urgent = workspace.ToplevelList.Any(t => t.RawUrgent);
            if (urgent && workspace.RawFocused)
            {
                foreach (var toplevel in workspace.ToplevelList)
                    toplevel.SetUrgent(false);
                urgent = false;
            }
            workspace.SetUrgent(urgent);
        }
        SetField(ref _focusedWorkspace, focused, nameof(FocusedWorkspace));
    }

    private void SetFocusedMonitor(WinlandMonitor? monitor) => SetField(ref _focusedMonitor, monitor, nameof(FocusedMonitor));

    private void SetActiveToplevel(WinlandWindow? toplevel) => SetField(ref _activeToplevel, toplevel, nameof(ActiveToplevel));

    protected override void OnDestroy()
    {
        WinlandConnection.UnregisterLog(_engine.Log);
        WinlandConnection.ConnectionChanged -= OnConnectionChanged;
        WinlandConnection.EventReceived -= OnEventReceived;
    }
}
