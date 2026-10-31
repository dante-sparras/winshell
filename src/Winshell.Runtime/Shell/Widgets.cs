using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Shell;

/// <summary>
/// The margin logic of the wrapper types: the wrapper's implicit size is its child's plus the margins,
/// and the child is placed inside the margins.
/// </summary>
public sealed class MarginWrapper : IPropertyObserver
{
    private readonly Item _wrapper;
    private Item? _child;
    private bool _childSet;
    private double _margin;
    private double? _top, _bottom, _left, _right;
    private double _extra;
    private double _inset;
    private bool _resizeChild = true;
    private double? _implicitWidthOverride, _implicitHeightOverride;
    private bool _updating;
    private static readonly string[] ChildProps = ["implicitWidth", "implicitHeight", "visible"];

    public MarginWrapper(Item wrapper)
    {
        _wrapper = wrapper;
    }

    public Item? Child
    {
        get => _child;
        set
        {
            _childSet = true;
            SetChild(value);
        }
    }

    public double Margin { get => _margin; set { _margin = value; Update(); } }

    public double TopMargin { get => _top ?? _margin; set { _top = value; Update(); } }

    public double BottomMargin { get => _bottom ?? _margin; set { _bottom = value; Update(); } }

    public double LeftMargin { get => _left ?? _margin; set { _left = value; Update(); } }

    public double RightMargin { get => _right ?? _margin; set { _right = value; Update(); } }

    public double ExtraMargin { get => _extra; set { _extra = value; Update(); } }

    /// <summary>Added on every side, on top of the margins. A rectangle uses it for the border width.</summary>
    public double Inset
    {
        get => _inset;
        set
        {
            if (_inset == value)
                return;
            _inset = value;
            Update();
        }
    }

    public bool ResizeChild { get => _resizeChild; set { _resizeChild = value; Update(); } }

    public void OverrideImplicitWidth(object? value)
    {
        _implicitWidthOverride = value is null ? null : ValueConverter.ToDouble(value, "real");
        Update();
    }

    public void OverrideImplicitHeight(object? value)
    {
        _implicitHeightOverride = value is null ? null : ValueConverter.ToDouble(value, "real");
        Update();
    }

    /// <summary>The first item child becomes the child, unless one was set.</summary>
    public void ChildAdded(Item item)
    {
        if (!_childSet && _child is null && item is not IWrapperInternal)
            SetChild(item);
    }

    public void ChildRemoved(Item item)
    {
        if (_child == item)
            SetChild(null);
    }

    private void SetChild(Item? child)
    {
        if (_child == child)
            return;
        if (_child is not null)
        {
            foreach (var p in ChildProps)
                _child.RemoveObserver(p, this);
        }
        _child = child;
        if (child is not null)
        {
            if (child.ParentItem != _wrapper)
                child.SetParentItem(_wrapper);
            foreach (var p in ChildProps)
                child.AddObserver(p, this);
        }
        _wrapper.NotifyChanged("child");
        Update();
    }

    public void OnPropertyChanged(QObject sender, string property) => Update();

    public void Update()
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            var l = LeftMargin + _extra + _inset;
            var r = RightMargin + _extra + _inset;
            var t = TopMargin + _extra + _inset;
            var b = BottomMargin + _extra + _inset;
            var childW = _child is { IsVisibleFlag: true } ? _child.ImplicitWidth : 0;
            var childH = _child is { IsVisibleFlag: true } ? _child.ImplicitHeight : 0;
            _wrapper.ImplicitWidth = _implicitWidthOverride ?? childW + l + r;
            _wrapper.ImplicitHeight = _implicitHeightOverride ?? childH + t + b;
            if (_child is null)
                return;
            var w = _wrapper.Width;
            var h = _wrapper.Height;
            if (_resizeChild)
            {
                _child.SetLayoutX(l);
                _child.SetLayoutY(t);
                _child.SetLayoutWidth(Math.Max(0, w - l - r));
                _child.SetLayoutHeight(Math.Max(0, h - t - b));
            }
            else
            {
                // Margins act as ratios of the free space.
                var cw = Math.Min(childW, Math.Max(0, w - l - r));
                var ch = Math.Min(childH, Math.Max(0, h - t - b));
                var freeX = w - cw;
                var freeY = h - ch;
                _child.SetLayoutX(l + r == 0 ? freeX / 2 : freeX * l / (l + r));
                _child.SetLayoutY(t + b == 0 ? freeY / 2 : freeY * t / (t + b));
                _child.SetLayoutWidth(cw);
                _child.SetLayoutHeight(ch);
            }
        }
        finally
        {
            _updating = false;
        }
    }
}

