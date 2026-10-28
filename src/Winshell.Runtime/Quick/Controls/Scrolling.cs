using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>
/// QtQuick.Controls' <c>ScrollBar</c>, drawn as WinUI's: a thin line that widens into a bar with a track and
/// arrows once the pointer has rested on it for 400 ms, and narrows again 2 s after the pointer leaves.
/// </summary>
public sealed class ScrollBar : Control, IPointerTarget, IKeepsPointerGrab
{
    private double _size = 1, _position, _stepSize, _minimumSize;
    private bool _active, _pressed, _interactive = true;
    private int _orientation = 2;
    private int _policy;
    private int _snapMode;
    private double _pressOffset;

    [QmlEnum]
    public enum PolicyValue
    {
        AsNeeded = 0,
        AlwaysOff = 1,
        AlwaysOn = 2,
    }

    [QmlEnum]
    public enum SnapModeValue
    {
        NoSnap = 0,
        SnapAlways = 1,
        SnapOnRelease = 2,
    }

    /// <summary>The length of WinUI's arrow buttons at each end, which the thumb does not cover.</summary>
    private const double ArrowZone = 16;

    private readonly NumberTransition _expansion = new(100);
    private bool _wantsExpanded;
    private TimeSpan _wantsSince = TimeSpan.MinValue;
    private IDisposable? _wake;
    private double _expanded;

    public ScrollBar()
    {
        SetPaddingForOrientation();
    }

    private void SetPaddingForOrientation()
    {
        if (_orientation == 2)
            SetDefaultPadding(3, ArrowZone, 3, ArrowZone);
        else
            SetDefaultPadding(ArrowZone, 3, ArrowZone, 3);
    }

    [Qml] public double Size { get { Track(); return _size; } set { if (SetField(ref _size, Math.Clamp(value, 0, 1))) GeometryUpdated(); } }

    [Qml] public double Position { get { Track(); return _position; } set { if (SetField(ref _position, value)) GeometryUpdated(); } }

    [Qml] public double StepSize { get { Track(); return _stepSize; } set => SetField(ref _stepSize, value); }

    [Qml] public double MinimumSize { get { Track(); return _minimumSize; } set { if (SetField(ref _minimumSize, value)) GeometryUpdated(); } }

    [Qml] public bool Active { get { Track(); return _active || _pressed || Hovered; } set => SetField(ref _active, value); }

    [Qml("pressed")] public bool IsPressed { get { Track("pressed"); return _pressed; } set => SetPressed(value); }

    [Qml] public bool Interactive { get { Track(); return _interactive; } set => SetField(ref _interactive, value); }

    [Qml]
    public int Orientation
    {
        get { Track(); return _orientation; }
        set
        {
            if (SetField(ref _orientation, value))
            {
                NotifyChanged("horizontal");
                NotifyChanged("vertical");
                SetPaddingForOrientation();
                UpdateImplicitSize();
                LayoutChildren();
            }
        }
    }

    [Qml] public bool Horizontal => _orientation == 1;

    [Qml] public bool Vertical => _orientation == 2;

    [Qml] public int Policy { get { Track(); return _policy; } set { if (SetField(ref _policy, value)) PolicyChanged?.Invoke(); } }

    [Qml] public int SnapMode { get { Track(); return _snapMode; } set => SetField(ref _snapMode, value); }

    /// <summary>The size of the handle as drawn, at least <c>minimumSize</c>.</summary>
    [Qml] public double VisualSize { get { Track("size"); Track("minimumSize"); return Math.Min(1, Math.Max(_size, _minimumSize)); } }

    /// <summary>The position of the handle as drawn, kept inside the bar.</summary>
    [Qml]
    public double VisualPosition
    {
        get
        {
            Track("position");
            Track("size");
            var vs = VisualSize;
            if (_size >= 1)
                return 0;
            var p = _position / (1 - _size) * (1 - vs);
            return Math.Clamp(p, 0, 1 - vs);
        }
    }

    internal event Action? PolicyChanged;

