using Jint;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>
/// The layer over a window's items that popups are shown in. A press outside every open popup reaches it
/// first: it closes the popups that close on an outside press, and lets the press through unless one is modal.
/// </summary>
public sealed class PopupOverlay : Item, IPointerTarget, IWheelTarget
{
    private static readonly ConditionalWeakTable<Item, PopupOverlay> Overlays = new();
    private readonly Item _root;
    private readonly PropertyWatch _rootWatch;
    private readonly List<Popup> _open = [];

    private PopupOverlay(Item root)
    {
        _root = root;
        Z = 1_000_000;
        SetParentItem(root);
        _rootWatch = new PropertyWatch(root, Fit, "width", "height");
        Fit();
    }

    /// <summary>The overlay of the window an item is in: the top of its tree.</summary>
    public static PopupOverlay For(Item item)
    {
        var root = item;
        while (root.ParentItem is { } p)
        {
            if (p is PopupOverlay overlay)
                return overlay;
            root = p;
        }
        if (root is PopupOverlay self)
            return self;
        return Overlays.GetValue(root, r => new PopupOverlay(r) { Engine = r.Engine });
    }

    /// <summary>The popup layers of the items a window shows.</summary>
    public static IEnumerable<PopupOverlay> Of(IItemHost host) =>
        Overlays.Select(entry => entry.Value).Where(overlay => overlay.Host == host && !overlay.IsDestroyed);

    /// <summary>The open popups of a window, the topmost last.</summary>
    public static IEnumerable<Popup> OpenIn(IItemHost host) => Of(host).SelectMany(overlay => overlay.OpenPopups);

    /// <summary>The popups closed by the press being delivered now, so the control that opened one does not reopen it.</summary>
    internal static HashSet<Popup> ClosedByPress { get; } = [];

    /// <summary>Where the pointer was last pressed, in the overlay's (the window's) coordinates.</summary>
    public QPoint LastPress { get; private set; }

    public IReadOnlyList<Popup> OpenPopups => _open;

    private void Fit()
    {
        using var _ = DependencyTracker.Suspend();
        SetLayoutX(0);
        SetLayoutY(0);
        SetLayoutWidth(_root.Width);
        SetLayoutHeight(_root.Height);
        foreach (var popup in _open.ToArray())
            popup.Reposition();
    }

    internal void Add(Popup popup)
    {
        _open.Remove(popup);
        _open.Add(popup);
        Update();
    }

    internal void Remove(Popup popup)
    {
        _open.Remove(popup);
        Update();
    }

    public override bool HitTest(double x, double y)
    {
        LastPress = new QPoint(x, y);
        return _open.Count > 0 && base.HitTest(x, y);
    }

    public bool AcceptsButton(int button) => _open.Count > 0;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        ClosedByPress.Clear();
        var modal = _open.Any(p => p.Modal);
        foreach (var popup in _open.ToArray().Reverse())
        {
            var policy = popup.ClosePolicy;
            var outsideParent = popup.ParentItem is not { } parent || !Contains(parent, x, y);
            if ((policy & (int)Popup.ClosePolicyValue.CloseOnPressOutside) != 0
                || ((policy & (int)Popup.ClosePolicyValue.CloseOnPressOutsideParent) != 0 && outsideParent))
            {
                ClosedByPress.Add(popup);
                popup.Close();
            }
            if (popup.Modal)
                break;
        }
        return modal;
    }

    private bool Contains(Item item, double x, double y)
    {
        var local = item.MapFromItem(this, x, y);
        return local is QPoint p && p.X >= 0 && p.Y >= 0 && p.X < item.Width && p.Y < item.Height;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel()
    {
    }

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted) =>
        _open.Any(p => p.Modal);

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        var dim = _open.LastOrDefault(p => p.Modal && p.Dim) ?? _open.LastOrDefault(p => p.DimSet && p.Dim);
        if (dim is null)
            return;
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, 0x4d) };
        canvas.DrawRect(SKRect.Create(0, 0, (float)Width, (float)Height), paint);
    }

    protected override void OnDestroy()
    {
        _rootWatch.Dispose();
        Overlays.Remove(_root);
        base.OnDestroy();
    }
}

/// <summary>The attached <c>Overlay.overlay</c>: the window's popup layer.</summary>
public sealed class OverlayAttached(QObject owner) : QObject
{
    [Qml]
    public Item? Overlay => owner is Item item ? PopupOverlay.For(item) : null;
}

/// <summary>The item a popup shows as: a pane in the window's overlay.</summary>
public sealed class PopupItem : Pane, IPointerTarget, IWheelTarget, IKeyTarget
{
    private readonly Popup _popup;

    private readonly bool _ownContent;

    internal PopupItem(Popup popup)
    {
        _popup = popup;
        // WinUI's FlyoutContentPadding, 16,15,16,17, inside a 1 pixel border.
        SetDefaultPadding(17, 16, 17, 18);
        if (popup.CreateContentItem() is { } content)
        {
            InitContentItem(content);
            _ownContent = true;
        }
    }

    // A popup that brings its own content item, such as a menu's column or a tool tip's label, is as big as that
    // item. Other popups are as big as their one child, as a pane is.

    public override double ImplicitContentWidth => _ownContent ? OwnContentSize(true) : base.ImplicitContentWidth;

    public override double ImplicitContentHeight => _ownContent ? OwnContentSize(false) : base.ImplicitContentHeight;

    private double OwnContentSize(bool width)
    {
        TrackQml(width ? "implicitContentWidth" : "implicitContentHeight");
        return width ? ContentItem?.ImplicitWidth ?? 0 : ContentItem?.ImplicitHeight ?? 0;
    }

    public Popup Popup => _popup;

    protected override Item? CreateDefaultBackground() => _popup.CreateDefaultBackground();

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        _popup.OnItemGeometryChanged();
    }

    public override bool HitTest(double x, double y) => _popup.Interactive && base.HitTest(x, y);

    public override bool AcceptsHover => _popup.Interactive && base.AcceptsHover;

    public bool AcceptsButton(int button) => _popup.Interactive;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers) => true;

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers)
    {
    }

    public void PointerCancel()
    {
    }

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted) => _popup.Interactive;

    public bool KeyPress(KeyEvent e)
    {
        if (e.Key == 0x01000000 && (_popup.ClosePolicy & (int)Popup.ClosePolicyValue.CloseOnEscape) != 0)
        {
            _popup.Close();
            return true;
        }
        // A modal popup keeps keys from the window under it, but Tab and the arrow keys still move focus inside it.
        return _popup.Modal && e.Key is not (0x01000001 or 0x01000002 or 0x01000012 or 0x01000013 or 0x01000014 or 0x01000015);
    }

    public bool KeyRelease(KeyEvent e) => false;

    internal void Complete() => Part(this);
}

/// <summary>The anchors of a popup: only <c>centerIn</c>.</summary>
public sealed class PopupAnchors(Popup popup) : QObject
{
    private Item? _centerIn;

    [Qml]
    public Item? CenterIn
    {
        get { Track(); return _centerIn; }
        set
        {
            if (SetField(ref _centerIn, value))
                popup.Reposition();
        }
    }
}

