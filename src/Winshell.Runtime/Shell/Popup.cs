using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Shell;

/// <summary>A popup anchor's <c>rect</c> or <c>margins</c> group.</summary>
public sealed class AnchorRect(PopupAnchor owner) : QObject
{
    private double _x, _y, _width = 1, _height = 1;

    [Qml] public double X { get { Track(); return _x; } set { if (SetField(ref _x, value)) owner.Changed(); } }

    [Qml] public double Y { get { Track(); return _y; } set { if (SetField(ref _y, value)) owner.Changed(); } }

    [Qml]
    public double Width
    {
        get { Track(); return _width; }
        set
        {
            var first = !WidthSet;
            WidthSet = true;
            if (SetField(ref _width, Math.Max(1, value)) || first)
                owner.Changed();
        }
    }

    [Qml]
    public double Height
    {
        get { Track(); return _height; }
        set
        {
            var first = !HeightSet;
            HeightSet = true;
            if (SetField(ref _height, Math.Max(1, value)) || first)
                owner.Changed();
        }
    }

    internal bool WidthSet { get; private set; }

    internal bool HeightSet { get; private set; }
}

public sealed class AnchorMargins(PopupAnchor owner) : QObject
{
    private double _left, _top, _right, _bottom;

    [Qml] public double Left { get { Track(); return _left; } set { if (SetField(ref _left, value)) owner.Changed(); } }

    [Qml] public double Top { get { Track(); return _top; } set { if (SetField(ref _top, value)) owner.Changed(); } }

    [Qml] public double Right { get { Track(); return _right; } set { if (SetField(ref _right, value)) owner.Changed(); } }

    [Qml] public double Bottom { get { Track(); return _bottom; } set { if (SetField(ref _bottom, value)) owner.Changed(); } }
}

/// <summary>The <c>PopupAnchor</c>: where a popup attaches to its parent.</summary>
[QmlSignal("anchoring")]
public sealed class PopupAnchor : QObject
{
    private ShellWindow? _window;
    private Item? _item;
    private int _edges = (int)(Shell.Edges.Top | Shell.Edges.Left);
    private int _gravity = (int)(Shell.Edges.Bottom | Shell.Edges.Right);
    private int _adjustment = (int)PopupAdjustment.All;

    private readonly Action _changed;

    public PopupAnchor(Action changed)
    {
        _changed = changed;
        Rect = new AnchorRect(this);
        Margins = new AnchorMargins(this);
    }

    internal void Changed() => _changed();

    [Qml]
    public ShellWindow? Window
    {
        get { Track(); return _window ?? (_item?.Host?.WindowObject as ShellWindow); }
        set
        {
            _item = null;
            if (SetField(ref _window, value))
                Changed();
        }
    }

    [Qml]
    public Item? Item
    {
        get { Track(); return _item; }
        set
        {
            _window = null;
            if (SetField(ref _item, value))
                Changed();
        }
    }

    [Qml]
    public AnchorRect Rect { get; }

    [Qml]
    public AnchorMargins Margins { get; }

    [Qml] public int Edges { get { Track(); return _edges; } set { if (SetField(ref _edges, value)) Changed(); } }

    [Qml] public int Gravity { get { Track(); return _gravity; } set { if (SetField(ref _gravity, value)) Changed(); } }

    [Qml] public int Adjustment { get { Track(); return _adjustment; } set { if (SetField(ref _adjustment, value)) Changed(); } }

    [Qml]
    public void UpdateAnchor() => Changed();

    /// <summary>The anchor rectangle in its window's coordinates.</summary>
    internal QRect AnchorRectInWindow()
    {
        using var _ = DependencyTracker.Suspend();
        QRect rect;
        if (_item is not null)
        {
            var origin = _item.MapToScene(0, 0);
            var w = Rect.WidthSet ? Rect.Width : _item.Width;
            var h = Rect.HeightSet ? Rect.Height : _item.Height;
            rect = new QRect(origin.X + Rect.X, origin.Y + Rect.Y, w, h);
        }
        else
        {
            rect = new QRect(Rect.X, Rect.Y, Rect.Width, Rect.Height);
        }
        return new QRect(rect.X - Margins.Left, rect.Y - Margins.Top, rect.Width + Margins.Left + Margins.Right, rect.Height + Margins.Top + Margins.Bottom);
    }
}