    /// <summary>Whether the bar shows: its policy, and for AsNeeded whether the content is larger than the view.</summary>
    public bool IsNeeded => _policy switch
    {
        (int)PolicyValue.AlwaysOff => false,
        (int)PolicyValue.AlwaysOn => true,
        _ => _size < 1,
    };

    bool IKeepsPointerGrab.KeepsGrab => _pressed;

    /// <summary>
    /// How far the bar has widened, 0 to 1. It widens 400 ms after the pointer comes to rest on it, or at once when
    /// pressed, and narrows 2 s after the pointer leaves, each over 100 ms.
    /// </summary>
    private double Expansion(PaintContext context)
    {
        var wants = Hovered || _pressed;
        if (context.Time <= TimeSpan.Zero)
            return wants ? 1 : 0;
        if (_wantsSince == TimeSpan.MinValue)
        {
            // The first paint shows the bar as it is, without waiting.
            _wantsExpanded = wants;
            _wantsSince = context.Time - TimeSpan.FromSeconds(10);
            _expansion.Jump(wants ? 1 : 0);
        }
        if (wants != _wantsExpanded)
        {
            _wantsExpanded = wants;
            _wantsSince = context.Time;
            _wake?.Dispose();
            _wake = null;
        }
        var delay = _pressed ? TimeSpan.Zero : TimeSpan.FromMilliseconds(wants ? 400 : 2000);
        var waited = context.Time - _wantsSince;
        if (waited < delay)
        {
            // Wake once when the delay ends, instead of painting every frame until then.
            _wake ??= Engine?.Loop.Schedule(delay - waited, () => { _wake = null; Update(); });
            return _expansion.Step(wants ? 0 : 1, context);
        }
        return _expansion.Step(wants ? 1 : 0, context);
    }

    private void GeometryUpdated()
    {
        NotifyChanged("visualSize");
        NotifyChanged("visualPosition");
        LayoutChildren();
        Update();
    }

    private void SetPressed(bool value)
    {
        if (SetField(ref _pressed, value, "pressed"))
        {
            NotifyChanged("active");
            StateChanged();
        }
    }

    protected override void StateChanged()
    {
        NotifyChanged("active");
        base.StateChanged();
    }

    [Qml]
    public void Increase() => Position = Math.Min(1 - _size, _position + (_stepSize > 0 ? _stepSize : 0.1));