/// <summary>
/// QtQuick.Controls' <c>Popup</c>. A popup is not a window: it is shown in the overlay of the window its parent
/// item is in, above every other item, at a position relative to its parent.
/// </summary>
[QmlDefaultProperty("contentData")]
[QmlSignal("aboutToShow")]
[QmlSignal("aboutToHide")]
[QmlSignal("opened")]
[QmlSignal("closed")]
public class Popup : QObject, IItemContainerChild, IPropertyObserver
{
    private readonly PopupItem _item;
    private Item? _parent;
    private bool _parentSet;
    private double _x, _y;
    private bool _visible;
    private bool _opened;
    private bool _modal;
    private bool? _dim;
    private int _closePolicy = (int)(ClosePolicyValue.CloseOnEscape | ClosePolicyValue.CloseOnPressOutside);
    private double _margins = -1;
    private double? _topMargin, _bottomMargin, _leftMargin, _rightMargin;
    private bool _repositioning;
    private bool _focus;
    private PopupOverlay? _overlay;
    private Item? _restoreFocus;

    private static readonly string[] Forwarded =
    [
        "width", "height", "implicitWidth", "implicitHeight", "padding", "horizontalPadding", "verticalPadding", "leftPadding",
        "rightPadding", "topPadding", "bottomPadding", "topInset", "bottomInset", "leftInset", "rightInset", "background",
        "contentItem", "contentWidth", "contentHeight", "availableWidth", "availableHeight", "implicitContentWidth",
        "implicitContentHeight", "implicitBackgroundWidth", "implicitBackgroundHeight", "opacity", "scale", "z", "spacing",
        "enabled", "activeFocus", "hovered", "font",
    ];

    [Flags]
    [QmlEnum]
    public enum ClosePolicyValue
    {
        NoAutoClose = 0,
        CloseOnPressOutside = 1,
        CloseOnPressOutsideParent = 2,
        CloseOnReleaseOutside = 4,
        CloseOnReleaseOutsideParent = 8,
        CloseOnEscape = 16,
    }

    [QmlEnum]
    public enum PopupTypeValue
    {
        Item = 0,
        Window = 1,
        Native = 2,
    }

    public Popup()
    {
        _item = new PopupItem(this);
        _item.SetOwner(this);
        foreach (var name in Forwarded)
            _item.AddObserver(name, this);
        Anchors = new PopupAnchors(this);
    }

    /// <summary>The item the popup shows as.</summary>
    public PopupItem PopupItem => _item;

    void IPropertyObserver.OnPropertyChanged(QObject sender, string property) => NotifyChanged(property);

    internal virtual Item? CreateDefaultBackground() => PaintedItemPart((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        Fluent.Shadow(c, r, Fluent.OverlayCornerRadius, t.FlyoutShadow);
        Fluent.FillRound(c, r, Fluent.OverlayCornerRadius, FluentResources.FlyoutPresenterBackground(t));
        Fluent.StrokeRound(c, r, Fluent.OverlayCornerRadius, FluentResources.FlyoutPresenterBorderBrush(t), FluentResources.FlyoutBorderThickness(t));
    });

    /// <summary>The content item the popup starts with, or null for a plain item.</summary>
    internal virtual Item? CreateContentItem() => null;

    protected static PaintedItem PaintedItemPart(Action<SKCanvas, PaintedItem, PaintContext> paint, double w = 0, double h = 0)
    {
        var item = new PaintedItem(paint, w, h);
        item.MarkComplete();
        return item;
    }

    /// <summary>Whether the popup takes the pointer. A tool tip does not.</summary>
    public virtual bool Interactive => true;

    // The parent and position

    public Item? ParentItem => _parent;

    [Qml]
    public Item? Parent
    {
        get { Track(); return _parent; }
        set
        {
            _parentSet = true;
            if (SetField(ref _parent, value))
                Reposition();
        }
    }

    void IItemContainerChild.AttachToItem(Item item)
    {
        if (!_parentSet)
        {
            _parent = item;
            NotifyChanged("parent");
        }
    }

    [Qml] public double X { get { Track(); return _x; } set { if (SetField(ref _x, value)) Reposition(); } }

    [Qml] public double Y { get { Track(); return _y; } set { if (SetField(ref _y, value)) Reposition(); } }

    [Qml] public double Z { get => _item.Z; set => _item.Z = value; }

    [Qml] public double Width { get { Track(); return _item.Width; } set => _item.Width = value; }

    [Qml] public double Height { get { Track(); return _item.Height; } set => _item.Height = value; }

    [Qml] public double ImplicitWidth { get { Track(); return _item.ImplicitWidth; } set => _item.ImplicitWidth = value; }

    [Qml] public double ImplicitHeight { get { Track(); return _item.ImplicitHeight; } set => _item.ImplicitHeight = value; }

    [Qml] public PopupAnchors Anchors { get; }

    [Qml] public double Margins { get { Track(); return _margins; } set { if (SetField(ref _margins, value)) Reposition(); } }

    [Qml] public double TopMargin { get { Track(); return _topMargin ?? _margins; } set { _topMargin = value; NotifyChanged("topMargin"); Reposition(); } }

    [Qml] public double BottomMargin { get { Track(); return _bottomMargin ?? _margins; } set { _bottomMargin = value; NotifyChanged("bottomMargin"); Reposition(); } }

    [Qml] public double LeftMargin { get { Track(); return _leftMargin ?? _margins; } set { _leftMargin = value; NotifyChanged("leftMargin"); Reposition(); } }

    [Qml] public double RightMargin { get { Track(); return _rightMargin ?? _margins; } set { _rightMargin = value; NotifyChanged("rightMargin"); Reposition(); } }

    /// <summary>Sets a margin default from C#, such as a menu's.</summary>
    protected void SetDefaultMargins(double margins) => _margins = margins;

    // Padding, parts and content, kept by the popup item

    [Qml] public double Padding { get { Track(); return _item.Padding; } set => _item.Padding = value; }

    [Qml] public double HorizontalPadding { get { Track(); return _item.HorizontalPadding; } set => _item.HorizontalPadding = value; }

    [Qml] public double VerticalPadding { get { Track(); return _item.VerticalPadding; } set => _item.VerticalPadding = value; }

    [Qml] public double LeftPadding { get { Track(); return _item.LeftPadding; } set => _item.LeftPadding = value; }

    [Qml] public double RightPadding { get { Track(); return _item.RightPadding; } set => _item.RightPadding = value; }

    [Qml] public double TopPadding { get { Track(); return _item.TopPadding; } set => _item.TopPadding = value; }

    [Qml] public double BottomPadding { get { Track(); return _item.BottomPadding; } set => _item.BottomPadding = value; }

    [Qml] public double TopInset { get { Track(); return _item.TopInset; } set => _item.TopInset = value; }

    [Qml] public double BottomInset { get { Track(); return _item.BottomInset; } set => _item.BottomInset = value; }

    [Qml] public double LeftInset { get { Track(); return _item.LeftInset; } set => _item.LeftInset = value; }

    [Qml] public double RightInset { get { Track(); return _item.RightInset; } set => _item.RightInset = value; }

    [Qml] public double Spacing { get { Track(); return _item.Spacing; } set => _item.Spacing = value; }

    [Qml] public Item? Background { get { Track(); return _item.Background; } set => _item.Background = value; }

    [Qml] public Item? ContentItem { get { Track(); return _item.ContentItem; } set => _item.ContentItem = value; }

    [Qml] public ItemData ContentData => _item.ContentData;

    [Qml] public QmlList<Item> ContentChildren => _item.ContentChildren;

    [Qml] public double ContentWidth { get { Track(); return _item.ContentWidth; } set => _item.ContentWidth = value; }

