using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>One edge or center line of an item, which an anchor attaches to.</summary>
public enum AnchorEdge
{
    Left,
    Right,
    Top,
    Bottom,
    HorizontalCenter,
    VerticalCenter,
    Baseline,
}

/// <summary>The value of <c>item.left</c>: a line an anchor can attach to.</summary>
public sealed record AnchorLine(Item Item, AnchorEdge Edge);

/// <summary>Where an item draws: the window that shows its tree.</summary>
public interface IItemHost
{
    /// <summary>Schedules a repaint of the window.</summary>
    void RequestUpdate();

    double DevicePixelRatio { get; }

    QObject? WindowObject { get; }

    /// <summary>Gives keyboard focus to an item in this window.</summary>
    void SetFocusItem(Item? item);

    Item? FocusItem { get; }
}

/// <summary>QtQuick's <c>Item</c>: the base of everything visible.</summary>
[QmlDefaultProperty("data")]
[QmlSignal("childrenChanged")]
public class Item : QObject
{
    private double _x, _y, _z;
    private double _width, _height;
    private double _implicitWidth, _implicitHeight;
    private bool _widthValid, _heightValid;
    private double _declaredWidth, _declaredHeight;
    private bool _visible = true;
    private bool _notification;
    private double _opacity = 1;
    private bool _enabled = true;
    private bool _clip;
    private double _scale = 1;
    private double _rotation;
    private int _transformOrigin = (int)TransformOriginValue.Center;
    private bool _focus;
    private bool _activeFocus;
    private bool _antialiasing = true;
    private bool _smooth = true;
    private string _state = "";
    private Item? _parent;
    private Anchors? _anchors;
    private IItemHost? _host;
    private readonly QmlList<Item> _children;
    private readonly QmlList<QObject> _resources;
    private readonly ItemData _data;

    public Item()
    {
        _children = new QmlList<Item>(this, "children", added: OnChildListAdd, removed: OnChildListRemove);
        _resources = new QmlList<QObject>(this, "resources", added: r => r.SetOwner(this));
        _data = new ItemData(this);
    }