/// <summary>Marks items a wrapper adds itself, which must not become its child.</summary>
public interface IWrapperInternal;

/// <summary>The <c>WrapperItem</c>.</summary>
public class WrapperItem : Item
{
    public WrapperItem()
    {
        Margins = new MarginWrapper(this);
    }

    protected MarginWrapper Margins { get; }

    [Qml] public Item? Child { get { Track(); return Margins.Child; } set => Margins.Child = value; }

    [Qml] public double Margin { get => Margins.Margin; set { Margins.Margin = value; NotifyChanged("margin"); } }

    [Qml] public double TopMargin { get => Margins.TopMargin; set { Margins.TopMargin = value; NotifyChanged("topMargin"); } }

    [Qml] public double BottomMargin { get => Margins.BottomMargin; set { Margins.BottomMargin = value; NotifyChanged("bottomMargin"); } }

    [Qml] public double LeftMargin { get => Margins.LeftMargin; set { Margins.LeftMargin = value; NotifyChanged("leftMargin"); } }

    [Qml] public double RightMargin { get => Margins.RightMargin; set { Margins.RightMargin = value; NotifyChanged("rightMargin"); } }

    [Qml] public double ExtraMargin { get => Margins.ExtraMargin; set { Margins.ExtraMargin = value; NotifyChanged("extraMargin"); } }

    [Qml] public bool ResizeChild { get => Margins.ResizeChild; set { Margins.ResizeChild = value; NotifyChanged("resizeChild"); } }

    [Qml("implicitWidth")]
    public new double ImplicitWidth
    {
        get => base.ImplicitWidth;
        set => Margins.OverrideImplicitWidth(value);
    }

    [Qml("implicitHeight")]
    public new double ImplicitHeight
    {
        get => base.ImplicitHeight;
        set => Margins.OverrideImplicitHeight(value);
    }

    protected override void OnChildAdded(Item child) => Margins.ChildAdded(child);

    protected override void OnChildRemoved(Item child) => Margins.ChildRemoved(child);

    protected override void OnGeometryChanged() => Margins.Update();
}

/// <summary>The <c>WrapperRectangle</c>.</summary>
public class WrapperRectangle : Quick.Rectangle
{
    private bool _contentInsideBorder = true;

    public WrapperRectangle()
    {
        Margins = new MarginWrapper(this);
        Border.LayoutChanged = SyncInset;
    }

    private void SyncInset() =>
        Margins.Inset = _contentInsideBorder && Border.IsVisible ? Border.Width : 0;

    protected MarginWrapper Margins { get; }

    [Qml] public Item? Child { get { Track(); return Margins.Child; } set => Margins.Child = value; }

    [Qml] public double Margin { get => Margins.Margin; set { Margins.Margin = value; NotifyChanged("margin"); } }

    [Qml] public double TopMargin { get => Margins.TopMargin; set { Margins.TopMargin = value; NotifyChanged("topMargin"); } }

    [Qml] public double BottomMargin { get => Margins.BottomMargin; set { Margins.BottomMargin = value; NotifyChanged("bottomMargin"); } }

    [Qml] public double LeftMargin { get => Margins.LeftMargin; set { Margins.LeftMargin = value; NotifyChanged("leftMargin"); } }

    [Qml] public double RightMargin { get => Margins.RightMargin; set { Margins.RightMargin = value; NotifyChanged("rightMargin"); } }

    [Qml]
    public double ExtraMargin
    {
        get => Margins.ExtraMargin;
        set { Margins.ExtraMargin = value; NotifyChanged("extraMargin"); }
    }

    [Qml] public bool ResizeChild { get => Margins.ResizeChild; set { Margins.ResizeChild = value; NotifyChanged("resizeChild"); } }

    /// <summary>Keeps the child inside the border, by adding the border width to the margins.</summary>
    [Qml]
    public bool ContentInsideBorder
    {
        get { Track(); return _contentInsideBorder; }
        set
        {
            if (SetField(ref _contentInsideBorder, value))
                SyncInset();
        }
    }