    [Qml] public double ContentHeight { get { Track(); return _item.ContentHeight; } set => _item.ContentHeight = value; }

    [Qml] public double AvailableWidth { get { Track(); return _item.AvailableWidth; } }

    [Qml] public double AvailableHeight { get { Track(); return _item.AvailableHeight; } }

    [Qml] public double ImplicitContentWidth { get { Track(); return _item.ImplicitContentWidth; } }

    [Qml] public double ImplicitContentHeight { get { Track(); return _item.ImplicitContentHeight; } }

    [Qml] public double ImplicitBackgroundWidth { get { Track(); return _item.ImplicitBackgroundWidth; } }

    [Qml] public double ImplicitBackgroundHeight { get { Track(); return _item.ImplicitBackgroundHeight; } }

    [Qml] public double Opacity { get { Track(); return _item.Opacity; } set => _item.Opacity = value; }

    [Qml] public double Scale { get { Track(); return _item.Scale; } set => _item.Scale = value; }

    [Qml] public bool Enabled { get { Track(); return _item.Enabled; } set => _item.Enabled = value; }

    [Qml] public bool Hovered { get { Track(); return _item.Hovered; } }

    [Qml] public FontGroup Font { get => _item.Font; set => _item.Font = value; }

    [Qml] public Palette Palette => _item.Palette;

    [Qml] public bool Focus { get { Track(); return _focus; } set => SetField(ref _focus, value); }

    [Qml] public bool ActiveFocus { get { Track(); return _item.ActiveFocus; } }

    [Qml] public QObject? Enter { get; set; }

    [Qml] public QObject? Exit { get; set; }

    [Qml] public int PopupType { get; set; }

    [Qml] public bool Mirrored => false;

    [Qml] public object? Locale { get; set; }

    // Behavior

    [Qml] public bool Modal { get { Track(); return _modal; } set { if (SetField(ref _modal, value)) NotifyChanged("dim"); } }

    [Qml] public bool Dim { get { Track(); return _dim ?? _modal; } set { _dim = value; NotifyChanged("dim"); _overlay?.Update(); } }

    internal bool DimSet => _dim is not null;

    [Qml] public int ClosePolicy { get { Track(); return _closePolicy; } set => SetField(ref _closePolicy, value); }

    [Qml]
    public bool Visible
    {
        get { Track(); return _visible; }
        set
        {
            if (_visible == value)
                return;
            _visible = value;
            NotifyChanged("visible");
            if (IsComplete || Engine is null)
                Apply();
        }
    }

    [Qml] public bool Opened { get { Track(); return _opened; } }

    [Qml]
    public void Open() => Visible = true;

    [Qml]
    public void Close() => Visible = false;

    [Qml]
    public void ForceActiveFocus() => _item.ForceActiveFocus();

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        _item.Complete();
        if (_visible)
            Apply();
    }

    /// <summary>Completes a popup made from C#, as the engine does for one made from QML.</summary>
    internal void CompleteFromCode()
    {
        if (!IsComplete)
            MarkComplete();
    }

    private void Apply()
    {
        if (_visible == _opened)
            return;
        if (_visible)
        {
            if (_parent is null)
            {
                _visible = false;
                NotifyChanged("visible");
                return;
            }
            Emit("aboutToShow");
            OnAboutToShow();
            _overlay = PopupOverlay.For(_parent);
            _item.Visible = true;
            _item.SetParentItem(_overlay);
            _overlay.Add(this);
            _opened = true;
            Reposition();
            // A modal popup takes focus however it was opened, so keys do not fall through to the
            // field under it. A flyout opened from the keyboard does the same, and gives focus back
            // when it closes. focus: true focuses the popup item itself.
            var scene = _item.Host as Scene;
            _restoreFocus = scene?.FocusItem;
            if (_modal || scene is { FocusVisible: true })
                ItemToFocusOnOpen().ForceActiveFocus();
            else if (_focus)
                _item.ForceActiveFocus();
            NotifyChanged("opened");
            Emit("opened");
        }
        else
        {
            Emit("aboutToHide");
            _overlay?.Remove(this);
            var host = _item.Host;
            var hadFocus = false;
            for (var f = host?.FocusItem; f is not null && !hadFocus; f = f.ParentItem)
                hadFocus = f == _item;
            if (hadFocus)
                host!.SetFocusItem(null);
            _item.SetParentItem(null);
            if (hadFocus && _restoreFocus is { IsDestroyed: false } restore && restore.Host == host && restore.Visible)
                restore.ForceActiveFocus();
            _restoreFocus = null;
            _overlay = null;
            _opened = false;
            NotifyChanged("opened");
            Emit("closed");
            OnClosed();
        }
    }

    /// <summary>The item that receives focus when the popup opens from the keyboard, or because it is modal.</summary>
    protected virtual Item ItemToFocusOnOpen() => FocusNavigation.TabStops(_item).FirstOrDefault() ?? _item;

    protected virtual void OnAboutToShow()
    {
    }

    protected virtual void OnClosed()
    {
    }

    internal void OnItemGeometryChanged()
    {
        if (!_repositioning)
            Reposition();
    }

    /// <summary>Where the popup wants to be, in its parent's coordinates, before it is kept inside the window.</summary>
    protected virtual QPoint RequestedPosition() => new(_x, _y);

    /// <summary>Places the popup item in the overlay: at its position relative to its parent, kept inside the window.</summary>
    public void Reposition()
    {
        if (!_opened || _overlay is null || _repositioning)
            return;
        _repositioning = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            double x, y;
            if (Anchors.CenterIn is { } center)
            {
                var c = (QPoint)_overlay.MapFromItem(center, center.Width / 2, center.Height / 2);
                x = c.X - _item.Width / 2;
                y = c.Y - _item.Height / 2;
            }
            else
            {
                var requested = RequestedPosition();
                var p = _parent is null ? requested : (QPoint)_overlay.MapFromItem(_parent, requested.X, requested.Y);
                x = p.X;
                y = p.Y;
            }
            (x, y) = KeepInside(x, y);
            _item.SetLayoutX(Math.Round(x));
            _item.SetLayoutY(Math.Round(y));
        }
        finally
        {
            _repositioning = false;
        }
    }

    /// <summary>Moves a position so the popup stays inside the window, by its margins. Margins of -1 mean no limit.</summary>
    protected virtual (double X, double Y) KeepInside(double x, double y)
    {
        if (_overlay is null)
            return (x, y);
        var w = _item.Width;
        var h = _item.Height;
        if (LeftMargin >= 0 && RightMargin >= 0)
            x = Math.Max(LeftMargin, Math.Min(x, _overlay.Width - RightMargin - w));
        if (TopMargin >= 0 && BottomMargin >= 0)
            y = Math.Max(TopMargin, Math.Min(y, _overlay.Height - BottomMargin - h));
        return (x, y);
    }

    public PopupOverlay? Overlay => _overlay;

    protected override void OnDestroy()
    {
        if (_opened)
        {
            _overlay?.Remove(this);
            _item.SetParentItem(null);
        }
        foreach (var name in Forwarded)
            _item.RemoveObserver(name, this);
        _item.Destroy();
        base.OnDestroy();
    }
}

/// <summary>QtQuick.Controls' <c>ToolTip</c>: a short text shown above an item.</summary>
public sealed class ToolTip : Popup
{
    private string _text = "";
    private int _delay;
    private int _timeout = -1;
    private IDisposable? _delayTimer, _timeoutTimer;
    private Quick.Text? _label;

