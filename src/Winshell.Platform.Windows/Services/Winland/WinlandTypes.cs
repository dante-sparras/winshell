using System.Globalization;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Winland;

/// <summary>The <c>WinlandEvent</c>: one line of the event stream. The object is reused for every event.</summary>
public sealed class WinlandEvent : QObject
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

/// <summary>The <c>WinlandMonitor</c>: a Monitor as Winland reports it.</summary>
public sealed class WinlandMonitor : QObject
{
    private int _id = -1;
    private string _name = "";
    private string _description = "";
    private int _x, _y, _width, _height;
    private double _scale = 1;
    private bool _focused;
    private WinlandWorkspace? _activeWorkspace;
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

    [Qml] public WinlandWorkspace? ActiveWorkspace { get { Track(); return _activeWorkspace; } }

    [Qml] public object? LastIpcObject { get { Track(); return _lastIpcObject; } }

    internal WinlandWorkspace? ActiveWorkspaceRaw => _activeWorkspace;

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

    internal void SetActiveWorkspace(WinlandWorkspace? workspace)
    {
        workspace?.SetMonitor(this);
        SetField(ref _activeWorkspace, workspace, nameof(ActiveWorkspace));
    }

    internal void SetFocused(bool focused) => SetField(ref _focused, focused, nameof(Focused));
}

/// <summary>
/// The <c>WinlandWorkspace</c>. Winland numbers Workspaces per Monitor, so two Workspaces can both be 3;
/// one is told from the other by its Monitor.
/// </summary>
[QmlSignal("typeChanged")]
public sealed class WinlandWorkspace : QObject
{
    private readonly WinlandService _ipc;
    private int _id = -1;
    private string _name = "";
    private string _address = "";
    private WinlandMonitor? _monitor;
    private bool _active, _focused, _urgent, _hasFullscreen;
    private object? _lastIpcObject;

    internal WinlandWorkspace(WinlandService ipc)
    {
        _ipc = ipc;
        Toplevels = new ObjectModel();
    }

    [Qml] public int Id { get { Track(); return _id; } }

    [Qml] public string Name { get { Track(); return _name; } }

    [Qml] public string Address { get { Track(); return _address; } }

    [Qml] public WinlandMonitor? Monitor { get { Track(); return _monitor; } }

    [Qml] public bool Active { get { Track(); return _active; } }

    [Qml] public bool Focused { get { Track(); return _focused; } }

    [Qml] public bool Urgent { get { Track(); return _urgent; } }

    [Qml] public bool HasFullscreen { get { Track(); return _hasFullscreen; } }

    [Qml] public ObjectModel Toplevels { get; }

    [Qml] public object? LastIpcObject { get { Track(); return _lastIpcObject; } }

    internal string RawName => _name;

    internal int RawId => _id;

    internal WinlandMonitor? RawMonitor => _monitor;

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

    internal void SetMonitor(WinlandMonitor? monitor) => SetField(ref _monitor, monitor, nameof(Monitor));

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

    internal void InsertToplevel(WinlandWindow toplevel)
    {
        if (Toplevels.IndexOf(toplevel) < 0)
            Toplevels.Add(toplevel);
    }

    internal void RemoveToplevel(WinlandWindow toplevel) => Toplevels.Remove(toplevel);

    internal IEnumerable<WinlandWindow> ToplevelList => Toplevels.Rows.OfType<WinlandWindow>();
}

/// <summary>
/// The <c>WinlandWindow</c>: a window as Winland reports it. Its address is the window handle, so
/// <c>wayland</c> is the <c>AppWindow</c> of the same window when <c>WindowList</c> is in use.
/// </summary>
public sealed class WinlandWindow : QObject
{
    private readonly WinlandService? _ipc;
    private ulong _address;
    private string _title = "";
    private WinlandWorkspace? _workspace;
    private WinlandMonitor? _monitor;
    private bool _activated, _urgent;
    private object? _lastIpcObject;

    internal WinlandWindow(WinlandService? ipc)
    {
        _ipc = ipc;
    }

    [Qml]
    public string Address
    {
        get
        {
            Track();
            return _address == 0 ? "" : _address.ToString("x", CultureInfo.InvariantCulture);
        }
    }

    [Qml]
    public AppWindow? Wayland
    {
        get
        {
            Track("address");
            return _ipc?.WindowListService?.Find((nint)_address);
        }
    }

    [Qml] public string Title { get { Track(); return _title; } }

    [Qml] public WinlandWorkspace? Workspace { get { Track(); return _workspace; } }

    [Qml] public WinlandMonitor? Monitor { get { Track(); return _monitor; } }

    [Qml] public bool Activated { get { Track(); return _activated; } }

    [Qml] public bool Urgent { get { Track(); return _urgent; } }

    [Qml] public object? LastIpcObject { get { Track(); return _lastIpcObject; } }

    internal ulong RawAddress => _address;

    internal WinlandWorkspace? RawWorkspace => _workspace;

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

    internal void SetWorkspace(WinlandWorkspace? workspace) => SetField(ref _workspace, workspace, nameof(Workspace));

    internal void SetUrgent(bool urgent) => SetField(ref _urgent, urgent, nameof(Urgent));

    internal void SetLastIpcObject(object? value)
    {
        _lastIpcObject = value;
        NotifyChanged("lastIpcObject");
    }

    internal void SetDerived(bool activated, WinlandMonitor? monitor)
    {
        SetField(ref _activated, activated, nameof(Activated));
        SetField(ref _monitor, monitor, nameof(Monitor));
    }
}