    [Qml]
    public void Decrease() => Position = Math.Max(0, _position - (_stepSize > 0 ? _stepSize : 0.1));

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, context) =>
    {
        _expanded = Expansion(context);
        if (_expanded <= 0 || !IsNeeded)
            return;
        var t = Theme.Current;
        var r = i.Bounds;
        Fluent.FillRound(c, r, 6, FluentResources.ScrollBarTrackFill(t).Faded(_expanded));
        if (t.IsHighContrast)
            Fluent.StrokeRound(c, r, 6, t.SystemColorButtonTextColor.Faded(_expanded));
        // Segoe Fluent Icons' solid carets at 8 pixels, centered in each end's arrow zone.
        var arrow = FluentResources.ScrollBarButtonArrowForeground(t).For(Enabled, false, false).Faded(_expanded);
        var zone = (float)ArrowZone;
        if (_orientation == 2)
        {
            DrawArrow(c, Fluent.CaretUpSolid, r.MidX, r.Top + zone / 2, arrow);
            DrawArrow(c, Fluent.CaretDownSolid, r.MidX, r.Bottom - zone / 2, arrow);
        }
        else
        {
            DrawArrow(c, Fluent.CaretLeftSolid, r.Left + zone / 2, r.MidY, arrow);
            DrawArrow(c, Fluent.CaretRightSolid, r.Right - zone / 2, r.MidY, arrow);
        }
    }, 12, 12));

    private static void DrawArrow(SKCanvas c, string glyph, float cx, float cy, QColor color)
    {
        if (Fluent.DrawGlyph(c, glyph, cx, cy, 8, color))
            return;
        // Without the icon font, a small triangle pointing the same way.
        var (dx, dy) = glyph switch
        {
            Fluent.CaretUpSolid => (0f, -1f),
            Fluent.CaretDownSolid => (0f, 1f),
            Fluent.CaretLeftSolid => (-1f, 0f),
            _ => (1f, 0f),
        };
        using var builder = new SKPathBuilder();
        builder.MoveTo(cx + dx * 3, cy + dy * 3);
        builder.LineTo(cx - dx * 2 + dy * 3, cy - dy * 2 + dx * 3);
        builder.LineTo(cx - dx * 2 - dy * 3, cy - dy * 2 - dx * 3);
        builder.Close();
        using var path = builder.Detach();
        using var paint = new SKPaint { Color = color.ToSk(), IsAntialias = true };
        c.DrawPath(path, paint);
    }

    protected override Item? CreateDefaultContentItem() => Part(new PaintedItem((c, i, _) =>
    {
        if (!IsNeeded)
            return;
        var t = Theme.Current;
        var r = i.Bounds;
        // A 2 pixel line near the far edge widens into a centered 6 pixel thumb.
        var e = (float)_expanded;
        var thickness = 2 + 4 * e;
        var offset = 2 * (1 - e);
        var color = FluentResources.ScrollBarThumbFill(t).For(Enabled, _pressed, Hovered);
        var rect = _orientation == 2
            ? SKRect.Create(r.MidX - thickness / 2 + offset, r.Top, thickness, r.Height)
            : SKRect.Create(r.Left, r.MidY - thickness / 2 + offset, r.Width, thickness);
        Fluent.FillRound(c, rect, thickness / 2, color);
    }, 6, 6));

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        using var _ = DependencyTracker.Suspend();
        var content = ContentItem;
        var thickness = Math.Max(Background?.ImplicitWidth ?? 0, (content?.ImplicitWidth ?? 0) + LeftPadding + RightPadding);
        if (_orientation == 1)
        {
            thickness = Math.Max(Background?.ImplicitHeight ?? 0, (content?.ImplicitHeight ?? 0) + TopPadding + BottomPadding);
            return (Background?.ImplicitWidth ?? 0, thickness);
        }
        return (thickness, Background?.ImplicitHeight ?? 0);
    }

    protected override void LayoutParts()
    {
        if (ContentItem is not { } handle || handle.ParentItem != this || handle.HasAnchors)
            return;
        var length = _orientation == 2 ? AvailableHeight : AvailableWidth;
        var start = VisualPosition * length;
        var size = VisualSize * length;
        if (_orientation == 2)
        {
            handle.SetLayoutX(LeftPadding);
            handle.SetLayoutY(TopPadding + start);
            handle.SetLayoutWidth(AvailableWidth);
            handle.SetLayoutHeight(size);
        }
        else
        {
            handle.SetLayoutX(LeftPadding + start);
            handle.SetLayoutY(TopPadding);
            handle.SetLayoutWidth(size);
            handle.SetLayoutHeight(AvailableHeight);
        }
    }

    public override bool HitTest(double x, double y) => IsNeeded && base.HitTest(x, y);

    public override bool AcceptsHover => IsNeeded && base.AcceptsHover;

    public bool AcceptsButton(int button) => _interactive && Enabled && IsNeeded && button == QtEnums.LeftButton;

    private double PointerPosition(double x, double y)
    {
        var length = _orientation == 2 ? AvailableHeight : AvailableWidth;
        var at = _orientation == 2 ? y - TopPadding : x - LeftPadding;
        var vs = VisualSize;
        if (length <= 0 || vs >= 1)
            return 0;
        // The handle's start, in 0..1-visualSize, then mapped back to the position range.
        var visual = Math.Clamp((at - _pressOffset) / length, 0, 1 - vs);
        return visual / (1 - vs) * (1 - _size);
    }

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        var length = _orientation == 2 ? AvailableHeight : AvailableWidth;
        var at = _orientation == 2 ? y - TopPadding : x - LeftPadding;
        // The arrows at the ends step, as WinUI's small decrease and increase buttons do.
        if (at < 0 || at > length)
        {
            if (at < 0)
                Decrease();
            else
                Increase();
            return true;
        }
        var start = VisualPosition * length;
        var size = VisualSize * length;
        // A press on the handle drags it from where it was taken; elsewhere the handle centers on the press.
        _pressOffset = at >= start && at <= start + size ? at - start : size / 2;
        SetPressed(true);
        Position = PointerPosition(x, y);
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (_pressed)
            Position = PointerPosition(x, y);
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers) => SetPressed(false);

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel() => SetPressed(false);
}