/// <summary>The <c>PopupWindow</c>: a frameless window placed next to its parent window.</summary>
public sealed class PopupWindow : ShellWindow, IItemContainerChild
{
    private bool _grabFocus;
    private bool _popupVisible;
    private Item? _containerItem;
    private ShellWindow? _watchedParent;
    private IDisposable? _parentVisibility;

    public PopupWindow()
    {
        Anchor = new PopupAnchor(AnchorChanged);
        base.Visible = false;
    }

    protected override WindowKind Kind => WindowKind.Popup;

    [Qml]
    public PopupAnchor Anchor { get; }

    [Qml]
    public ShellWindow? ParentWindow
    {
        get => Anchor.Window;
        set => Anchor.Window = value;
    }

    [Qml]
    public double RelativeX
    {
        get => Anchor.Rect.X;
        set => Anchor.Rect.X = value;
    }

    [Qml]
    public double RelativeY
    {
        get => Anchor.Rect.Y;
        set => Anchor.Rect.Y = value;
    }

    [Qml]
    public bool GrabFocus
    {
        get { Track(); return _grabFocus; }
        set
        {
            if (SetField(ref _grabFocus, value))
                OnPlacementChanged();
        }
    }

    [Qml]
    public override ShellScreen? Screen
    {
        get
        {
            Track();
            var parent = Anchor.Window;
            return parent?.Screen ?? base.Screen;
        }
        set { }
    }

    [Qml]
    public override bool Visible
    {
        get { Track(); return _popupVisible; }
        set
        {
            if (SetField(ref _popupVisible, value))
            {
                if (value)
                    Anchor.Emit("anchoring");
                UpdateVisibility();
            }
        }
    }

    protected override bool ShouldShow => _popupVisible && Anchor.Window is { BackingWindowVisible: true };

    public void AttachToItem(Item item) => _containerItem = item;

    internal void AnchorChanged()
    {
        WatchParent();
        OnPlacementChanged();
        UpdateVisibility();
    }

    /// <summary>One connection to the current parent. A new backend or a new parent replaces it.</summary>
    private void WatchParent()
    {
        var parent = Anchor.Window;
        if (ReferenceEquals(parent, _watchedParent))
            return;
        _parentVisibility?.Dispose();
        _parentVisibility = null;
        _watchedParent = parent;
        if (parent is not null)
            _parentVisibility = parent.Connect("backingWindowVisibleChanged", _ => UpdateVisibility());
    }

    public override void OnDismissed()
    {
        if (_grabFocus && _popupVisible)
            Visible = false;
    }

    protected override void OnPlacementChanged()
    {
        if (Backend is null)
            return;
        var parent = Anchor.Window;
        if (parent is null)
            return;
        using var _ = DependencyTracker.Suspend();
        var origin = parent.GlobalOrigin;
        var anchor = Anchor.AnchorRectInWindow();
        // The parent's origin is in the coordinates of the screen it is on, so the popup is placed and clamped there.
        var screen = parent.ShownOn?.Info;
        var rect = Place(
            new QRect(anchor.X + origin.X, anchor.Y + origin.Y, anchor.Width, anchor.Height),
            ImplicitWidth,
            ImplicitHeight,
            (Edges)Anchor.Edges,
            (Edges)Anchor.Gravity,
            Anchor.Adjustment,
            screen?.WorkArea ?? screen?.Geometry);
        Backend.SetPopup(new PopupPlacement(screen, rect, _grabFocus));
    }