    [QmlEnum]
    public enum TransformOriginValue
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Center,
        Right,
        BottomLeft,
        Bottom,
        BottomRight,
    }

    // Geometry

    [Qml]
    public double X
    {
        get { Track(); return _x; }
        set => SetGeometry(ref _x, value, "x");
    }

    [Qml]
    public double Y
    {
        get { Track(); return _y; }
        set => SetGeometry(ref _y, value, "y");
    }

    [Qml]
    public double Z
    {
        get { Track(); return _z; }
        set
        {
            if (SetField(ref _z, value))
                Update();
        }
    }

    [Qml]
    public double Width
    {
        get { Track(); return _width; }
        set
        {
            _declaredWidth = Math.Max(0, double.IsNaN(value) ? 0 : value);
            _widthValid = true;
            SetGeometry(ref _width, _declaredWidth, "width");
        }
    }

    [Qml]
    public double Height
    {
        get { Track(); return _height; }
        set
        {
            _declaredHeight = Math.Max(0, double.IsNaN(value) ? 0 : value);
            _heightValid = true;
            SetGeometry(ref _height, _declaredHeight, "height");
        }
    }

    [Qml]
    public double ImplicitWidth
    {
        get { Track(); return _implicitWidth; }
        set
        {
            if (!SetField(ref _implicitWidth, Math.Max(0, value)))
                return;
            if (!_widthValid)
                SetGeometry(ref _width, _implicitWidth, "width");
            OnImplicitSizeChanged();
        }
    }

    [Qml]
    public double ImplicitHeight
    {
        get { Track(); return _implicitHeight; }
        set
        {
            if (!SetField(ref _implicitHeight, Math.Max(0, value)))
                return;
            if (!_heightValid)
                SetGeometry(ref _height, _implicitHeight, "height");
            OnImplicitSizeChanged();
        }
    }

    /// <summary>Whether width was set explicitly, rather than following implicitWidth.</summary>
    public bool HasExplicitWidth => _widthValid;

    public bool HasExplicitHeight => _heightValid;

    /// <summary>The width QML set. A layout's assigned width does not replace it.</summary>
    internal double DeclaredWidth => _declaredWidth;

    /// <summary>The height QML set. A layout's assigned height does not replace it.</summary>
    internal double DeclaredHeight => _declaredHeight;

    /// <summary>Sets the geometry from an anchor, layout or positioner, which keeps the implicit size link.</summary>
    public void SetLayoutX(double value) => SetGeometry(ref _x, value, "x");

    public void SetLayoutY(double value) => SetGeometry(ref _y, value, "y");

    public void SetLayoutWidth(double value) => SetGeometry(ref _width, Math.Max(0, value), "width");

    public void SetLayoutHeight(double value) => SetGeometry(ref _height, Math.Max(0, value), "height");

    /// <summary>Lets width follow implicitWidth again, as it does when width was never set.</summary>
    public void ResetWidth()
    {
        _widthValid = false;
        SetGeometry(ref _width, _implicitWidth, "width");
    }

    public void ResetHeight()
    {
        _heightValid = false;
        SetGeometry(ref _height, _implicitHeight, "height");
    }

    private void SetGeometry(ref double field, double value, string name)
    {
        if (double.IsNaN(value))
            value = 0;
        if (field == value)
            return;
        field = value;
        NotifyChanged(name);
        OnGeometryChanged();
        Update();
    }

    protected virtual void OnGeometryChanged()
    {
    }

    protected virtual void OnImplicitSizeChanged()
    {
    }

    // Anchor lines, as values to assign to anchors.

    [Qml]
    public AnchorLine Left => new(this, AnchorEdge.Left);

    [Qml]
    public AnchorLine Right => new(this, AnchorEdge.Right);

    [Qml]
    public AnchorLine Top => new(this, AnchorEdge.Top);

    [Qml]
    public AnchorLine Bottom => new(this, AnchorEdge.Bottom);

    [Qml]
    public AnchorLine HorizontalCenter => new(this, AnchorEdge.HorizontalCenter);

    [Qml]
    public AnchorLine VerticalCenter => new(this, AnchorEdge.VerticalCenter);

    [Qml]
    public AnchorLine Baseline => new(this, AnchorEdge.Baseline);

    [Qml]
    public double BaselineOffset { get; set; }

    [Qml]
    public Anchors Anchors => _anchors ??= new Anchors(this) { Engine = Engine };

    public bool HasAnchors => _anchors is { IsActive: true };

    // Appearance

    [Qml]
    public bool Visible
    {
        get { Track(); return _visible && (_parent?.Visible ?? true); }
        set
        {
            if (_visible == value)
                return;
            _visible = value;
            NotifyVisibleTree();
            _parent?.OnChildVisibilityChanged(this);
            if (!Visible)
                ReleaseFocusIfUnusable();
        }
    }

    /// <summary>The item's own visible flag, without its parents.</summary>
    public bool IsVisibleFlag => _visible;

    private void NotifyVisibleTree()
    {
        NotifyChanged("visible");
        foreach (var child in _children.Snapshot)
        {
            if (child._visible)
                child.NotifyVisibleTree();
        }
        Update();
    }

    protected virtual void OnChildVisibilityChanged(Item child)
    {
    }

    [Qml]
    public double Opacity
    {
        get { Track(); return _opacity; }
        set
        {
            if (SetField(ref _opacity, Math.Clamp(value, 0, 1)))
                Update();
        }
    }

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled && (_parent?.Enabled ?? true); }
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            NotifyEnabledTree();
            if (!Enabled)
                ReleaseFocusIfUnusable();
            Update();
        }
    }

    private void NotifyEnabledTree()
    {
        NotifyChanged("enabled");
        foreach (var child in _children.Snapshot)
        {
            if (child._enabled)
                child.NotifyEnabledTree();
        }
    }

    /// <summary>Drops keyboard focus when this item, or a descendant that has it, can no longer take keys.</summary>
    private void ReleaseFocusIfUnusable()
    {
        if (_host?.FocusItem is not { } focused)
            return;
        for (var item = focused; item is not null; item = item.ParentItem)
        {
            if (item != this)
                continue;
            _host.SetFocusItem(null);
            return;
        }
    }

    [Qml]
    public bool Clip
    {
        get { Track(); return _clip; }
        set
        {
            if (SetField(ref _clip, value))
                Update();
        }
    }

    [Qml]
    public double Scale
    {
        get { Track(); return _scale; }
        set
        {
            if (SetField(ref _scale, value))
                Update();
        }
    }

    [Qml]
    public double Rotation
    {
        get { Track(); return _rotation; }
        set
        {
            if (SetField(ref _rotation, value))
                Update();
        }
    }

    [Qml]
    public int TransformOrigin
    {
        get { Track(); return _transformOrigin; }
        set
        {
            if (SetField(ref _transformOrigin, value))
                Update();
        }
    }

    [Qml]
    public bool Antialiasing
    {
        get { Track(); return _antialiasing; }
        set => SetField(ref _antialiasing, value);
    }

    [Qml]
    public bool Smooth
    {
        get { Track(); return _smooth; }
        set => SetField(ref _smooth, value);
    }

    [Qml]
    public string State
    {
        get { Track(); return _state; }
        set
        {
            var previous = _state;
            if (SetField(ref _state, value ?? ""))
                States.Apply(this, previous, _state);
        }
    }

    private readonly QmlList<QObject>? _states = null;

    [Qml("states")]
    public QmlList<QObject> StatesList => _statesList ??= new QmlList<QObject>(this, "states", added: s => s.SetOwner(this));

    private QmlList<QObject>? _statesList;

    [Qml("transitions")]
    public QmlList<QObject> TransitionsList => _transitionsList ??= new QmlList<QObject>(this, "transitions", added: s => s.SetOwner(this));

    private QmlList<QObject>? _transitionsList;

    [Qml]
    public LayerProperties Layer => _layer ??= new LayerProperties(this) { Engine = Engine };

    /// <summary>The pointer handlers declared in this item: HoverHandler, TapHandler, WheelHandler.</summary>
    public List<PointerHandler> PointerHandlers { get; } = [];

    [Qml]
    public Palette Palette
    {
        get
        {
            if (_palette is not null)
                return _palette;
            // The palette owns its color groups, and the item owns the palette, so destroying the item
            // unsubscribes the groups from the static theme event.
            _palette = new Palette { Engine = Engine };
            _palette.SetOwner(this);
            return _palette;
        }
    }

    private Palette? _palette;

    private LayerProperties? _layer;

    // Focus

    [Qml]
    public bool Focus
    {
        get { Track(); return _focus; }
        set
        {
            if (!SetField(ref _focus, value))
                return;
            if (Host is not { } host)
                return;
            if (value)
                host.SetFocusItem(this);
            else if (host.FocusItem == this)
                host.SetFocusItem(null);
        }
    }

    [Qml]
    public bool ActiveFocus
    {
        get { Track(); return _activeFocus; }
    }

    internal void SetActiveFocus(bool value)
    {
        if (SetField(ref _activeFocus, value, nameof(ActiveFocus)))
            OnActiveFocusChanged(value);
    }

    protected virtual void OnActiveFocusChanged(bool focused)
    {
    }

    /// <summary>
    /// Gives this item keyboard focus. As in Qt, a keyboard reason (Tab, Backtab, a shortcut) shows the focus
    /// rectangle, as moving focus with Tab does; any other reason leaves it as it is.
    /// </summary>
    [Qml]
    public void ForceActiveFocus(int reason = QtEnums.OtherFocusReason)
    {
        _focus = true;
        NotifyChanged("focus");
        if (reason is QtEnums.TabFocusReason or QtEnums.BacktabFocusReason or QtEnums.ShortcutFocusReason && Host is Scene scene)
            scene.MoveFocus(this);
        else
            Host?.SetFocusItem(this);
    }

    /// <summary>The tab stop after this item, or before it, in its window, wrapping around; null when there is none.</summary>
    [Qml]
    public Item? NextItemInFocusChain(bool forward = true) =>
        Host is Scene scene ? FocusNavigation.Next(scene.Root, this, forward) : null;

    [Qml]
    public bool ActiveFocusOnTab
    {
        get { Track(); return _activeFocusOnTab ?? DefaultActiveFocusOnTab; }
        set
        {
            if (_activeFocusOnTab == value)
                return;
            _activeFocusOnTab = value;
            NotifyChanged("activeFocusOnTab");
        }
    }

    private bool? _activeFocusOnTab;

    /// <summary>Whether Tab reaches the item when QML does not say, as it does a button or a text field.</summary>
    protected virtual bool DefaultActiveFocusOnTab => false;

    // Tree

    [Qml]
    public Item? Parent
    {
        get { Track(); return _parent; }
        set => SetParentItem(value);
    }

    public Item? ParentItem => _parent;

    [Qml]
    public QmlList<Item> Children => _children;

    [Qml]
    public QmlList<QObject> Resources => _resources;

    [Qml]
    public ItemData Data => _data;

    [Qml]
    public IReadOnlyList<Item> VisibleChildren
    {
        get
        {
            Track(nameof(Children));
            return _children.Snapshot.Where(c => c.Visible).ToList();
        }
    }

    [Qml]
    public QRect ChildrenRect
    {
        get
        {
            Track(nameof(Children));
            var items = _children.Items;
            if (items.Count == 0)
                return new QRect(0, 0, 0, 0);
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            foreach (var c in items)
            {
                l = Math.Min(l, c.X);
                t = Math.Min(t, c.Y);
                r = Math.Max(r, c.X + c.Width);
                b = Math.Max(b, c.Y + c.Height);
            }
            return new QRect(l, t, r - l, b - t);
        }
    }

    public IReadOnlyList<Item> ChildItems => _children.Snapshot;

    /// <summary>The children in the order they are drawn: by z, then by position in the list.</summary>
    public IEnumerable<Item> PaintOrder =>
        _children.Snapshot.Select((c, i) => (c, i)).OrderBy(t => t.c._z).ThenBy(t => t.i).Select(t => t.c);

    public void SetParentItem(Item? parent)
    {
        if (_parent == parent)
            return;
        if (parent == this)
            throw new QmlException("Cannot set an item as its own parent");
        var old = _parent;
        if (old is not null)
        {
            _parent = null;
            old._children.Remove(this);
        }
        if (parent is not null && !parent._children.Contains(this))
        {
            _parent = parent;
            parent._children.Add(this);
        }
        _parent = parent;
        if (Owner is null || Owner == old)
            SetOwner(parent);
        SetHost(parent?._host);
        NotifyChanged("parent");
        if (old is not null && old.Visible != (parent?.Visible ?? true))
            NotifyVisibleTree();
        OnParentChanged(old, parent);
        old?.Update();
        Update();
    }

    protected virtual void OnParentChanged(Item? oldParent, Item? newParent)
    {
    }

    private void OnChildListAdd(Item child)
    {
        if (child._parent != this)
        {
            var old = child._parent;
            if (old is not null)
            {
                child._parent = null;
                old._children.Remove(child);
            }
            child._parent = this;
            if (child.Owner is null || child.Owner == old)
                child.SetOwner(this);
            child.SetHost(_host);
            child.NotifyChanged("parent");
            child.OnParentChanged(old, this);
        }
        OnChildAdded(child);
        Emit("childrenChanged");
        Update();
    }

    private void OnChildListRemove(Item child)
    {
        if (child._parent == this)
        {
            child._parent = null;
            child.SetHost(null);
            child.NotifyChanged("parent");
            child.OnParentChanged(this, null);
        }
        OnChildRemoved(child);
        Emit("childrenChanged");
        Update();
    }

    protected virtual void OnChildAdded(Item child)
    {
    }

    protected virtual void OnChildRemoved(Item child)
    {
    }

    /// <summary>The window this item is shown in, or null.</summary>
    public IItemHost? Host => _host;

    public void SetHost(IItemHost? host)
    {
        if (_host == host)
            return;
        if (_host?.FocusItem == this && host is null)
            _host.SetFocusItem(null);
        if (_notification)
            MoveNotification(_host, host);
        _host = host;
        foreach (var child in _children.Snapshot)
            child.SetHost(host);
        OnHostChanged();
        if (host is not null && _focus)
            host.SetFocusItem(this);
    }

    /// <summary>The host changed, or its pixel ratio did.</summary>
    protected virtual void OnHostChanged()
    {
    }

    internal void PixelRatioChanged()
    {
        foreach (var child in _children.Snapshot)
            child.PixelRatioChanged();
        OnHostChanged();
    }

    /// <summary>Schedules a repaint of the window this item is in.</summary>
    public void Update() => _host?.RequestUpdate();

    /// <summary>Remember that this item is a notification, so the platform can find it without walking the scene.</summary>
    internal void TrackNotification()
    {
        _notification = true;
        if (_host is Scene scene)
            scene.Signals.Add(this);
    }

    /// <summary>The item is no longer a notification.</summary>
    internal void UntrackNotification()
    {
        if (!_notification)
            return;
        _notification = false;
        if (_host is Scene scene)
            scene.Signals.Remove(this);
    }

    private void MoveNotification(IItemHost? from, IItemHost? to)
    {
        if (from is Scene old)
            old.Signals.Remove(this);
        if (to is Scene next)
            next.Signals.Add(this);
    }

    // Painting

    /// <summary>Draws the item itself, in its own coordinates. Children are drawn after it.</summary>
    public virtual void Paint(SKCanvas canvas, PaintContext context)
    {
    }

    /// <summary>Whether this item takes the clip of a rounded shape for its children, as ClippingRectangle does.</summary>
    public virtual SKRoundRect? ClipShape => null;

    // Coordinates

    /// <summary>The point in the coordinates of the window's root item.</summary>
    public QPoint MapToScene(double x, double y)
    {
        for (var item = this; item is not null; item = item._parent)
        {
            if (item._parent is null)
                break;
            x += item._x;
            y += item._y;
        }
        return new QPoint(x, y);
    }

    public QPoint MapFromScene(double x, double y)
    {
        var origin = MapToScene(0, 0);
        return new QPoint(x - origin.X, y - origin.Y);
    }

    [Qml]
    public object MapToItem(Item? item, double x, double y, double width = double.NaN, double height = double.NaN)
    {
        var scene = MapToScene(x, y);
        var p = item is null ? scene : item.MapFromScene(scene.X, scene.Y);
        return double.IsNaN(width) ? p : new QRect(p.X, p.Y, width, height);
    }

    [Qml]
    public object MapFromItem(Item? item, double x, double y, double width = double.NaN, double height = double.NaN)
    {
        var scene = item is null ? new QPoint(x, y) : item.MapToScene(x, y);
        var p = MapFromScene(scene.X, scene.Y);
        return double.IsNaN(width) ? p : new QRect(p.X, p.Y, width, height);
    }

    [Qml]
    public QPoint MapToGlobal(double x, double y)
    {
        var scene = MapToScene(x, y);
        var origin = (Host?.WindowObject as IGlobalPosition)?.GlobalOrigin ?? new QPoint(0, 0);
        return new QPoint(scene.X + origin.X, scene.Y + origin.Y);
    }

    [Qml]
    public QPoint MapFromGlobal(double x, double y)
    {
        var origin = (Host?.WindowObject as IGlobalPosition)?.GlobalOrigin ?? new QPoint(0, 0);
        return MapFromScene(x - origin.X, y - origin.Y);
    }

    [Qml]
    public bool Contains(object? point)
    {
        if (ValueConverter.Unwrap(point) is QPoint p)
            return p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        return false;
    }

    [Qml]
    public Item? ChildAt(double x, double y)
    {
        foreach (var child in PaintOrder.Reverse())
        {
            if (child.Visible && x >= child.X && y >= child.Y && x < child.X + child.Width && y < child.Y + child.Height)
                return child;
        }
        return null;
    }

    /// <summary>Whether a point in the item's coordinates is inside it, for input.</summary>
    public virtual bool HitTest(double x, double y) => x >= 0 && y >= 0 && x < _width && y < _height;

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        _anchors?.Activate();
        if (_state.Length > 0)
            States.Apply(this, "", _state);
    }

    protected override void OnDestroy()
    {
        UntrackNotification();
        // A destroyed item can't keep focus: the window's focus item becomes none, and the window's
        // activeFocusItem says so.
        if (_host?.FocusItem == this)
            _host.SetFocusItem(null);
        _anchors?.Detach();
        if (_parent is not null)
        {
            var parent = _parent;
            _parent = null;
            parent._children.Remove(this);
        }
        foreach (var child in _children.Snapshot.ToArray())
            child.Destroy();
        base.OnDestroy();
        _ = _states;
    }
}