/// <summary>
/// The attached <c>ScrollBar.vertical</c> and <c>ScrollBar.horizontal</c> of a Flickable or ScrollView:
/// keeps each bar's size and position in step with the view, and the view with a dragged bar.
/// </summary>
public sealed class ScrollBarAttached : QObject
{
    private readonly QObject _owner;
    private ScrollBar? _vertical, _horizontal;
    private Flickable? _flickable;
    private readonly List<IDisposable> _watches = [];
    private bool _syncing;

    public ScrollBarAttached(QObject owner)
    {
        _owner = owner;
    }

    [Qml]
    public ScrollBar? Vertical
    {
        get { Track(); return _vertical; }
        set
        {
            if (_vertical == value)
                return;
            Detach(_vertical);
            _vertical = value;
            if (value is not null)
                value.Orientation = 2;
            NotifyChanged("vertical");
            Rebind();
        }
    }

    [Qml]
    public ScrollBar? Horizontal
    {
        get { Track(); return _horizontal; }
        set
        {
            if (_horizontal == value)
                return;
            Detach(_horizontal);
            _horizontal = value;
            if (value is not null)
                value.Orientation = 1;
            NotifyChanged("horizontal");
            Rebind();
        }
    }

    private Item? HostItem => _owner as Item;

    private void Detach(ScrollBar? bar)
    {
        if (bar is not null && bar.ParentItem == HostItem && _owner is not ScrollView)
            bar.SetParentItem(null);
    }

    /// <summary>Connects the bars to the view: the Flickable itself, or the one a ScrollView holds.</summary>
    internal void Rebind()
    {
        foreach (var w in _watches)
            w.Dispose();
        _watches.Clear();
        _flickable = _owner as Flickable ?? (_owner as ScrollView)?.Flickable;
        var host = HostItem;
        if (host is null)
            return;
        foreach (var bar in new[] { _vertical, _horizontal })
        {
            if (bar is null)
                continue;
            if (bar.ParentItem is null)
                bar.SetParentItem(host);
            if (bar.Z == 0)
                bar.Z = 1;
            _watches.Add(new PropertyWatch(bar, () => FromBar(bar), "position"));
            _watches.Add(new PropertyWatch(bar, Place, "implicitWidth", "implicitHeight", "visible"));
            bar.PolicyChanged += Place;
        }
        _watches.Add(new PropertyWatch(host, Place, "width", "height"));
        if (host is Control control)
            _watches.Add(new PropertyWatch(control, Place, "topPadding", "bottomPadding", "leftPadding", "rightPadding"));
        if (_flickable is not null)
            _watches.Add(new PropertyWatch(_flickable, Sync, "contentX", "contentY", "contentWidth", "contentHeight", "width", "height", "moving"));
        Place();
        Sync();
    }

    /// <summary>Puts the vertical bar along the right edge and the horizontal one along the bottom.</summary>
    private void Place()
    {
        var host = HostItem;
        if (host is null)
            return;
        using var _ = DependencyTracker.Suspend();
        var both = _vertical is { IsNeeded: true } && _horizontal is { IsNeeded: true };
        if (_vertical is { } v && v.ParentItem == host && !v.HasAnchors)
        {
            if (!v.HasExplicitWidth)
                v.SetLayoutWidth(v.ImplicitWidth);
            if (v.GetBinding("x") is null)
                v.SetLayoutX(host.Width - v.Width);
            if (v.GetBinding("y") is null)
                v.SetLayoutY(0);
            if (!v.HasExplicitHeight)
                v.SetLayoutHeight(Math.Max(0, host.Height - (both ? _horizontal!.Height : 0)));
        }
        if (_horizontal is { } h && h.ParentItem == host && !h.HasAnchors)
        {
            if (!h.HasExplicitHeight)
                h.SetLayoutHeight(h.ImplicitHeight);
            if (h.GetBinding("y") is null)
                h.SetLayoutY(host.Height - h.Height);
            if (h.GetBinding("x") is null)
                h.SetLayoutX(0);
            if (!h.HasExplicitWidth)
                h.SetLayoutWidth(Math.Max(0, host.Width - (both ? _vertical!.Width : 0)));
        }
    }