    public ToolTip()
    {
        // WinUI's ToolTipBorderThemePadding, 8,5,8,7, inside a 1 pixel border.
        Padding = 9;
        TopPadding = 6;
        BottomPadding = 8;
        SetDefaultMargins(6);
        ClosePolicy = (int)(ClosePolicyValue.CloseOnEscape | ClosePolicyValue.CloseOnPressOutsideParent | ClosePolicyValue.CloseOnReleaseOutsideParent);
    }

    public override bool Interactive => false;

    [Qml]
    public string Text
    {
        get { Track(); return _text; }
        set
        {
            if (SetField(ref _text, value ?? "") && _label is not null)
                _label.Text_ = _text;
        }
    }

    [Qml] public int Delay { get { Track(); return _delay; } set => SetField(ref _delay, value); }

    [Qml] public int Timeout { get { Track(); return _timeout; } set { if (SetField(ref _timeout, value) && Opened) StartTimeout(); } }

    internal override Item? CreateDefaultBackground() => PaintedItemPart((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        Fluent.Shadow(c, r, Fluent.ControlCornerRadius, t.TooltipShadow);
        Fluent.FillRound(c, r, Fluent.ControlCornerRadius, FluentResources.ToolTipBackground(t));
        Fluent.StrokeRound(c, r, Fluent.ControlCornerRadius, FluentResources.ToolTipBorderBrush(t));
    });

    internal override Item? CreateContentItem()
    {
        _label = new Quick.Text { WrapMode = (int)Quick.Text.WrapModeValue.Wrap };
        _label.Font.PixelSize = 12;
        _label.MarkComplete();
        return new ToolTipContent(_label);
    }

    /// <summary>Holds the label, wrapping it at the width WinUI tool tips wrap at.</summary>
    private sealed class ToolTipContent : Item
    {
        private readonly Quick.Text _label;
        private readonly PropertyWatch _watch;

        public ToolTipContent(Quick.Text label)
        {
            _label = label;
            label.SetParentItem(this);
            _watch = new PropertyWatch(label, Fit, "implicitWidth", "implicitHeight");
            MarkComplete();
            Fit();
        }

        private void Fit()
        {
            using var _ = DependencyTracker.Suspend();
            // WinUI's ToolTipMaxWidth of 320, less the padding and border on each side.
            const double max = 320 - 18;
            if (_label.ImplicitWidth > max && !_label.HasExplicitWidth)
                _label.Width = max;
            ImplicitWidth = Math.Min(_label.ImplicitWidth, max);
            ImplicitHeight = _label.ImplicitHeight;
        }

        protected override void OnGeometryChanged()
        {
            base.OnGeometryChanged();
            _label.SetLayoutWidth(Width);
        }

        protected override void OnDestroy()
        {
            _watch.Dispose();
            base.OnDestroy();
        }
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        if (_label is not null)
            _label.Text_ = _text;
    }

    protected override QPoint RequestedPosition()
    {
        var parent = ParentItem;
        if (parent is null)
            return new QPoint(X, Y);
        // Centered above the parent, unless QML placed it.
        var x = GetBinding("x") is not null || X != 0 ? X : (parent.Width - Width) / 2;
        var y = GetBinding("y") is not null || Y != 0 ? Y : -Height - 4;
        return new QPoint(x, y);
    }

    protected override (double X, double Y) KeepInside(double x, double y)
    {
        // Flips below the parent when it does not fit above it.
        if (Overlay is { } overlay && ParentItem is { } parent && y < TopMargin && GetBinding("y") is null)
        {
            var below = (QPoint)overlay.MapFromItem(parent, 0, parent.Height + 4);
            if (below.Y + Height <= overlay.Height - BottomMargin)
                y = below.Y;
        }
        return base.KeepInside(x, y);
    }

    [Qml]
    public void Show(string text, int timeout = -1)
    {
        Text = text;
        if (timeout >= 0)
            Timeout = timeout;
        Open();
    }

    [Qml]
    public void Hide() => Close();

    /// <summary>Opens after the delay, as the attached ToolTip does when its item becomes hovered.</summary>
    internal void ShowDelayed()
    {
        _delayTimer?.Dispose();
        if (_delay <= 0 || Engine is null)
        {
            Open();
            return;
        }
        _delayTimer = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(_delay), () =>
        {
            if (!IsDestroyed)
                Open();
        });
    }

    internal void CancelDelayed()
    {
        _delayTimer?.Dispose();
        _delayTimer = null;
        Close();
    }

    protected override void OnAboutToShow() => StartTimeout();

    private void StartTimeout()
    {
        _timeoutTimer?.Dispose();
        if (_timeout > 0 && Engine is not null)
            _timeoutTimer = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(_timeout), Close);
    }

    protected override void OnClosed() => _timeoutTimer?.Dispose();

    protected override void OnDestroy()
    {
        _delayTimer?.Dispose();
        _timeoutTimer?.Dispose();
        base.OnDestroy();
    }
}

/// <summary>The attached <c>ToolTip</c>: <c>ToolTip.visible: hovered; ToolTip.text: "..."</c> on any item.</summary>
public sealed class ToolTipAttached(QObject owner) : QObject, IDestructionListener
{
    private string _text = "";
    private int _delay;
    private int _timeout = -1;
    private bool _visible;
    private ToolTip? _toolTip;

    [Qml]
    public string Text
    {
        get { Track(); return _text; }
        set
        {
            if (SetField(ref _text, value ?? "") && _toolTip is not null)
                _toolTip.Text = _text;
        }
    }

    [Qml] public int Delay { get { Track(); return _delay; } set { if (SetField(ref _delay, value) && _toolTip is not null) _toolTip.Delay = value; } }

    [Qml] public int Timeout { get { Track(); return _timeout; } set { if (SetField(ref _timeout, value) && _toolTip is not null) _toolTip.Timeout = value; } }

    [Qml]
    public bool Visible
    {
        get { Track(); return _visible; }
        set
        {
            if (!SetField(ref _visible, value))
                return;
            if (value)
                Tip()?.ShowDelayed();
            else
                _toolTip?.CancelDelayed();
        }
    }

    [Qml("toolTip")] public ToolTip? ToolTip_ => Tip();

    private ToolTip? Tip()
    {
        if (owner is not Item item)
            return null;
        if (_toolTip is null)
        {
            _toolTip = new ToolTip { Engine = owner.Engine ?? Engine, Parent = item, Text = _text, Delay = _delay, Timeout = _timeout };
            _toolTip.SetOwner(this);
            _toolTip.CompleteFromCode();
        }
        return _toolTip;
    }

    [Qml]
    public void Show(string text, int timeout = -1)
    {
        Text = text;
        if (timeout >= 0)
            Timeout = timeout;
        Tip()?.Open();
    }

    [Qml]
    public void Hide() => _toolTip?.Close();

    public void OnDestroying() => _toolTip?.Destroy();
}

/// <summary>Arranges a menu's items in a column, each as wide as the widest.</summary>
internal sealed class MenuColumn : Item
{
    private readonly Menu _menu;
    private readonly Dictionary<Item, PropertyWatch> _watches = [];
    private bool _arranging;

    public MenuColumn(Menu menu)
    {
        _menu = menu;
        MarkComplete();
    }

