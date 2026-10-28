using System.Globalization;
using Winshell.Platform.Windows.Services.Toplevels;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.Hyprland;

/// <summary>Quickshell's <c>HyprlandEvent</c>: one line of the event stream. The object is reused for every event.</summary>
public sealed class HyprlandEvent : QObject
{
    private string _name = "";
    private string _data = "";

    [Qml] public string Name { get { Track(); return _name; } }

    [Qml] public string Data { get { Track(); return _data; } }

    [Qml]
    public IReadOnlyList<object?> Parse(int argumentCount) => WinlandProtocol.ParseArgs(_data, argumentCount);

    internal void Set(string name, string data)
    {
        SetField(ref _name, name, nameof(Name));
        SetField(ref _data, data, nameof(Data));
    }
}

/// <summary>Quickshell's <c>HyprlandMonitor</c>: a Monitor as Winland reports it.</summary>
public sealed class HyprlandMonitor : QObject
{
    private int _id = -1;
    private string _name = "";
    private string _description = "";
    private int _x, _y, _width, _height;
    private double _scale = 1;
    private bool _focused;
    private HyprlandWorkspace? _activeWorkspace;
    private object? _lastIpcObject;

    [Qml] public int Id { get { Track(); return _id; } }

    [Qml] public string Name { get { Track(); return _name; } }

    [Qml] public string Description { get { Track(); return _description; } }

    [Qml] public int X { get { Track(); return _x; } }

    [Qml] public int Y { get { Track(); return _y; } }

    [Qml] public int Width { get { Track(); return _width; } }

    [Qml] public int Height { get { Track(); return _height; } }

    [Qml] public double Scale { get { Track(); return _scale; } }

    [Qml] public bool Focused { get { Track(); return _focused; } }

    [Qml] public HyprlandWorkspace? ActiveWorkspace { get { Track(); return _activeWorkspace; } }

    [Qml] public object? LastIpcObject { get { Track(); return _lastIpcObject; } }

    internal HyprlandWorkspace? ActiveWorkspaceRaw => _activeWorkspace;

    internal string RawName => _name;

    internal int RawId => _id;

    internal void UpdateInitial(int id, string name, string description)
    {
        SetField(ref _id, id, nameof(Id));
        SetField(ref _name, name, nameof(Name));
        SetField(ref _description, description, nameof(Description));
    }

    internal void UpdateGeometry(int x, int y, int width, int height, double scale)
    {
        SetField(ref _x, x, nameof(X));
        SetField(ref _y, y, nameof(Y));
        SetField(ref _width, width, nameof(Width));
        SetField(ref _height, height, nameof(Height));
        SetField(ref _scale, scale, nameof(Scale));
    }

    internal void SetLastIpcObject(object? value)
    {
        _lastIpcObject = value;
        NotifyChanged("lastIpcObject");
    }

    internal void SetActiveWorkspace(HyprlandWorkspace? workspace)
    {
        workspace?.SetMonitor(this);
        SetField(ref _activeWorkspace, workspace, nameof(ActiveWorkspace));
    }

    internal void SetFocused(bool focused) => SetField(ref _focused, focused, nameof(Focused));
}

/// <summary>
/// Quickshell's <c>HyprlandWorkspace</c>. Winland numbers Workspaces per Monitor, so two Workspaces can both be 3;
/// one is told from the other by its Monitor.
/// </summary>
[QmlSignal("typeChanged")]
public sealed class HyprlandWorkspace : QObject
{
    private readonly HyprlandIpc _ipc;
    private int _id = -1;
    private string _name = "";
    private string _address = "";
    private HyprlandMonitor? _monitor;
    private bool _active, _focused, _urgent, _hasFullscreen;
    private object? _lastIpcObject;

    internal HyprlandWorkspace(HyprlandIpc ipc)
    {
        _ipc = ipc;
        Toplevels = new ObjectModel();
    }

    [Qml] public int Id { get { Track(); return _id; } }

    [Qml] public string Name { get { Track(); return _name; } }

    [Qml] public string Address { get { Track(); return _address; } }

    [Qml] public HyprlandMonitor? Monitor { get { Track(); return _monitor; } }

    [Qml] public bool Active { get { Track(); return _active; } }

    [Qml] public bool Focused { get { Track(); return _focused; } }

    [Qml] public bool Urgent { get { Track(); return _urgent; } }

    [Qml] public bool HasFullscreen { get { Track(); return _hasFullscreen; } }

    [Qml] public ObjectModel Toplevels { get; }

    [Qml] public object? LastIpcObject { get { Track(); return _lastIpcObject; } }

    internal string RawName => _name;

    internal int RawId => _id;

    internal HyprlandMonitor? RawMonitor => _monitor;

    internal bool RawFocused => _focused;

    internal bool IsSpecial => WinlandProtocol.IsSpecial(_name);