    [Qml("implicitWidth")]
    public new double ImplicitWidth
    {
        get => base.ImplicitWidth;
        set => Margins.OverrideImplicitWidth(value);
    }

    [Qml("implicitHeight")]
    public new double ImplicitHeight
    {
        get => base.ImplicitHeight;
        set => Margins.OverrideImplicitHeight(value);
    }

    protected override void OnChildAdded(Item child) => Margins.ChildAdded(child);

    protected override void OnChildRemoved(Item child) => Margins.ChildRemoved(child);

    protected override void OnGeometryChanged() => Margins.Update();

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Margins.Update();
    }
}

/// <summary>The <c>ClippingWrapperRectangle</c>: a WrapperRectangle that clips its child to its corners.</summary>
public sealed class ClippingWrapperRectangle : WrapperRectangle, IRoundedClip
{
    public ClippingWrapperRectangle()
    {
        Clip = true;
    }
}

/// <summary>The <c>WrapperMouseArea</c>.</summary>
public sealed class WrapperMouseArea : MouseArea
{
    private readonly MarginWrapper _wrapper;

    public WrapperMouseArea()
    {
        _wrapper = new MarginWrapper(this);
    }

    [Qml] public Item? Child { get { Track(); return _wrapper.Child; } set => _wrapper.Child = value; }

    [Qml] public double Margin { get => _wrapper.Margin; set { _wrapper.Margin = value; NotifyChanged("margin"); } }

    [Qml] public double TopMargin { get => _wrapper.TopMargin; set { _wrapper.TopMargin = value; NotifyChanged("topMargin"); } }

    [Qml] public double BottomMargin { get => _wrapper.BottomMargin; set { _wrapper.BottomMargin = value; NotifyChanged("bottomMargin"); } }

    [Qml] public double LeftMargin { get => _wrapper.LeftMargin; set { _wrapper.LeftMargin = value; NotifyChanged("leftMargin"); } }

    [Qml] public double RightMargin { get => _wrapper.RightMargin; set { _wrapper.RightMargin = value; NotifyChanged("rightMargin"); } }

    [Qml] public double ExtraMargin { get => _wrapper.ExtraMargin; set { _wrapper.ExtraMargin = value; NotifyChanged("extraMargin"); } }

    [Qml] public bool ResizeChild { get => _wrapper.ResizeChild; set { _wrapper.ResizeChild = value; NotifyChanged("resizeChild"); } }

    [Qml("implicitWidth")]
    public new double ImplicitWidth
    {
        get => base.ImplicitWidth;
        set => _wrapper.OverrideImplicitWidth(value);
    }

    [Qml("implicitHeight")]
    public new double ImplicitHeight
    {
        get => base.ImplicitHeight;
        set => _wrapper.OverrideImplicitHeight(value);
    }

    protected override void OnChildAdded(Item child) => _wrapper.ChildAdded(child);

    protected override void OnChildRemoved(Item child) => _wrapper.ChildRemoved(child);

    protected override void OnGeometryChanged() => _wrapper.Update();
}

/// <summary>
/// The <c>WrapperManager</c>: picks the one visual child of a wrapper item.
/// A single child is used automatically. More than one child needs <see cref="Child"/> set, and the rest are unparented.
/// </summary>
public sealed class WrapperManager : QObject
{
    private Item? _wrapper;
    private Item? _assignedWrapper;
    private Item? _defaultChild;
    private Item? _child;
    private IDisposable? _destroyed;
    private bool _ready;
    private bool _assigned;
    private bool _multiple;

    /// <summary>The item whose visual child is wrapped. QML name is <c>wrapper</c>. The C# name avoids <see cref="QObject"/>'s script wrapper.</summary>
    [Qml("wrapper")]
    public Item? Wrapper_
    {
        get { TrackQml("wrapper"); return _wrapper ?? _assignedWrapper; }
        set
        {
            if (_ready)
            {
                (Engine?.Log ?? Log.Default).Warn("scene", "Cannot set wrapper after WrapperManager initialization.");
                return;
            }
            _assignedWrapper = value;
            NotifyChanged("wrapper");
        }
    }

    [Qml]
    public Item? Child
    {
        get { Track(); return _child; }
        set => Assign(value, value is null);
    }