    protected override void OnChildAdded(Item child)
    {
        base.OnChildAdded(child);
        if (child is MenuItem item)
            item.Menu = _menu;
        _watches[child] = new PropertyWatch(child, Arrange, "implicitWidth", "implicitHeight", "visible");
        Arrange();
    }

    protected override void OnChildRemoved(Item child)
    {
        base.OnChildRemoved(child);
        if (child is MenuItem { } item && item.Menu == _menu)
            item.Menu = null;
        if (_watches.Remove(child, out var w))
            w.Dispose();
        Arrange();
    }

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        Arrange();
    }

    public void Arrange()
    {
        if (_arranging)
            return;
        _arranging = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            var items = ChildItems.Where(c => c.IsVisibleFlag).ToList();
            var width = items.Count == 0 ? 0 : items.Max(c => c.ImplicitWidth);
            ImplicitWidth = Math.Max(width, 120);
            var y = 0.0;
            foreach (var child in items)
            {
                child.SetLayoutX(0);
                child.SetLayoutY(y);
                if (!child.HasExplicitWidth)
                    child.SetLayoutWidth(Width > 0 ? Width : ImplicitWidth);
                if (!child.HasExplicitHeight)
                    child.SetLayoutHeight(child.ImplicitHeight);
                y += child.Height;
            }
            ImplicitHeight = y;
        }
        finally
        {
            _arranging = false;
        }
    }

    protected override void OnDestroy()
    {
        foreach (var w in _watches.Values)
            w.Dispose();
        base.OnDestroy();
    }
}

/// <summary>QtQuick.Controls' <c>Menu</c>: a popup list of menu items, opened at the pointer.</summary>
public sealed class Menu : Popup
{
    private string _title = "";
    private MenuColumn? _column;

    public Menu()
    {
        // WinUI's MenuFlyoutPresenterThemePadding, 0,2,0,2, inside a 1 pixel border.
        Padding = 1;
        TopPadding = 3;
        BottomPadding = 3;
        SetDefaultMargins(0);
    }

    internal override Item? CreateContentItem() => _column = new MenuColumn(this);

    [Qml] public string Title { get { Track(); return _title; } set => SetField(ref _title, value ?? ""); }

    [Qml] public int Count { get { Track("contentChildren"); return Items.Count; } }

    [Qml] public bool Cascade { get; set; }

    [Qml] public double Overlap { get; set; }

    [Qml] public QmlComponent? Delegate { get; set; }

    [Qml] public int CurrentIndex { get; set; } = -1;

    private IReadOnlyList<Item> Items => _column?.ChildItems ?? [];

    [Qml] public Item? ItemAt(int index) => index >= 0 && index < Items.Count ? Items[index] : null;

    [Qml] public void AddItem(Item item) => item.SetParentItem(_column);

    [Qml]
    public void InsertItem(int index, Item item)
    {
        if (_column is null)
            return;
        item.SetParentItem(_column);
        var from = _column.Children.IndexOf(item);
        index = Math.Clamp(index, 0, _column.Children.Count - 1);
        if (from != index)
            _column.Children.Move(from, index);
        _column.Arrange();
    }

    [Qml]
    public void RemoveItem(Item item)
    {
        if (item.ParentItem == _column)
            item.Destroy();
    }

    [Qml]
    public Item? TakeItem(int index)
    {
        var item = ItemAt(index);
        item?.SetParentItem(null);
        return item;
    }

    [Qml] public void Dismiss() => Close();

    /// <summary>
    /// Opens the menu: at the pointer with no arguments, at a point with (x, y), or at a point in an item with
    /// (item, x, y).
    /// </summary>
    [Qml]
    public void Popup(params object?[] args)
    {
        var values = args.Select(ValueConverter.Unwrap).ToList();
        Item? relative = null;
        if (values.Count > 0 && values[0] is Item first)
        {
            relative = first;
            values.RemoveAt(0);
        }
        if (values.Count > 0 && values[0] is MenuItem)
            values.RemoveAt(0);
        QPoint? point = values.Count switch
        {
            >= 2 when values[0] is double x && values[1] is double y => new QPoint(x, y),
            >= 1 when values[0] is QPoint p => p,
            _ => null,
        };
        var parent = ParentItem;
        if (relative is not null && parent is null)
            Parent = relative;
        parent = ParentItem;
        if (parent is null)
            return;
        QPoint at;
        if (point is { } given)
        {
            at = relative is null || relative == parent ? given : (QPoint)parent.MapFromItem(relative, given.X, given.Y);
        }
        else
        {
            at = PointerIn(parent);
        }
        X = at.X;
        Y = at.Y;
        Open();
        Reposition();
    }

    /// <summary>The last pointer position over an item, in its coordinates.</summary>
    private static QPoint PointerIn(Item item)
    {
        if (item is MouseArea area)
            return new QPoint(area.MouseX, area.MouseY);
        if (item is Control control)
            return control.LastPointer;
        var overlay = PopupOverlay.For(item);
        return (QPoint)item.MapFromItem(overlay, overlay.LastPress.X, overlay.LastPress.Y);
    }
}

/// <summary>QtQuick.Controls' <c>MenuItem</c>: a row of a menu. Clicking it triggers it and closes the menu.</summary>
[QmlSignal("triggered")]
public sealed class MenuItem : AbstractButton
{
    private bool _highlighted;
    private Menu? _menu;

    public MenuItem()
    {
        // WinUI's MenuFlyoutItemMargin of 4,2 and MenuFlyoutItemThemePaddingNarrow of 11,4,11,5, the rows of a
        // menu opened with the mouse: 32 pixels apart.
        SetDefaultPadding(15, 6, 15, 7);
        TopInset = 2;
        BottomInset = 2;
        LeftInset = 4;
        RightInset = 4;
        Spacing = 12;
    }

    [Qml] public bool Highlighted { get { Track(); return _highlighted || Hovered; } set { if (SetField(ref _highlighted, value)) StateChanged(); } }

    [Qml] public Menu? Menu { get { Track(); return _menu; } internal set => SetField(ref _menu, value); }

    [Qml] public Menu? SubMenu => null;

    [Qml] public Item? Arrow { get; set; }

    [Qml] public double ImplicitTextPadding => Checkable ? 28 : 0;

    protected override void ConfigureLabel(IconLabel label)
    {
        label.Alignment = QtEnums.AlignLeft | QtEnums.AlignVCenter;
        label.LeftPadding = Checkable ? 28 : 0;
    }

    protected override void OnClicked()
    {
        Emit("triggered");
        _menu?.Close();
    }

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        Fluent.FillRound(c, r, Fluent.ControlCornerRadius, FluentResources.MenuFlyoutItemBackground(t).For(Enabled, IsDown, Hovered || _highlighted));
        if (Checkable && Checked)
            Fluent.DrawCheck(c, r.Left + (float)(LeftPadding - LeftInset) + 8, r.MidY, 12, ContentColor);
    }, 120, 28));

    public override QColor ContentColor => FluentResources.MenuFlyoutItemForeground(Theme.Current).For(Enabled, IsDown, Hovered || _highlighted);
}

/// <summary>QtQuick.Controls' <c>MenuSeparator</c>: a line between groups of menu items.</summary>
public sealed class MenuSeparator : Control
{
    public MenuSeparator()
    {
        // WinUI's MenuFlyoutSeparatorThemePadding: a pixel above and below, across the whole menu.
        SetDefaultPadding(0, 1, 0, 1);
    }