    /// <summary>
    /// Shows the Workspace. A Workspace number means that number on the focused Monitor in Winland, so the
    /// Workspace's own Monitor is focused first when it is another one.
    /// </summary>
    [Qml]
    public void Activate()
    {
        if (!IsSpecial && _monitor is { RawId: >= 0 } monitor && monitor != _ipc.RawFocusedMonitor)
            _ipc.Send($"[[BATCH]]dispatch focusmonitor {monitor.RawId.ToString(CultureInfo.InvariantCulture)};dispatch workspace {_name}");
        else
            _ipc.Dispatch("workspace " + _name);
    }

    internal void UpdateInitial(int id, string name)
    {
        SetField(ref _id, id, nameof(Id));
        SetField(ref _name, name, nameof(Name));
        SetField(ref _address, name, nameof(Address));
    }

    internal void SetId(int id) => SetField(ref _id, id, nameof(Id));

    internal void SetMonitor(HyprlandMonitor? monitor) => SetField(ref _monitor, monitor, nameof(Monitor));

    internal void SetHasFullscreen(bool value) => SetField(ref _hasFullscreen, value, nameof(HasFullscreen));

    internal void SetLastIpcObject(object? value)
    {
        _lastIpcObject = value;
        NotifyChanged("lastIpcObject");
    }

    internal void SetDerived(bool active, bool focused)
    {
        SetField(ref _active, active, nameof(Active));
        SetField(ref _focused, focused, nameof(Focused));
    }

    internal void SetUrgent(bool urgent) => SetField(ref _urgent, urgent, nameof(Urgent));

    internal void InsertToplevel(HyprlandToplevel toplevel)
    {
        if (Toplevels.IndexOf(toplevel) < 0)
            Toplevels.Add(toplevel);
    }

    internal void RemoveToplevel(HyprlandToplevel toplevel) => Toplevels.Remove(toplevel);

    internal IEnumerable<HyprlandToplevel> ToplevelList => Toplevels.Rows.OfType<HyprlandToplevel>();
}

/// <summary>
/// Quickshell's <c>HyprlandToplevel</c>: a Client as Winland reports it. Its address is the window handle, so
/// <c>wayland</c> is the <c>Toplevel</c> of the same window. As an attached object of a <c>Toplevel</c> it finds the
/// Client of that window.
/// </summary>
public sealed class HyprlandToplevel : QObject
{
    private readonly HyprlandIpc? _ipc;
    private readonly Toplevel? _attachedTo;
    private ulong _address;
    private string _title = "";
    private HyprlandWorkspace? _workspace;
    private HyprlandMonitor? _monitor;
    private bool _activated, _urgent;
    private object? _lastIpcObject;

    internal HyprlandToplevel(HyprlandIpc? ipc)
    {
        _ipc = ipc;
    }

    private HyprlandToplevel(Toplevel toplevel, HyprlandIpc? ipc)
    {
        _attachedTo = toplevel;
        _ipc = ipc;
        _address = (ulong)toplevel.Hwnd;
    }

    /// <summary>The attached object of a <c>Toplevel</c>.</summary>
    internal static QObject Attach(QObject owner) =>
        owner is Toplevel toplevel
            ? new HyprlandToplevel(toplevel, toplevel.Manager.Engine?.FindNativeSingleton<HyprlandIpc>())
            : new HyprlandToplevel((HyprlandIpc?)null);

    [Qml]
    public string Address
    {
        get
        {
            Track();
            return _address == 0 ? "" : _address.ToString("x", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>For an attached object, the Client of its window; null until Winland reports it.</summary>
    [Qml]
    public HyprlandToplevel? Handle => _attachedTo is not null ? _ipc?.FindToplevel(_address) : null;

    [Qml]
    public Toplevel? Wayland
    {
        get
        {
            if (_attachedTo is not null)
                return _attachedTo;
            Track("address");
            return _ipc?.ToplevelManager?.Find((nint)_address);
        }
    }

    [Qml] public string Title { get { Track(); return _title; } }

    [Qml] public HyprlandWorkspace? Workspace { get { Track(); return _workspace; } }

    [Qml] public HyprlandMonitor? Monitor { get { Track(); return _monitor; } }

    [Qml] public bool Activated { get { Track(); return _activated; } }

    [Qml] public bool Urgent { get { Track(); return _urgent; } }

    [Qml] public object? LastIpcObject { get { Track(); return _lastIpcObject; } }

    internal ulong RawAddress => _address;

    internal HyprlandWorkspace? RawWorkspace => _workspace;

    internal bool RawUrgent => _urgent;

    internal void SetAddress(ulong address)
    {
        if (_address == address)
            return;
        _address = address;
        NotifyChanged("address");
        NotifyChanged("wayland");
    }

    internal void SetTitle(string title) => SetField(ref _title, title, nameof(Title));

    internal void SetWorkspace(HyprlandWorkspace? workspace) => SetField(ref _workspace, workspace, nameof(Workspace));

    internal void SetUrgent(bool urgent) => SetField(ref _urgent, urgent, nameof(Urgent));

    internal void SetLastIpcObject(object? value)
    {
        _lastIpcObject = value;
        NotifyChanged("lastIpcObject");
    }

    internal void SetDerived(bool activated, HyprlandMonitor? monitor)
    {
        SetField(ref _activated, activated, nameof(Activated));
        SetField(ref _monitor, monitor, nameof(Monitor));
    }
}
