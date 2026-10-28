using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Platform.Windows.Accessibility;

/// <summary>
/// Exposes a window's items to UI Automation. The window answers WM_GETOBJECT with the root of its tree. Once a
/// client has asked, the tree is brought up to date after every frame while any client listens, raising what changed
/// as UI Automation events; when none listens, it is brought up to date when the next call comes in.
/// </summary>
internal sealed class WindowAutomation : IDisposable
{
    /// <summary>The message that runs a UI Automation call on the window's thread.</summary>
    public static readonly uint RunMessage = Win32.WM_APP + 2;

    private readonly Win32Window _window;
    private readonly Scene _scene;
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<AccessibleNode, UiaElement> _elements = [];
    private AccessibleTree? _tree;
    private bool _stale;
    private bool _focusPending;

    public WindowAutomation(Win32Window window, Scene scene, IEventLoop loop, Log log)
    {
        _window = window;
        _scene = scene;
        _loop = loop;
        _log = log;
        scene.FocusChanged += OnFocusChanged;
    }

    public nint Handle => _window.Handle;

    /// <summary>Whether the window has the keyboard, so focus inside it is focus on the desktop.</summary>
    public bool HasFocus => Handle != 0 && Win32.GetFocus() == Handle;

    public UiaRoot Root => (UiaRoot)For(Tree.Root);

    private AccessibleTree Tree
    {
        get
        {
            if (_tree is null)
            {
                _tree = new AccessibleTree(_scene);
                _tree.Refresh();
            }
            return _tree;
        }
    }

    public UiaElement For(AccessibleNode node)
    {
        if (!_elements.TryGetValue(node, out var element))
            _elements[node] = element = node.IsRoot ? new UiaRoot(this, node) : new UiaElement(this, node);
        return element;
    }

    public UiaElement? FocusedElement => HasFocus && Tree.Find(_scene.FocusItem) is { } node ? For(node) : null;

    /// <summary>Answers WM_GETOBJECT for UI Automation's root object.</summary>
    public nint? GetObject(nint wParam, nint lParam)
    {
        if ((int)lParam != Uia.RootObjectId)
            return null;
        Sync();
        return Uia.UiaReturnRawElementProvider(Handle, wParam, lParam, Root);
    }