    protected override Item? CreateDefaultContentItem() => Part(new PaintedItem((c, i, _) =>
    {
        using var paint = new SKPaint { Color = FluentResources.MenuFlyoutSeparatorBackground(Theme.Current).ToSk() };
        c.DrawRect(SKRect.Create(0, MathF.Round(i.Bounds.MidY), i.Bounds.Width, 1), paint);
    }, 120, 1));
}

/// <summary>The list a ComboBox opens: one row per model entry, the current one marked.</summary>
internal sealed class ComboBoxPopup : Popup
{
    private readonly ComboBox _box;
    private readonly Flickable _list;
    private readonly Column _column;

    public ComboBoxPopup(ComboBox box)
    {
        _box = box;
        Padding = 0;
        TopPadding = 2;
        BottomPadding = 2;
        SetDefaultMargins(0);
        _column = new Column();
        _column.MarkComplete();
        _list = (Flickable)ContentItem!;
        _list.FlickableData.Append(_column);
        Parent = box;
        Engine = box.Engine;
    }

    internal override Item? CreateContentItem()
    {
        var list = new Flickable { Clip = true };
        list.MarkComplete();
        return list;
    }

    public Column Column => _column;

    public Flickable List => _list;

    /// <summary>Sizes the list to its rows, up to a height, and the popup to the combo box's width.</summary>
    public void Fit()
    {
        using var _ = DependencyTracker.Suspend();
        // The rows were just made: lay them out now, so the column has its height before the popup opens.
        PolishQueue.Flush();
        var rowsHeight = _column.ImplicitHeight;
        _list.ContentHeight = rowsHeight;
        _list.ContentWidth = -1;
        ImplicitHeight = Math.Min(rowsHeight, 400) + TopPadding + BottomPadding;
        Width = _box.Width;
        foreach (var row in _column.ChildItems)
        {
            if (!row.HasExplicitWidth)
                row.SetLayoutWidth(_box.Width);
        }
    }

    protected override QPoint RequestedPosition()
    {
        // The current row opens over the box, as WinUI's combo box does.
        var index = Math.Max(0, _box.CurrentIndex);
        var rows = _column.ChildItems;
        var rowY = index < rows.Count ? rows[index].Y : 0;
        var rowH = index < rows.Count ? rows[index].Height : _box.Height;
        var scroll = Math.Max(0, Math.Min(rowY - (Height - rowH) / 2, _list.ContentHeight - _list.Height));
        _list.ContentY = Math.Max(0, scroll);
        return new QPoint(0, -(rowY - _list.ContentY) - TopPadding + (_box.Height - rowH) / 2);
    }

    /// <summary>However it is opened, from the box or by <c>popup.open()</c>, the list shows the current rows.</summary>
    protected override void OnAboutToShow() => _box.PrepareRows();

    /// <summary>The current row, not the first one. Arrow keys and Enter then apply to the entry that is shown.</summary>
    protected override Item ItemToFocusOnOpen() => _box.HighlightedRow() ?? base.ItemToFocusOnOpen();

    protected override void OnClosed() => _box.OnPopupClosed();
}

/// <summary>QtQuick.Controls' <c>ComboBox</c>: a button that opens a list to pick one entry from.</summary>
[QmlSignal("activated", "index")]
[QmlSignal("highlighted", "index")]
[QmlSignal("accepted")]
public sealed class ComboBox : Control, IPointerTarget, IKeyTarget, IWheelTarget
{
    private object? _model;
    private List<ModelEntry> _rows = [];
    private IObservableModel? _observed;
    private string _textRole = "";
    private string _valueRole = "";
    private int _currentIndex = -1;
    private bool _currentIndexSet;
    private string? _displayText;
    private QmlComponent? _delegate;
    private Popup? _popup;
    private bool _popupSet;
    private Item? _indicator;
    private bool _pressed;
    private bool _flat;
    private bool _ignoreRelease;
    private int _highlightedIndex = -1;
    private bool _rowsDirty = true;
    private readonly List<PropertyWatch> _rowWatches = [];

    public ComboBox()
    {
        SetDefaultPadding(11, 5, 38, 7);
    }

    protected override int DefaultFocusPolicy => StrongFocus;

    [Qml]
    public object? Model
    {
        get { Track(); return _model; }
        set
        {
            _model = value is Jint.Native.JsValue js ? ValueConverter.KeepJs(js) : value;
            if (_observed is not null)
            {
                _observed.ModelChanged -= ReadModel;
                _observed.RowChanged -= ReadRow;
            }
            _observed = _model as IObservableModel;
            if (_observed is not null)
            {
                _observed.ModelChanged += ReadModel;
                _observed.RowChanged += ReadRow;
            }
            NotifyChanged("model");
            ReadModel();
        }
    }

    private void ReadRow(int index) => ReadModel();

    private void ReadModel()
    {
        _rows = ModelReader.Read(_model);
        _rowsDirty = true;
        NotifyChanged("count");
        if (_currentIndex >= _rows.Count)
            SetCurrentIndex(_rows.Count - 1);
        else if (_currentIndex < 0 && _rows.Count > 0 && !_currentIndexSet)
            SetCurrentIndex(0);
        TextChanged();
    }

    [Qml] public int Count { get { Track(); return _rows.Count; } }

    [Qml] public string TextRole { get { Track(); return _textRole; } set { if (SetField(ref _textRole, value ?? "")) { _rowsDirty = true; TextChanged(); } } }

    [Qml] public string ValueRole { get { Track(); return _valueRole; } set { if (SetField(ref _valueRole, value ?? "")) NotifyChanged("currentValue"); } }

    [Qml]
    public int CurrentIndex
    {
        get { Track(); return _currentIndex; }
        set
        {
            _currentIndexSet = true;
            SetCurrentIndex(value);
        }
    }

    private void SetCurrentIndex(int value)
    {
        if (SetField(ref _currentIndex, value, nameof(CurrentIndex)))
            TextChanged();
    }

    private void TextChanged()
    {
        NotifyChanged("currentText");
        NotifyChanged("currentValue");
        NotifyChanged("displayText");
        if (HasDefaultContentItem && ContentItem is Text text)
            text.Text_ = DisplayText;
        Update();
    }

    [Qml] public string CurrentText { get { Track("currentIndex"); Track("model"); return TextAt(_currentIndex); } }

    [Qml] public object? CurrentValue { get { Track("currentIndex"); Track("model"); return ValueAt(_currentIndex); } }

    [Qml]
    public string DisplayText
    {
        get { Track(); TrackQml("currentText"); return _displayText ?? TextAt(_currentIndex); }
        set { _displayText = value; TextChanged(); }
    }

    [Qml] public bool Editable { get; set; }

    [Qml] public string EditText { get; set; } = "";

    [Qml] public bool AcceptableInput => true;

    [Qml] public object? Validator { get; set; }

    [Qml] public int InputMethodHints { get; set; }

    [Qml] public int ImplicitContentWidthPolicy { get; set; }

    [Qml] public bool Flat { get { Track(); return _flat; } set { if (SetField(ref _flat, value)) Update(); } }

    [Qml("pressed")] public bool IsPressed { get { Track("pressed"); return _pressed; } set => SetPressed(value); }

    [Qml] public bool Down { get { Track("pressed"); return _pressed || (_popup?.Opened ?? false); } set => SetPressed(value); }

    [Qml] public int HighlightedIndex { get { Track(); return _highlightedIndex; } }