    /// <summary>
    /// Places a popup of the given size at an anchor rectangle, the way xdg-positioner does: the anchor point
    /// comes from the edges, the popup grows toward the gravity, and it flips, slides or shrinks to fit the screen.
    /// </summary>
    public static QRect Place(QRect anchor, double width, double height, Edges edges, Edges gravity, int adjustment, QRect? bounds)
    {
        double AnchorX(Edges e) => (e & Edges.Left) != 0 ? anchor.X : (e & Edges.Right) != 0 ? anchor.Right : anchor.X + anchor.Width / 2;
        double AnchorY(Edges e) => (e & Edges.Top) != 0 ? anchor.Y : (e & Edges.Bottom) != 0 ? anchor.Bottom : anchor.Y + anchor.Height / 2;
        double PlaceX(Edges e, Edges g) => (g & Edges.Left) != 0 ? AnchorX(e) - width : (g & Edges.Right) != 0 ? AnchorX(e) : AnchorX(e) - width / 2;
        double PlaceY(Edges e, Edges g) => (g & Edges.Top) != 0 ? AnchorY(e) - height : (g & Edges.Bottom) != 0 ? AnchorY(e) : AnchorY(e) - height / 2;
        static Edges FlipH(Edges e) => (e & ~(Edges.Left | Edges.Right)) | ((e & Edges.Left) != 0 ? Edges.Right : 0) | ((e & Edges.Right) != 0 ? Edges.Left : 0);
        static Edges FlipV(Edges e) => (e & ~(Edges.Top | Edges.Bottom)) | ((e & Edges.Top) != 0 ? Edges.Bottom : 0) | ((e & Edges.Bottom) != 0 ? Edges.Top : 0);

        var x = PlaceX(edges, gravity);
        var y = PlaceY(edges, gravity);
        var w = width;
        var h = height;
        if (bounds is { } b)
        {
            if ((adjustment & (int)PopupAdjustment.FlipX) != 0 && (x < b.X || x + w > b.Right))
            {
                var fx = PlaceX(FlipH(edges), FlipH(gravity));
                if (fx >= b.X && fx + w <= b.Right)
                    x = fx;
            }
            if ((adjustment & (int)PopupAdjustment.FlipY) != 0 && (y < b.Y || y + h > b.Bottom))
            {
                var fy = PlaceY(FlipV(edges), FlipV(gravity));
                if (fy >= b.Y && fy + h <= b.Bottom)
                    y = fy;
            }
            if ((adjustment & (int)PopupAdjustment.SlideX) != 0)
                x = Math.Max(b.X, Math.Min(x, b.Right - w));
            if ((adjustment & (int)PopupAdjustment.SlideY) != 0)
                y = Math.Max(b.Y, Math.Min(y, b.Bottom - h));
            if ((adjustment & (int)PopupAdjustment.ResizeX) != 0 && (x < b.X || x + w > b.Right))
            {
                var l = Math.Max(x, b.X);
                w = Math.Max(1, Math.Min(x + w, b.Right) - l);
                x = l;
            }
            if ((adjustment & (int)PopupAdjustment.ResizeY) != 0 && (y < b.Y || y + h > b.Bottom))
            {
                var t = Math.Max(y, b.Y);
                h = Math.Max(1, Math.Min(y + h, b.Bottom) - t);
                y = t;
            }
        }
        return new QRect(Math.Round(x), Math.Round(y), Math.Round(w), Math.Round(h));
    }

    protected override void OnBackendCreated()
    {
        base.OnBackendCreated();
        WatchParent();
    }

    protected override void OnDestroy()
    {
        _parentVisibility?.Dispose();
        _parentVisibility = null;
        _watchedParent = null;
        base.OnDestroy();
    }
}

/// <summary>The attached <c>ShellWindow</c> object of an item: its window and content item.</summary>
public sealed class ShellWindowAttached(QObject owner) : QObject
{
    private Item? OwnerItem => owner as Item;

    [Qml]
    public ShellWindow? Window
    {
        get
        {
            Track();
            return OwnerItem?.Host?.WindowObject as ShellWindow;
        }
    }

    [Qml]
    public Item? ContentItem => Window?.ContentItem;

    [Qml]
    public QPoint ItemPosition(Item item) => Window?.ItemPosition(item) ?? new QPoint(0, 0);

    [Qml]
    public QRect ItemRect(Item item) => Window?.ItemRect(item) ?? new QRect(0, 0, 0, 0);

    [Qml]
    public object? MapFromItem(Item item, object? xOrPoint, double y = double.NaN, double width = double.NaN, double height = double.NaN) =>
        Window?.MapFromItem(item, xOrPoint, y, width, height);
}

/// <summary>The <c>PopupAdjustment</c> values, for <c>PopupAnchor.adjustment</c>. Combine them with <c>|</c>.</summary>
[Flags]
public enum PopupAdjustment
{
    None = 0,
    SlideX = 1,
    SlideY = 2,
    Slide = SlideX | SlideY,
    FlipX = 4,
    FlipY = 8,
    Flip = FlipX | FlipY,
    ResizeX = 16,
    ResizeY = 32,
    Resize = ResizeX | ResizeY,
    All = Slide | Flip | Resize,
}