    /// <summary>Sets each bar's size and position from the view.</summary>
    private void Sync()
    {
        if (_flickable is not { } f || _syncing)
            return;
        _syncing = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            if (_vertical is { } v)
            {
                var ch = Math.Max(f.ContentHeight, 1);
                v.Size = Math.Min(1, f.Height / ch);
                v.Position = f.ContentY / ch;
                v.Active = f.Moving;
            }
            if (_horizontal is { } h)
            {
                var cw = Math.Max(f.ContentWidth, 1);
                h.Size = Math.Min(1, f.Width / cw);
                h.Position = f.ContentX / cw;
                h.Active = f.Moving;
            }
        }
        finally
        {
            _syncing = false;
        }
        Place();
    }

    /// <summary>Scrolls the view to a dragged bar.</summary>
    private void FromBar(ScrollBar bar)
    {
        if (_flickable is not { } f || _syncing || !bar.IsPressed)
            return;
        _syncing = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            if (bar == _vertical)
                f.ContentY = bar.Position * f.ContentHeight;
            else
                f.ContentX = bar.Position * f.ContentWidth;
        }
        finally
        {
            _syncing = false;
        }
    }
}

/// <summary>
/// QtQuick.Controls' <c>ScrollView</c>: scrolls its content in a Flickable with scroll bars. A Flickable or
/// ListView child is used as the view; other children go into a Flickable the scroll view makes.
/// </summary>
[QmlDefaultProperty("contentData")]
public sealed class ScrollView : Control
{
    private Flickable _flickable;
    private bool _ownFlickable = true;
    private double? _contentWidth, _contentHeight;
    private PropertyWatch? _childWatch;
    private Item? _single;
    private readonly ScrollViewData _data;

    public ScrollView()
    {
        _flickable = MakeFlickable();
        InitContentItem(_flickable);
        _data = new ScrollViewData(this);
    }

    private static Flickable MakeFlickable()
    {
        var f = new Flickable { Clip = true };
        f.MarkComplete();
        return f;
    }

    public Flickable Flickable => _flickable;

    [Qml] public ScrollViewData ContentData => _data;

    [Qml] public QmlList<Item> ContentChildren => _flickable.ContentItem.Children;

    [Qml]
    public double ContentWidth
    {
        get { Track(); return _contentWidth ?? (_ownFlickable ? _single?.ImplicitWidth ?? 0 : _flickable.ContentWidth); }
        set { _contentWidth = value; SyncContentSize(); }
    }

    [Qml]
    public double ContentHeight
    {
        get { Track(); return _contentHeight ?? (_ownFlickable ? _single?.ImplicitHeight ?? 0 : _flickable.ContentHeight); }
        set { _contentHeight = value; SyncContentSize(); }
    }

    public override double ImplicitContentWidth { get { TrackQml("implicitContentWidth"); return ContentWidth; } }

    public override double ImplicitContentHeight { get { TrackQml("implicitContentHeight"); return ContentHeight; } }

    private ScrollBarAttached Bars => (ScrollBarAttached)GetAttached(typeof(ScrollBarAttached), o => new ScrollBarAttached(o));

    internal void Add(object? item)
    {
        switch (item)
        {
            case Flickable flickable when _ownFlickable && _flickable.ContentItem.ChildItems.Count == 0:
                var old = _flickable;
                _flickable = flickable;
                _ownFlickable = false;
                ContentItem = flickable;
                old.Destroy();
                Bars.Rebind();
                break;
            case Item child:
                child.SetParentItem(_flickable.ContentItem);
                WatchSingle();
                break;
            case QObject obj:
                Resources.Add(obj);
                break;
        }
    }