    [Qml]
    public QmlComponent? Delegate
    {
        get { Track(); return _delegate; }
        set
        {
            _delegate = value;
            _rowsDirty = true;
            NotifyChanged("delegate");
        }
    }

    [Qml]
    public Popup? Popup
    {
        get { Track(); return _popup; }
        set
        {
            _popupSet = true;
            if (_popup == value)
                return;
            _popup = value;
            if (value is not null && value.ParentItem is null)
                value.Parent = this;
            NotifyChanged("popup");
        }
    }

    [Qml]
    public Item? Indicator
    {
        get { Track(); return _indicator; }
        set
        {
            if (_indicator == value)
                return;
            if (_indicator?.ParentItem == this)
                _indicator.SetParentItem(null);
            _indicator = value;
            value?.SetParentItem(this);
            NotifyChanged("indicator");
            LayoutChildren();
        }
    }

    [Qml] public double ImplicitIndicatorWidth => _indicator?.ImplicitWidth ?? 0;

    [Qml] public double ImplicitIndicatorHeight => _indicator?.ImplicitHeight ?? 0;

    // Model access

    private static object? Role(object? row, string role)
    {
        switch (row)
        {
            case Jint.Native.JsValue js when js.IsObject() && !js.IsArray():
                return ValueConverter.FromJs(js.AsObject().Get(role));
            case QObject obj when obj.HasProperty(role):
                return obj.ReadProperty(role);
            case IReadOnlyDictionary<string, object?> dict when dict.TryGetValue(role, out var v):
                return v;
            case IDictionary<string, object?> d when d.TryGetValue(role, out var v2):
                return v2;
            default:
                return null;
        }
    }

    [Qml]
    public string TextAt(int index)
    {
        if (index < 0 || index >= _rows.Count)
            return "";
        var row = _rows[index];
        object? value;
        if (_textRole.Length > 0)
            value = row.Roles is { } roles && roles.TryGetValue(_textRole, out var r) ? r : Role(row.ModelData, _textRole);
        else if (row.Roles is { } roles && roles.Count > 0)
            value = roles.TryGetValue("text", out var t) ? t : roles.TryGetValue("modelData", out var m) ? m : roles.Values.First();
        else
            value = row.ModelData;
        return ValueConverter.ToQmlString(value);
    }

    [Qml]
    public object? ValueAt(int index)
    {
        if (index < 0 || index >= _rows.Count)
            return null;
        var row = _rows[index];
        if (_valueRole.Length == 0)
            return row.ModelData is Jint.Native.JsValue ? TextAt(index) : row.ModelData ?? TextAt(index);
        return row.Roles is { } roles && roles.TryGetValue(_valueRole, out var r) ? r : Role(row.ModelData, _valueRole);
    }

    [Qml]
    public int IndexOfValue(object? value)
    {
        var v = ValueConverter.Unwrap(value);
        for (var i = 0; i < _rows.Count; i++)
        {
            if (ValueConverter.AreEqual(ValueAt(i), v))
                return i;
        }
        return -1;
    }

