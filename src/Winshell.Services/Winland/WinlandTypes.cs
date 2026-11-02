using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Winshell.Winland;

/// <summary>One line of the event stream.</summary>
/// <param name="Name">The event's name, before <c>&gt;&gt;</c>, such as <c>workspacev2</c>.</param>
/// <param name="Data">The text after <c>&gt;&gt;</c>.</param>
public sealed record WinlandEvent(string Name, string Data)
{
    /// <summary>Splits <see cref="Data"/> at commas into that many strings. The last keeps any further commas, and missing ones are empty.</summary>
    public IReadOnlyList<string> Parse(int argumentCount) => WinlandProtocol.ParseArgs(Data, argumentCount);
}

/// <summary>A Monitor as Winland reports it.</summary>
public sealed class WinlandMonitor : ObservableObject
{
    private int _id = -1;
    private string _name = "";
    private string _description = "";
    private int _x, _y, _width, _height;
    private double _scale = 1;
    private bool _focused;
    private WinlandWorkspace? _activeWorkspace;
    private JsonElement? _lastIpcObject;

    internal WinlandMonitor()
    {
    }

    /// <summary>Winland's number for the Monitor. -1 until Winland reports it.</summary>
    public int Id => _id;

    /// <summary>The Windows display name, such as <c>\\.\DISPLAY1</c>, which is the screen's id.</summary>
    public string Name => _name;

    public string Description => _description;

    public int X => _x;

    public int Y => _y;

    public int Width => _width;

    public int Height => _height;

    public double Scale => _scale;

    /// <summary>Whether this is <see cref="WinlandService.FocusedMonitor"/>.</summary>
    public bool Focused => _focused;

    /// <summary>The Workspace shown on the Monitor, or null.</summary>
    public WinlandWorkspace? ActiveWorkspace => _activeWorkspace;

    /// <summary>The Monitor's entry from Winland's last monitor list. Null before the first one.</summary>
    public JsonElement? LastIpcObject => _lastIpcObject;

    internal void UpdateInitial(int id, string name, string description)
    {
        Set(ref _id, id, nameof(Id));
        Set(ref _name, name, nameof(Name));
        Set(ref _description, description, nameof(Description));
    }

    internal void UpdateGeometry(int x, int y, int width, int height, double scale)
    {
        Set(ref _x, x, nameof(X));
        Set(ref _y, y, nameof(Y));
        Set(ref _width, width, nameof(Width));
        Set(ref _height, height, nameof(Height));
        Set(ref _scale, scale, nameof(Scale));
    }

    internal void SetLastIpcObject(JsonElement value)
    {
        _lastIpcObject = value;
        Changed(nameof(LastIpcObject));
    }

    internal void SetActiveWorkspace(WinlandWorkspace? workspace)
    {
        workspace?.SetMonitor(this);
        Set(ref _activeWorkspace, workspace, nameof(ActiveWorkspace));
    }

    internal void SetFocused(bool focused) => Set(ref _focused, focused, nameof(Focused));
}

/// <summary>A Workspace. Winland numbers Workspaces per Monitor, so two can both be 3; <see cref="Monitor"/> tells them apart.</summary>
public sealed class WinlandWorkspace : ObservableObject
{
    private readonly WinlandService _ipc;
    private int _id = -1;
    private string _name = "";
    private string _address = "";
    private WinlandMonitor? _monitor;
    private bool _active, _focused, _urgent, _hasFullscreen;
    private JsonElement? _lastIpcObject;

    internal WinlandWorkspace(WinlandService ipc) => _ipc = ipc;

    /// <summary>The Workspace number. A named Workspace gets a negative id, and <c>special:special</c> gets -99.</summary>
    public int Id => _id;

    /// <summary>The Workspace's name: its number, a name, or <c>special:</c> and a name.</summary>
    public string Name => _name;

    /// <summary>The same as <see cref="Name"/>.</summary>
    public string Address => _address;

    public WinlandMonitor? Monitor => _monitor;

    /// <summary>Whether the Workspace is shown on its Monitor.</summary>
    public bool Active => _active;

    /// <summary>Whether this is <see cref="WinlandService.FocusedWorkspace"/>.</summary>
    public bool Focused => _focused;

    /// <summary>Whether a window on it is urgent while the Workspace is not focused.</summary>
    public bool Urgent => _urgent;

    public bool HasFullscreen => _hasFullscreen;