    private void WatchSingle()
    {
        var children = _flickable.ContentItem.ChildItems;
        var single = children.Count == 1 ? children[0] : null;
        if (single == _single)
            return;
        _childWatch?.Dispose();
        _single = single;
        if (single is not null)
            _childWatch = new PropertyWatch(single, SyncContentSize, "implicitWidth", "implicitHeight", "width", "height");
        SyncContentSize();
    }

    private void SyncContentSize()
    {
        NotifyChanged("contentWidth");
        NotifyChanged("contentHeight");
        NotifyChanged("implicitContentWidth");
        NotifyChanged("implicitContentHeight");
        if (_ownFlickable)
        {
            using var _ = DependencyTracker.Suspend();
            var w = _contentWidth ?? (_single is null ? 0 : Math.Max(_single.ImplicitWidth, _single.HasExplicitWidth ? _single.Width : 0));
            var h = _contentHeight ?? (_single is null ? 0 : Math.Max(_single.ImplicitHeight, _single.HasExplicitHeight ? _single.Height : 0));
            _flickable.ContentWidth = w;
            _flickable.ContentHeight = h;
        }
        UpdateImplicitSize();
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        var bars = Bars;
        if (bars.Vertical is null)
        {
            var v = new ScrollBar { Engine = Engine };
            v.MarkComplete();
            bars.Vertical = v;
        }
        if (bars.Horizontal is null)
        {
            var h = new ScrollBar { Engine = Engine };
            h.MarkComplete();
            bars.Horizontal = h;
        }
        bars.Rebind();
        SyncContentSize();
    }

    protected override void OnDestroy()
    {
        _childWatch?.Dispose();
        base.OnDestroy();
    }

    /// <summary>A scroll view's default list: a Flickable becomes the view, other items its content.</summary>
    public sealed class ScrollViewData(ScrollView owner) : IQmlList
    {
        private readonly List<object?> _all = [];

        public int Count => _all.Count;

        public object? this[int index] => _all[index];

        public Type ElementType => typeof(QObject);

        public event Action? Changed;

        public void Append(object? item)
        {
            _all.Add(item);
            owner.Add(item);
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (var child in owner._flickable.ContentItem.ChildItems.ToArray())
                child.SetParentItem(null);
            _all.Clear();
            Changed?.Invoke();
        }

        public IEnumerator<object?> GetEnumerator() => _all.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>QtQuick.Controls' <c>ScrollIndicator</c>: a thin, non-interactive scroll position line.</summary>
public sealed class ScrollIndicator : Control
{
    private double _size = 1, _position;
    private bool _active;
    private int _orientation = 2;

    public ScrollIndicator()
    {
        SetDefaultPadding(2, 2, 2, 2);
    }

    [Qml] public double Size { get { Track(); return _size; } set { if (SetField(ref _size, value)) Update(); } }

    [Qml] public double Position { get { Track(); return _position; } set { if (SetField(ref _position, value)) Update(); } }

    [Qml] public bool Active { get { Track(); return _active; } set => SetField(ref _active, value); }

    [Qml] public int Orientation { get { Track(); return _orientation; } set { if (SetField(ref _orientation, value)) UpdateImplicitSize(); } }

    [Qml] public double MinimumSize { get; set; }

    protected override Item? CreateDefaultContentItem() => Part(new PaintedItem((c, i, _) =>
    {
        if (_size >= 1)
            return;
        var r = i.Bounds;
        var color = Theme.Current.ControlStrongFillColorDefault;
        var rect = _orientation == 2
            ? SKRect.Create(r.MidX - 1, r.Top + (float)(_position * r.Height), 2, (float)(_size * r.Height))
            : SKRect.Create(r.Left + (float)(_position * r.Width), r.MidY - 1, (float)(_size * r.Width), 2);
        Fluent.FillRound(c, rect, 1, color);
    }, 2, 2));
}