    [Qml]
    public int Find(string text, int flags = 0)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (string.Equals(TextAt(i), text, flags == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    [Qml]
    public void IncrementCurrentIndex()
    {
        if (_currentIndex < _rows.Count - 1)
            Activate(_currentIndex + 1);
    }

    [Qml]
    public void DecrementCurrentIndex()
    {
        if (_currentIndex > 0)
            Activate(_currentIndex - 1);
    }

    [Qml]
    public void SelectAll()
    {
    }

    private void Activate(int index)
    {
        _currentIndexSet = true;
        SetCurrentIndex(index);
        Emit("activated", index);
    }

    // Parts

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var down = Down;
        if (_flat)
        {
            Fluent.FillRound(c, r, Fluent.ControlCornerRadius, !Enabled ? QColor.Transparent : _pressed ? t.SubtleFillColorTertiary : Hovered ? t.SubtleFillColorSecondary : QColor.Transparent);
            return;
        }
        bool enabled = Enabled, hovered = Hovered || down;
        var fill = FluentResources.ComboBoxBackground(t).For(enabled, _pressed, hovered);
        if (t.IsHighContrast)
        {
            Fluent.FillRound(c, r, Fluent.ControlCornerRadius, fill);
            Fluent.StrokeRound(c, r, Fluent.ControlCornerRadius, FluentResources.ComboBoxBorderBrush(t).For(enabled, _pressed, hovered));
        }
        else if (!enabled || _pressed)
        {
            Fluent.ElevatedRect(c, r, Fluent.ControlCornerRadius, fill, t.ControlStrokeColorDefault, t.ControlStrokeColorDefault);
        }
        else
        {
            Fluent.ElevatedRect(c, r, Fluent.ControlCornerRadius, fill, t.ControlStrokeColorDefault, t.ControlStrokeColorSecondary, edgeOnTop: t.IsDark);
        }
    }, 120, Fluent.ControlHeight));

    private QColor LabelColor => FluentResources.ComboBoxForeground(Theme.Current).For(Enabled, _pressed, Hovered);

    protected override Item? CreateDefaultContentItem()
    {
        var text = new Text { VerticalAlignment = QtEnums.AlignVCenter, Elide = (int)Quick.Text.ElideValue.ElideRight };
        CopyFont(Font, text.Font);
        text.Text_ = DisplayText;
        return Part(text);
    }

    protected override void OnFontChanged()
    {
        if (HasDefaultContentItem && ContentItem is Text text)
            CopyFont(Font, text.Font);
    }

    protected override void CreateDefaults()
    {
        if (_indicator is null)
        {
            Indicator = Part(new PaintedItem((c, i, _) =>
            {
                var t = Theme.Current;
                var r = i.Bounds;
                var offset = _pressed ? 1.5f : 0;
                Fluent.DrawChevron(c, Fluent.ChevronDown, r.MidX, r.MidY + offset, 12, FluentResources.ComboBoxDropDownGlyphForeground(t).For(Enabled, _pressed, Hovered));
            }, 32, 32));
        }
        if (!_popupSet && _popup is null)
        {
            var popup = new ComboBoxPopup(this);
            popup.SetOwner(this);
            popup.CompleteFromCode();
            _popup = popup;
        }
        base.CreateDefaults();
        if (HasDefaultContentItem && ContentItem is Text text)
        {
            text.Color = LabelColor;
            text.Text_ = DisplayText;
        }
    }

    protected override void OnThemeChanged()
    {
        if (HasDefaultContentItem && ContentItem is Text text)
            text.Color = LabelColor;
    }

    protected override void StateChanged()
    {
        OnThemeChanged();
        base.StateChanged();
    }

    protected override void LayoutParts()
    {
        if (_indicator is { } ind && ind.ParentItem == this && !ind.HasAnchors)
        {
            if (!ind.HasExplicitWidth)
                ind.SetLayoutWidth(ind.ImplicitWidth);
            if (!ind.HasExplicitHeight)
                ind.SetLayoutHeight(ind.ImplicitHeight);
            ind.SetLayoutX(Width - ind.Width - 4);
            ind.SetLayoutY((Height - ind.Height) / 2);
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        using var _ = DependencyTracker.Suspend();
        // Wide enough for the longest entry, as Qt's default content width policy is for the current one.
        var font = Font.Spec;
        var widest = 0.0;
        for (var i = 0; i < Math.Min(_rows.Count, 50); i++)
        {
            using var row = TextLayout.Build([new TextSpan(TextAt(i), font)], double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, 1, true);
            widest = Math.Max(widest, row.Width);
        }
        return (Math.Max(w, Math.Ceiling(widest) + LeftPadding + RightPadding), h);
    }

    // The popup list

    private void BuildRows()
    {
        if (_popup is not ComboBoxPopup popup || !_rowsDirty)
            return;
        _rowsDirty = false;
        foreach (var w in _rowWatches)
            w.Dispose();
        _rowWatches.Clear();
        foreach (var row in popup.Column.ChildItems.ToArray())
            row.Destroy();
        for (var i = 0; i < _rows.Count; i++)
        {
            var index = i;
            Item? item;
            if (_delegate is not null)
            {
                var context = ModelReader.ContextFor(_rows[i], i);
                item = _delegate.Create(null, null, context, o =>
                {
                    foreach (var (k, v) in context)
                    {
                        if (o.GetDynamicProperty(k) is { IsRequired: true })
                            o.WriteProperty(k, v);
                    }
                    if (o is Item it)
                        it.SetParentItem(popup.Column);
                }) as Item;
            }
            else
            {
                var row = new Row(this, index) { Engine = Engine, Text = TextAt(i) };
                CopyFont(Font, row.Font);
                row.MarkComplete();
                row.SetParentItem(popup.Column);
                item = row;
            }
            if (item is null)
                continue;
            item.SetOwner(popup);
            if (item is AbstractButton button)
            {
                button.Connect("clicked", _ =>
                {
                    Activate(index);
                    _popup?.Close();
                });
                _rowWatches.Add(new PropertyWatch(button, () =>
                {
                    if (button.Hovered)
                        SetHighlighted(index);
                }, "hovered"));
            }
        }
    }

    private void SetHighlighted(int index)
    {
        if (SetField(ref _highlightedIndex, index, nameof(HighlightedIndex)))
        {
            Emit("highlighted", index);
            if (_popup is ComboBoxPopup p)
            {
                foreach (var row in p.Column.ChildItems)
                    row.Update();
            }
        }
    }

    private void OpenPopup()
    {
        if (_popup is null)
            return;
        if (_popup is not ComboBoxPopup)
            PrepareRows();
        _popup.Open();
        NotifyChanged("down");
        Update();
    }

    /// <summary>Builds the rows, fits the default list to them, and highlights the current one.</summary>
    internal void PrepareRows()
    {
        BuildRows();
        if (_popup is ComboBoxPopup popup)
            popup.Fit();
        SetHighlighted(_currentIndex);
    }

    internal void OnPopupClosed()
    {
        SetHighlighted(-1);
        NotifyChanged("down");
        Update();
    }

    private void SetPressed(bool value)
    {
        if (SetField(ref _pressed, value, "pressed"))
        {
            NotifyChanged("down");
            StateChanged();
        }
    }

    public bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        _ignoreRelease = _popup is not null && PopupOverlay.ClosedByPress.Contains(_popup);
        PopupOverlay.ClosedByPress.Clear();
        SetPressed(true);
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers) => SetPressed(HitTest(x, y));

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        var was = _pressed;
        SetPressed(false);
        if (!was || _ignoreRelease || !HitTest(x, y))
            return;
        if (_popup?.Opened == true)
            _popup.Close();
        else
            OpenPopup();
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel() => SetPressed(false);

    public bool KeyPress(KeyEvent e)
    {
        if (IsPopupOpen)
            return KeyPressOpen(e);
        var alt = (e.Modifiers & QtEnums.AltModifier) != 0;
        switch (e.Key)
        {
            // Alt+Down and F4 open the list, as they do in Windows.
            case 0x01000015 when alt:
            case 0x01000033:
            case 0x20 or 0x01000004 or 0x01000005:
                OpenPopup();
                return true;
            case 0x01000013:
                DecrementCurrentIndex();
                return true;
            case 0x01000015:
                IncrementCurrentIndex();
                return true;
        }
        return false;
    }

    /// <summary>
    /// While the list is open, arrows move the highlight. Enter and Space commit it. They do not
    /// change the current entry on the way down, and they do not reopen the list.
    /// </summary>
    private bool KeyPressOpen(KeyEvent e)
    {
        var alt = (e.Modifiers & QtEnums.AltModifier) != 0;
        switch (e.Key)
        {
            case 0x01000013:
                MoveHighlight(-1);
                return true;
            case 0x01000015 when !alt:
                MoveHighlight(1);
                return true;
            case 0x01000015 when alt:
            case 0x01000033:
            case 0x01000000:
                _popup?.Close();
                return true;
            case 0x20 or 0x01000004 or 0x01000005:
                if (!e.IsAutoRepeat)
                    AcceptHighlight();
                return true;
        }
        return false;
    }

    private void CloseList() => _popup?.Close();

    private void MoveHighlight(int delta)
    {
        if (_rows.Count == 0)
            return;
        var start = _highlightedIndex >= 0 ? _highlightedIndex : Math.Max(0, _currentIndex);
        var next = Math.Clamp(start + delta, 0, _rows.Count - 1);
        SetHighlighted(next);
        HighlightedRow()?.ForceActiveFocus();
    }

    private void AcceptHighlight()
    {
        var index = _highlightedIndex >= 0 ? _highlightedIndex : _currentIndex;
        if (index >= 0)
            Activate(index);
        _popup?.Close();
    }

    internal Item? HighlightedRow()
    {
        if (_popup is not ComboBoxPopup popup)
            return null;
        var rows = popup.Column.ChildItems;
        var index = _highlightedIndex >= 0 ? _highlightedIndex : Math.Max(0, _currentIndex);
        return (uint)index < (uint)rows.Count ? rows[index] : null;
    }

    /// <summary>Whether the list is open.</summary>
    public bool IsPopupOpen => _popup?.Opened == true;

    /// <summary>Opens or closes the list, as UI Automation's expand and collapse do.</summary>
    public void SetPopupOpen(bool open)
    {
        if (open && !IsPopupOpen)
            OpenPopup();
        else if (!open && IsPopupOpen)
            _popup?.Close();
    }

    public bool KeyRelease(KeyEvent e) => false;

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted)
    {
        if (!WheelEnabled || Math.Abs(angleY) <= Math.Abs(angleX))
            return false;
        if (angleY > 0)
            DecrementCurrentIndex();
        else
            IncrementCurrentIndex();
        return true;
    }

    protected override void OnDestroy()
    {
        if (_observed is not null)
        {
            _observed.ModelChanged -= ReadModel;
            _observed.RowChanged -= ReadRow;
        }
        foreach (var w in _rowWatches)
            w.Dispose();
        base.OnDestroy();
    }

    /// <summary>The default row of the list: an item delegate marked when it is the current entry.</summary>
    internal sealed class Row(ComboBox box, int index) : ItemDelegate
    {
        public bool IsCurrent => box.CurrentIndex == index;

        protected override bool ShowsSelection => IsCurrent;

        public override bool KeyPress(KeyEvent e)
        {
            switch (e.Key)
            {
                case 0x01000000:
                    box.CloseList();
                    return true;
                case 0x01000013:
                    box.MoveHighlight(-1);
                    return true;
                case 0x01000015 when (e.Modifiers & QtEnums.AltModifier) == 0:
                    box.MoveHighlight(1);
                    return true;
            }
            return base.KeyPress(e);
        }
    }
}