    public ObservableCollection<WinlandWindow> Toplevels { get; } = [];

    /// <summary>The Workspace's entry from Winland's last workspace list. Null before the first one.</summary>
    public JsonElement? LastIpcObject => _lastIpcObject;

    internal bool IsSpecial => WinlandProtocol.IsSpecial(_name);

    /// <summary>
    /// Shows the Workspace. A Workspace number means that number on the focused Monitor in Winland, so the
    /// Workspace's own Monitor is focused first when it is another one.
    /// </summary>
    public void Activate()
    {
        if (!IsSpecial && _monitor is { Id: >= 0 } monitor && monitor != _ipc.FocusedMonitor)
            _ipc.Send($"[[BATCH]]dispatch focusmonitor {monitor.Id.ToString(CultureInfo.InvariantCulture)};dispatch workspace {_name}");
        else
            _ipc.Dispatch("workspace " + _name);
    }

    internal void UpdateInitial(int id, string name)
    {
        Set(ref _id, id, nameof(Id));
        Set(ref _name, name, nameof(Name));
        Set(ref _address, name, nameof(Address));
    }

    internal void SetId(int id) => Set(ref _id, id, nameof(Id));

    internal void SetMonitor(WinlandMonitor? monitor) => Set(ref _monitor, monitor, nameof(Monitor));

    internal void SetHasFullscreen(bool value) => Set(ref _hasFullscreen, value, nameof(HasFullscreen));

    internal void SetLastIpcObject(JsonElement value)
    {
        _lastIpcObject = value;
        Changed(nameof(LastIpcObject));
    }

    internal void SetDerived(bool active, bool focused)
    {
        Set(ref _active, active, nameof(Active));
        Set(ref _focused, focused, nameof(Focused));
    }

    internal void SetUrgent(bool urgent) => Set(ref _urgent, urgent, nameof(Urgent));

    internal void InsertToplevel(WinlandWindow toplevel)
    {
        if (!Toplevels.Contains(toplevel))
            Toplevels.Add(toplevel);
    }

    internal void RemoveToplevel(WinlandWindow toplevel) => Toplevels.Remove(toplevel);
}

/// <summary>A window as Winland reports it. Its address is the window handle.</summary>
public sealed class WinlandWindow : ObservableObject
{
    private ulong _address;
    private string _title = "";
    private WinlandWorkspace? _workspace;
    private WinlandMonitor? _monitor;
    private bool _activated, _urgent;
    private JsonElement? _lastIpcObject;

    internal WinlandWindow()
    {
    }

    /// <summary>The window handle in hexadecimal, without <c>0x</c>.</summary>
    public string Address => _address == 0 ? "" : _address.ToString("x", CultureInfo.InvariantCulture);

    /// <summary>The window handle, which names the same window in <c>Winshell.Windows</c>.</summary>
    public nint Handle => (nint)_address;

    public string Title => _title;

    public WinlandWorkspace? Workspace => _workspace;

    /// <summary>The Monitor of the window's Workspace, or null.</summary>
    public WinlandMonitor? Monitor => _monitor;

    /// <summary>Whether this is <see cref="WinlandService.ActiveToplevel"/>.</summary>
    public bool Activated => _activated;

    /// <summary>Whether Winland marked the window urgent. It clears when the window is activated or its Workspace is focused.</summary>
    public bool Urgent => _urgent;

    /// <summary>The window's entry from Winland's last window list. Null before the first one.</summary>
    public JsonElement? LastIpcObject => _lastIpcObject;

    internal ulong RawAddress => _address;

    internal void SetAddress(ulong address)
    {
        if (_address == address)
            return;
        _address = address;
        Changed(nameof(Address));
        Changed(nameof(Handle));
    }

    internal void SetTitle(string title) => Set(ref _title, title, nameof(Title));

    internal void SetWorkspace(WinlandWorkspace? workspace) => Set(ref _workspace, workspace, nameof(Workspace));

    internal void SetUrgent(bool urgent) => Set(ref _urgent, urgent, nameof(Urgent));

    internal void SetLastIpcObject(JsonElement value)
    {
        _lastIpcObject = value;
        Changed(nameof(LastIpcObject));
    }

    internal void SetDerived(bool activated, WinlandMonitor? monitor)
    {
        Set(ref _activated, activated, nameof(Activated));
        Set(ref _monitor, monitor, nameof(Monitor));
    }
}