    /// <summary>Runs a call from UI Automation on the window's thread and returns its HRESULT.</summary>
    public int Run(Func<int> call)
    {
        if (Environment.CurrentManagedThreadId == _thread)
        {
            if (_stale)
                Sync();
            return Guard(call);
        }
        var handle = Handle;
        if (handle == 0)
            return Uia.ElementNotAvailable;
        var work = new Work(call);
        var gc = GCHandle.Alloc(work);
        try
        {
            Win32.SendMessageW(handle, RunMessage, 0, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }
        return work.Ran ? work.Result : Uia.ElementNotAvailable;
    }

    /// <summary>Runs the call a RunMessage carries.</summary>
    public void RunPosted(nint lParam)
    {
        if (GCHandle.FromIntPtr(lParam).Target is Work work)
        {
            if (_stale)
                Sync();
            work.Result = Guard(work.Call);
            work.Ran = true;
        }
    }

    private int Guard(Func<int> call)
    {
        try
        {
            return call();
        }
        catch (Exception e)
        {
            _log.Error("uia", e.ToString());
            return e is COMException com ? com.HResult : Uia.E_FAIL;
        }
    }

    private sealed class Work(Func<int> call)
    {
        public Func<int> Call => call;

        public int Result;

        public bool Ran;
    }

    /// <summary>Runs an action on a node after the current call, unless the node has left the tree by then.</summary>
    public void Post(AccessibleNode node, Action action) => _loop.Post(() =>
    {
        if (node.IsAlive)
            Guard(() => { action(); return 0; });
    });


    // Coordinates: UI Automation works in physical pixels on the virtual desktop.

    public UiaRect ToScreen(QRect r)
    {
        var origin = new POINT();
        Win32.ClientToScreen(Handle, ref origin);
        var scale = _window.Scale;
        return new UiaRect { Left = origin.X + r.X * scale, Top = origin.Y + r.Y * scale, Width = r.Width * scale, Height = r.Height * scale };
    }

    private (double X, double Y) ToScene(double x, double y)
    {
        var origin = new POINT();
        Win32.ClientToScreen(Handle, ref origin);
        return ((x - origin.X) / _window.Scale, (y - origin.Y) / _window.Scale);
    }

    public QPoint ToItem(Item item, double screenX, double screenY)
    {
        var (x, y) = ToScene(screenX, screenY);
        return item.MapFromScene(x, y);
    }

    public UiaElement ElementAt(double screenX, double screenY)
    {
        var (x, y) = ToScene(screenX, screenY);
        return For(Tree.NodeAt(x, y));
    }

    // Events

    /// <summary>Brings the tree up to date after a frame, and raises what changed.</summary>
    public void AfterFrame()
    {
        if (_tree is null)
            return;
        if (Uia.UiaClientsAreListening())
            Sync();
        else
            _stale = true;
    }

    private void OnFocusChanged(Item? item)
    {
        if (_tree is null || _focusPending)
            return;
        // Let the key or click that moved focus finish, so a popup it opened is in the tree.
        _focusPending = true;
        _loop.Post(() =>
        {
            _focusPending = false;
            if (_tree is null || Handle == 0)
                return;
            Sync();
            if (Uia.UiaClientsAreListening() && FocusedElement is { } focused)
                Uia.UiaRaiseAutomationEvent(focused, Uia.FocusChangedEvent);
        });
    }

    private void Sync()
    {
        _stale = false;
        var changes = Tree.Refresh();
        var listening = changes.Count > 0 && Uia.UiaClientsAreListening();
        foreach (var change in changes)
        {
            if (change.Kind == AccessibleChangeKind.Removed)
            {
                if (_elements.Remove(change.Node, out var gone) && listening)
                    Uia.UiaDisconnectProvider(gone);
                continue;
            }
            if (listening)
                Raise(change);
        }
    }

    private void Raise(AccessibleChange change)
    {
        if (change.Kind == AccessibleChangeKind.Added)
        {
            if (change.Node.Role == Role.PopupMenu)
                Uia.UiaRaiseAutomationEvent(For(change.Node), Uia.MenuOpenedEvent);
            return;
        }
        var element = For(change.Node);
        if (change.Kind == AccessibleChangeKind.Children)
        {
            int[] id = change.Node.IsRoot ? [42, (int)Handle] : element.RuntimeId;
            Uia.UiaRaiseStructureChangedEvent(element, Uia.ChildrenInvalidated, id, id.Length);
            return;
        }
        var (property, old, now) = change.Property switch
        {
            AccessibleProperty.Name => (Uia.NameProperty, change.Old, change.New),
            AccessibleProperty.Description => (Uia.HelpTextProperty, change.Old, change.New),
            AccessibleProperty.IsEnabled => (Uia.IsEnabledProperty, change.Old, change.New),
            AccessibleProperty.ToggleState => (Uia.ToggleStateProperty, (object?)(int?)(ToggleState?)change.Old, (object?)(int?)(ToggleState?)change.New),
            AccessibleProperty.Value => (Uia.ValueValueProperty, change.Old, change.New),
            AccessibleProperty.RangeValue => (Uia.RangeValueValueProperty, change.Old, change.New),
            AccessibleProperty.IsSelected => (Uia.IsSelectedProperty, change.Old, change.New),
            AccessibleProperty.IsExpanded => (Uia.ExpandCollapseStateProperty, (object?)(change.Old is true ? 1 : 0), (object?)(change.New is true ? 1 : 0)),
            AccessibleProperty.HorizontalScrollPercent => (Uia.HorizontalScrollPercentProperty, change.Old, change.New),
            AccessibleProperty.VerticalScrollPercent => (Uia.VerticalScrollPercentProperty, change.Old, change.New),
            _ => (0, null, null),
        };
        if (property != 0)
            Uia.UiaRaiseAutomationPropertyChangedEvent(element, property, old, now);
        if (change.Property == AccessibleProperty.IsSelected && change.New is true)
            Uia.UiaRaiseAutomationEvent(element, Uia.ElementSelectedEvent);
        if (change.Node.TextInput is not null && change.Property == AccessibleProperty.Value)
            Uia.UiaRaiseAutomationEvent(element, Uia.TextChangedEvent);
        if (change.Property == AccessibleProperty.TextSelection && change.Node.HasKeyboardFocus)
            Uia.UiaRaiseAutomationEvent(element, Uia.TextSelectionChangedEvent);
    }

    public void Raise(AccessibleNode node, int eventId)
    {
        if (node.IsAlive && Uia.UiaClientsAreListening())
            Uia.UiaRaiseAutomationEvent(For(node), eventId);
    }

    public void Dispose()
    {
        _scene.FocusChanged -= OnFocusChanged;
        if (_tree is null)
            return;
        // Tell clients the window's elements are gone, so they let go of them.
        if (Handle != 0)
            Uia.UiaReturnRawElementProvider(Handle, 0, 0, null);
        foreach (var element in _elements.Values)
            Uia.UiaDisconnectProvider(element);
        _elements.Clear();
        _tree = null;
    }
}