/// <summary>An object that knows where its coordinates are on the screen, such as a window.</summary>
public interface IGlobalPosition
{
    QPoint GlobalOrigin { get; }
}

/// <summary>
/// An item's <c>data</c> list: items added to it become children, other objects become resources.
/// </summary>
public sealed class ItemData(Item owner) : IQmlList
{
    public int Count => owner.Children.Count + owner.Resources.Count;

    public object? this[int index]
    {
        get
        {
            var children = owner.Children.Snapshot;
            return index < children.Count ? children[index] : owner.Resources.Snapshot[index - children.Count];
        }
    }

    private List<object?> All => owner.Children.Snapshot.Cast<object?>().Concat(owner.Resources.Snapshot).ToList();

    public Type ElementType => typeof(QObject);

    public event Action? Changed;

    public void Append(object? item)
    {
        switch (item)
        {
            case Item child:
                child.SetParentItem(owner);
                break;
            case IItemContainerChild windowLike:
                windowLike.AttachToItem(owner);
                owner.Resources.Add((QObject)windowLike);
                break;
            case QObject obj:
                owner.Resources.Add(obj);
                break;
            case null:
                break;
            default:
                throw new QmlException($"Cannot add {item} to an item");
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        foreach (var child in owner.Children.Snapshot.ToArray())
            child.SetParentItem(null);
        owner.Resources.Clear();
        Changed?.Invoke();
    }

    public IEnumerator<object?> GetEnumerator() => All.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A non-item object placed inside an item that wants to know which item, such as a popup window.</summary>
public interface IItemContainerChild
{
    void AttachToItem(Item item);
}

/// <summary>The <c>layer</c> grouped property. Layers are drawn the same as without one; effects read it.</summary>
public sealed class LayerProperties(Item owner) : QObject
{
    private bool _enabled;
    private QObject? _effect;

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                owner.Update();
        }
    }

    [Qml]
    public QObject? Effect
    {
        get { Track(); return _effect; }
        set
        {
            if (SetField(ref _effect, value))
                owner.Update();
        }
    }

    [Qml]
    public bool Smooth { get; set; }

    [Qml]
    public int Samples { get; set; }

    [Qml]
    public bool Mipmap { get; set; }

    [Qml]
    public string SamplerName { get; set; } = "source";

    [Qml]
    public object? TextureSize { get; set; }
}