    public override void ComponentComplete()
    {
        _wrapper = _assignedWrapper ?? Owner as Item;
        _ready = true;
        if (_wrapper is null)
        {
            (Engine?.Log ?? Log.Default).Warn("scene", "Parent of WrapperManager is not an Item.");
            return;
        }
        var items = _wrapper.ChildItems.ToList();
        if (items.Count == 1)
            _defaultChild = items[0];
        else if (items.Count > 1)
            _multiple = true;
        var chosen = _assigned ? _child : _defaultChild;
        if (!_assigned && _multiple)
            WarnMultiple();
        foreach (var item in items)
        {
            if (!ReferenceEquals(item, chosen))
                Detach(item);
        }
        Adopt(chosen);
    }

    private void Assign(Item? child, bool explicitNull)
    {
        _assigned = true;
        if (!_ready)
        {
            _child = explicitNull ? null : child;
            Watch(child);
            return;
        }
        Adopt(explicitNull ? null : child);
    }

    private void Adopt(Item? child)
    {
        if (ReferenceEquals(child, _child) && (child is null || _wrapper is null || ReferenceEquals(child.ParentItem, _wrapper)))
            return;
        if (_child is { } previous && _wrapper is not null && ReferenceEquals(previous.ParentItem, _wrapper))
            Detach(previous);
        _child = child;
        Watch(child);
        if (child is not null && _wrapper is not null && !ReferenceEquals(child.ParentItem, _wrapper))
            child.SetParentItem(_wrapper);
        NotifyChanged("child");
    }

    private void Watch(Item? child)
    {
        _destroyed?.Dispose();
        _destroyed = child?.Connect("destroyed", _ =>
        {
            _child = null;
            NotifyChanged("child");
        });
    }

    private void Detach(Item item)
    {
        item.SetParentItem(null);
        if (item.Owner is null && _wrapper is not null)
            item.SetOwner(_wrapper);
    }

    private void WarnMultiple() =>
        (Engine?.Log ?? Log.Default).Warn("scene", "Wrapper component cannot have more than one visual child. Remove all additional children, or pick a specific component to wrap using the child property.");
}

/// <summary>
/// The <c>MarginWrapperManager</c>: gives the item it is declared in the margin behavior of a wrapper.
/// </summary>
public sealed class MarginWrapperManager : QObject
{
    private MarginWrapper? _wrapper;
    private Item? _target;
    private Item? _child;
    private double _margin;
    private double? _top, _bottom, _left, _right;
    private double _extra;
    private bool _resizeChild = true;
    private double? _implicitWidth;
    private double? _implicitHeight;

    /// <summary>The item this manager is declared in. The C# name avoids <see cref="QObject"/>'s script wrapper.</summary>
    [Qml("wrapper")]
    public Item? Wrapper_
    {
        get { TrackQml("wrapper"); return _target ?? Owner as Item; }
    }

    [Qml] public Item? Child { get { Track(); return _child; } set { if (SetField(ref _child, value)) Apply(); } }

    [Qml]
    public double Margin
    {
        get { Track(); return _margin; }
        set
        {
            if (!SetField(ref _margin, value))
                return;
            // A side with no margin of its own follows this one.
            if (_top is null) NotifyChanged("topMargin");
            if (_bottom is null) NotifyChanged("bottomMargin");
            if (_left is null) NotifyChanged("leftMargin");
            if (_right is null) NotifyChanged("rightMargin");
            Apply();
        }
    }

    [Qml] public double TopMargin { get { Track(); return _top ?? _margin; } set { if (SetField(ref _top, value, nameof(TopMargin))) Apply(); } }

    [Qml] public double BottomMargin { get { Track(); return _bottom ?? _margin; } set { if (SetField(ref _bottom, value, nameof(BottomMargin))) Apply(); } }

    [Qml] public double LeftMargin { get { Track(); return _left ?? _margin; } set { if (SetField(ref _left, value, nameof(LeftMargin))) Apply(); } }

    [Qml] public double RightMargin { get { Track(); return _right ?? _margin; } set { if (SetField(ref _right, value, nameof(RightMargin))) Apply(); } }

    [Qml] public double ExtraMargin { get { Track(); return _extra; } set { if (SetField(ref _extra, value)) Apply(); } }

    [Qml] public bool ResizeChild { get { Track(); return _resizeChild; } set { if (SetField(ref _resizeChild, value)) Apply(); } }

    [Qml]
    public double ImplicitWidth
    {
        get { Track(); return _implicitWidth ?? _target?.ImplicitWidth ?? 0; }
        set
        {
            if (!SetField(ref _implicitWidth, value))
                return;
            _wrapper?.OverrideImplicitWidth(value);
        }
    }

    [Qml]
    public double ImplicitHeight
    {
        get { Track(); return _implicitHeight ?? _target?.ImplicitHeight ?? 0; }
        set
        {
            if (!SetField(ref _implicitHeight, value))
                return;
            _wrapper?.OverrideImplicitHeight(value);
        }
    }

    protected override void OnOwnerChanged() => _target = Owner as Item;

    public override void ComponentComplete()
    {
        _target = Owner as Item;
        if (_target is null)
            return;
        _wrapper = new MarginWrapper(_target);
        _target.AddObserver("width", _wrapper);
        _target.AddObserver("height", _wrapper);
        Apply();
    }

    private void Apply()
    {
        if (_wrapper is null || _target is null)
            return;
        _wrapper.Margin = _margin;
        if (_top is { } t) _wrapper.TopMargin = t;
        if (_bottom is { } b) _wrapper.BottomMargin = b;
        if (_left is { } l) _wrapper.LeftMargin = l;
        if (_right is { } r) _wrapper.RightMargin = r;
        _wrapper.ExtraMargin = _extra;
        _wrapper.ResizeChild = _resizeChild;
        if (_implicitWidth is { } width)
            _wrapper.OverrideImplicitWidth(width);
        if (_implicitHeight is { } height)
            _wrapper.OverrideImplicitHeight(height);
        _wrapper.Child = _child ?? _target.ChildItems.FirstOrDefault();
    }
}

/// <summary>The <c>ClippingRectangle</c>: a rectangle whose children are clipped to its rounded corners.</summary>
public sealed class ClippingRectangle : Quick.Rectangle, IRoundedClip
{
    private bool _contentInsideBorder;

    public ClippingRectangle()
    {
        Clip = true;
    }

    [Qml]
    public Item ContentItem => this;

    [Qml]
    public bool ContentInsideBorder { get { Track(); return _contentInsideBorder; } set => SetField(ref _contentInsideBorder, value); }

    public override SKRoundRect? ClipShape => Shape(_contentInsideBorder && Border.IsVisible ? Border.Width : 0);
}

/// <summary>The <c>IconImage</c>: a square image for an icon, sharp at the window's pixel ratio.</summary>
public sealed class IconImage : Item
{
    private readonly Quick.Image _image;
    private double _implicitSize;

    public IconImage()
    {
        _image = new BackerImage { FillMode = (int)Quick.Image.FillModeValue.PreserveAspectFit };
        _image.SetParentItem(this);
        _image.Anchors.Fill = this;
        _image.AddObserver("status", new Forward(this, "status"));
    }

    private sealed class BackerImage : Quick.Image, IWrapperInternal;

    private sealed class Forward(QObject owner, string name) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => owner.NotifyChanged(name);
    }

    [Qml] public string Source { get => _image.Source; set { _image.Source = value; NotifyChanged("source"); } }

    [Qml]
    public double ImplicitSize
    {
        get { Track(); return _implicitSize; }
        set
        {
            if (!SetField(ref _implicitSize, value))
                return;
            base.ImplicitWidth = value;
            base.ImplicitHeight = value;
            UpdateSourceSize();
        }
    }

    [Qml]
    public QSize ActualSize
    {
        get { Track("width"); return new QSize(Width, Height); }
    }

    [Qml] public bool Asynchronous { get => _image.Asynchronous; set => _image.Asynchronous = value; }

    [Qml] public bool Mipmap { get => _image.Mipmap; set => _image.Mipmap = value; }

    [Qml] public int Status { get { Track(); return _image.Status; } }

    [Qml] public Quick.Image Backer => _image;

    protected override void OnGeometryChanged() => UpdateSourceSize();

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        _image.Engine = Engine;
        _image.CreationContext = CreationContext;
        _image.MarkComplete();
        UpdateSourceSize();
    }

    private void UpdateSourceSize()
    {
        var size = Math.Max(Width, Height);
        if (size > 0)
            _image.SourceSize = new QSize(Math.Ceiling(size), Math.Ceiling(size));
    }
}
