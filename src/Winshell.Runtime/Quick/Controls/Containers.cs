using Jint;
using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>
/// QtQuick.Controls' <c>Pane</c>: a control whose children go into its content item. With one child, the
/// pane takes that child's implicit size as its content size.
/// </summary>
[QmlDefaultProperty("contentData")]
public class Pane : Control
{
    private double? _contentWidth, _contentHeight;
    private Item? _watchedHost;
    private PropertyWatch? _childWatch;
    private Item? _singleChild;

    public Pane()
    {
        SetDefaultPadding(12, 12, 12, 12);
        InitContentItem(Part(new Item()));
    }

    [Qml]
    public ItemData ContentData => (ContentItem ?? this).Data;

    [Qml]
    public QmlList<Item> ContentChildren => (ContentItem ?? this).Children;

    [Qml]
    public double ContentWidth
    {
        get { Track(); return _contentWidth ?? ((_singleChild?.ImplicitWidth) ?? 0); }
        set { _contentWidth = value; ContentSizeChanged(); }
    }

    [Qml]
    public double ContentHeight
    {
        get { Track(); return _contentHeight ?? ((_singleChild?.ImplicitHeight) ?? 0); }
        set { _contentHeight = value; ContentSizeChanged(); }
    }

    public override double ImplicitContentWidth
    {
        get { TrackQml("implicitContentWidth"); return ContentWidth; }
    }

    public override double ImplicitContentHeight
    {
        get { TrackQml("implicitContentHeight"); return ContentHeight; }
    }

    private void ContentSizeChanged()
    {
        NotifyChanged("contentWidth");
        NotifyChanged("contentHeight");
        NotifyChanged("implicitContentWidth");
        NotifyChanged("implicitContentHeight");
        UpdateImplicitSize();
    }

    protected override void OnContentItemChanged(Item? oldItem, Item? newItem)
    {
        base.OnContentItemChanged(oldItem, newItem);
        // The children of the old content item move into the new one.
        if (oldItem is not null && newItem is not null && HasDefaultContentItem is false)
        {
            foreach (var child in oldItem.ChildItems.ToArray())
                child.SetParentItem(newItem);
        }
        if (_watchedHost is not null)
            _watchedHost.Children.Changed -= OnChildrenChanged;
        _watchedHost = newItem;
        if (newItem is not null)
            newItem.Children.Changed += OnChildrenChanged;
        OnChildrenChanged();
    }

    private void OnChildrenChanged()
    {
        var children = _watchedHost?.ChildItems ?? [];
        var single = children.Count == 1 ? children[0] : null;
        if (single == _singleChild)
            return;
        _childWatch?.Dispose();
        _childWatch = null;
        _singleChild = single;
        if (single is not null)
            _childWatch = new PropertyWatch(single, ContentSizeChanged, "implicitWidth", "implicitHeight");
        ContentSizeChanged();
    }

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        using var paint = new SKPaint { Color = Theme.Current.SolidBackgroundFillColorBase.ToSk() };
        c.DrawRect(i.Bounds, paint);
    }));

    protected override void OnDestroy()
    {
        _childWatch?.Dispose();
        if (_watchedHost is not null)
            _watchedHost.Children.Changed -= OnChildrenChanged;
        base.OnDestroy();
    }
}

/// <summary>QtQuick.Controls' <c>Frame</c>: a pane drawn as a Windows 11 card.</summary>
public class Frame : Pane
{
    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        Fluent.FillRound(c, i.Bounds, Fluent.OverlayCornerRadius, t.CardBackgroundFillColorDefault);
        Fluent.StrokeRound(c, i.Bounds, Fluent.OverlayCornerRadius, t.CardStrokeColorDefault.A > 0 ? (t.IsDark ? t.ControlStrokeColorDefault : t.CardStrokeColorDefault) : t.ControlStrokeColorDefault);
    }));
}

/// <summary>QtQuick.Controls' <c>GroupBox</c>: a frame with a title above it.</summary>
public sealed class GroupBox : Frame
{
    private string _title = "";
    private Item? _label;
    private bool _labelSet, _defaultLabel;

    public GroupBox()
    {
        Spacing = 6;
    }

    [Qml]
    public string Title
    {
        get { Track(); return _title; }
        set
        {
            if (SetField(ref _title, value ?? "") && _defaultLabel && _label is Text text)
                text.Text_ = _title;
            UpdateImplicitSize();
            LayoutChildren();
        }
    }

    [Qml]
    public Item? Label
    {
        get { Track(); return _label; }
        set
        {
            _labelSet = true;
            SetLabel(value, false);
        }
    }

    [Qml] public double ImplicitLabelWidth { get { Track(); return _label?.ImplicitWidth ?? 0; } }

    [Qml] public double ImplicitLabelHeight { get { Track(); return _label?.ImplicitHeight ?? 0; } }

    private PropertyWatch? _labelWatch;

    private void SetLabel(Item? value, bool isDefault)
    {
        if (_label == value)
            return;
        RetirePart(_label, _defaultLabel);
        _labelWatch?.Dispose();
        _label = value;
        _defaultLabel = isDefault && value is not null;
        if (value is not null)
        {
            value.SetParentItem(this);
            _labelWatch = new PropertyWatch(value, () =>
            {
                NotifyChanged("implicitLabelWidth");
                NotifyChanged("implicitLabelHeight");
                LayoutChildren();
                UpdateImplicitSize();
            }, "implicitWidth", "implicitHeight");
        }
        NotifyChanged("label");
        LayoutChildren();
        UpdateImplicitSize();
    }

    private double LabelSpace => _label is null ? 0 : _label.Height + Spacing;

    protected override void CreateDefaults()
    {
        if (!_labelSet && _label is null)
        {
            var text = new Text { Text_ = _title };
            CopyFont(Font, text.Font);
            text.Font.Weight = 600;
            SetLabel(Part(text), true);
        }
        base.CreateDefaults();
    }

    protected override void LayoutParts()
    {
        if (_label is { } label && label.ParentItem == this && !label.HasAnchors)
        {
            label.SetLayoutX(0);
            label.SetLayoutY(0);
            if (!label.HasExplicitWidth)
                label.SetLayoutWidth(Math.Max(label.ImplicitWidth, 0));
            if (!label.HasExplicitHeight)
                label.SetLayoutHeight(label.ImplicitHeight);
        }
        var offset = LabelSpace;
        if (Background is { } bg && bg.ParentItem == this && !bg.HasAnchors)
        {
            bg.SetLayoutY(TopInset + offset);
            if (!bg.HasExplicitHeight)
                bg.SetLayoutHeight(Math.Max(0, Height - TopInset - BottomInset - offset));
        }
        if (ContentItem is { } content && content.ParentItem == this && !content.HasAnchors)
        {
            content.SetLayoutY(TopPadding + offset);
            if (!content.HasExplicitHeight)
                content.SetLayoutHeight(Math.Max(0, AvailableHeight - offset));
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        using var _ = DependencyTracker.Suspend();
        var labelH = _label is null ? 0 : _label.ImplicitHeight + Spacing;
        return (Math.Max(w, ImplicitLabelWidth), h + labelH);
    }
}

/// <summary>QtQuick.Controls' <c>ToolBar</c>.</summary>
public sealed class ToolBar : Pane
{
    [QmlEnum]
    public enum PositionValue
    {
        Header = 0,
        Footer = 1,
    }

    public ToolBar()
    {
        SetDefaultPadding(6, 6, 6, 6);
    }

    [Qml] public int Position { get; set; }

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        using var paint = new SKPaint { Color = Theme.Current.SolidBackgroundFillColorTertiary.ToSk() };
        c.DrawRect(i.Bounds, paint);
    }, 0, 48));
}

/// <summary>QtQuick.Controls' <c>ToolSeparator</c>: a thin divider between tool buttons.</summary>
public sealed class ToolSeparator : Control
{
    private int _orientation = 2;

    public ToolSeparator()
    {
        SetDefaultPadding(6, 4, 6, 4);
    }

    [Qml] public int Orientation { get { Track(); return _orientation; } set { if (SetField(ref _orientation, value)) { UpdateImplicitSize(); Update(); } } }

    [Qml] public bool Horizontal => _orientation == 1;

    [Qml] public bool Vertical => _orientation == 2;

    protected override Item? CreateDefaultContentItem() => Part(new PaintedItem((c, i, _) =>
    {
        using var paint = new SKPaint { Color = Theme.Current.DividerStrokeColorDefault.ToSk() };
        var r = i.Bounds;
        if (_orientation == 2)
            c.DrawRect(SKRect.Create(MathF.Round(r.MidX), r.Top, 1, r.Height), paint);
        else
            c.DrawRect(SKRect.Create(r.Left, MathF.Round(r.MidY), r.Width, 1), paint);
    }, 1, 24));

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        return _orientation == 2 ? (w, h) : (Math.Max(w, 24 + LeftPadding + RightPadding), 1 + TopPadding + BottomPadding);
    }
}

/// <summary>QtQuick.Controls' <c>Page</c>: a pane with a header and a footer.</summary>
public class Page : Pane
{
    private string _title = "";
    private Item? _header, _footer;
    private PropertyWatch? _headerWatch, _footerWatch;

    public Page()
    {
        SetDefaultPadding(0, 0, 0, 0);
    }

    [Qml] public string Title { get { Track(); return _title; } set => SetField(ref _title, value ?? ""); }

    [Qml] public Item? Header { get { Track(); return _header; } set => SetBar(ref _header, ref _headerWatch, value, "header"); }

    [Qml] public Item? Footer { get { Track(); return _footer; } set => SetBar(ref _footer, ref _footerWatch, value, "footer"); }

    [Qml] public double ImplicitHeaderWidth => _header?.ImplicitWidth ?? 0;

    [Qml] public double ImplicitHeaderHeight => _header?.ImplicitHeight ?? 0;

    [Qml] public double ImplicitFooterWidth => _footer?.ImplicitWidth ?? 0;

    [Qml] public double ImplicitFooterHeight => _footer?.ImplicitHeight ?? 0;

    private void SetBar(ref Item? field, ref PropertyWatch? watch, Item? value, string name)
    {
        if (field == value)
            return;
        if (field is not null && field.ParentItem == this)
            field.SetParentItem(null);
        watch?.Dispose();
        field = value;
        if (value is not null)
        {
            value.SetParentItem(this);
            watch = new PropertyWatch(value, () => { LayoutChildren(); UpdateImplicitSize(); }, "implicitHeight", "height", "visible");
        }
        NotifyChanged(name);
        LayoutChildren();
        UpdateImplicitSize();
    }

    private double HeaderSpace => _header is { IsVisibleFlag: true } h ? h.Height : 0;

    private double FooterSpace => _footer is { IsVisibleFlag: true } f ? f.Height : 0;

    protected override void LayoutParts()
    {
        if (_header is { } header && !header.HasAnchors)
        {
            header.SetLayoutX(0);
            header.SetLayoutY(0);
            header.SetLayoutWidth(Width);
            if (!header.HasExplicitHeight)
                header.SetLayoutHeight(header.ImplicitHeight);
        }
        if (_footer is { } footer && !footer.HasAnchors)
        {
            if (!footer.HasExplicitHeight)
                footer.SetLayoutHeight(footer.ImplicitHeight);
            footer.SetLayoutX(0);
            footer.SetLayoutWidth(Width);
            footer.SetLayoutY(Height - footer.Height);
        }
        if (ContentItem is { } content && content.ParentItem == this && !content.HasAnchors)
        {
            content.SetLayoutY(HeaderSpace + TopPadding);
            if (!content.HasExplicitHeight)
                content.SetLayoutHeight(Math.Max(0, Height - HeaderSpace - FooterSpace - TopPadding - BottomPadding));
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        using var _ = DependencyTracker.Suspend();
        return (Math.Max(w, Math.Max(ImplicitHeaderWidth, ImplicitFooterWidth)), h + (_header?.ImplicitHeight ?? 0) + (_footer?.ImplicitHeight ?? 0));
    }

    protected override void OnDestroy()
    {
        _headerWatch?.Dispose();
        _footerWatch?.Dispose();
        base.OnDestroy();
    }
}

/// <summary>
/// QtQuick.Controls' <c>ApplicationWindow</c>. Winshell shows QML in shell windows, so this is a page:
/// a header, a footer, a menu bar and a background inside whatever holds it.
/// </summary>
public sealed class ApplicationWindow : Page
{
    private QColor? _color;

    [Qml] public QColor Color { get { Track(); return _color ?? Theme.Current.SolidBackgroundFillColorBase; } set { _color = value; NotifyChanged("color"); Update(); } }

    [Qml] public Item? MenuBar { get; set; }

    [Qml] public int Flags { get; set; }

    [Qml] public int Visibility { get; set; }

    [Qml] public double MinimumWidth { get; set; }

    [Qml] public double MinimumHeight { get; set; }

    [Qml] public double MaximumWidth { get; set; }

    [Qml] public double MaximumHeight { get; set; }

    [Qml] public Item? ActiveFocusControl => Host?.FocusItem;

    [Qml] public void Show() => Visible = true;

    [Qml] public void Hide() => Visible = false;

    [Qml] public void ShowNormal() => Visible = true;

    [Qml] public void ShowMaximized() => Visible = true;

    [Qml] public void ShowFullScreen() => Visible = true;

    [Qml] public void Close() => Visible = false;

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        using var paint = new SKPaint { Color = Color.ToSk() };
        c.DrawRect(i.Bounds, paint);
    }));
}

/// <summary>
/// QtQuick.Controls' <c>Container</c>: a control that holds a list of items, one of them current. TabBar and
/// SwipeView are containers.
/// </summary>
[QmlDefaultProperty("contentData")]
public abstract class Container : Control
{
    private readonly List<Item> _items = [];
    private int _currentIndex = -1;
    private readonly ContainerData _data;

    protected Container()
    {
        _data = new ContainerData(this);
    }

    [Qml] public ContainerData ContentData => _data;

    [Qml] public IReadOnlyList<Item> ContentChildren { get { Track("count"); return _items.ToList(); } }

    [Qml] public IReadOnlyList<Item> ContentModel { get { Track("count"); return _items.ToList(); } }

    [Qml] public int Count { get { Track(); return _items.Count; } }

    public IReadOnlyList<Item> Items => _items;

    [Qml]
    public int CurrentIndex
    {
        get { Track(); return _currentIndex; }
        set => SetCurrentIndex(value);
    }

    [Qml] public Item? CurrentItem { get { Track("currentIndex"); return _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null; } }

    /// <summary>The item the content items are parented to.</summary>
    protected abstract Item ItemHost { get; }

    [Qml]
    public void SetCurrentIndex(int index)
    {
        if (!SetField(ref _currentIndex, index, nameof(CurrentIndex)))
            return;
        NotifyChanged("currentItem");
        OnCurrentIndexChanged();
    }

    protected virtual void OnCurrentIndexChanged()
    {
    }

    [Qml] public void IncrementCurrentIndex() { if (_currentIndex < _items.Count - 1) SetCurrentIndex(_currentIndex + 1); }

    [Qml] public void DecrementCurrentIndex() { if (_currentIndex > 0) SetCurrentIndex(_currentIndex - 1); }

    [Qml] public void AddItem(Item item) => InsertItem(_items.Count, item);

    [Qml]
    public void InsertItem(int index, Item item)
    {
        if (_items.Contains(item))
            return;
        index = Math.Clamp(index, 0, _items.Count);
        _items.Insert(index, item);
        item.SetParentItem(ItemHost);
        OnItemAdded(item, index);
        NotifyChanged("count");
        NotifyChanged("contentChildren");
        if (_currentIndex < 0)
            SetCurrentIndex(0);
        else if (index <= _currentIndex && _items.Count > 1)
        {
            _currentIndex++;
            NotifyChanged("currentIndex");
        }
        ItemsChanged();
    }

    [Qml]
    public void MoveItem(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to)
            return;
        var oldCurrent = _currentIndex;
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        NotifyChanged("contentChildren");
        if (from == oldCurrent)
            SetCurrentIndex(to);
        else if (from < oldCurrent && to >= oldCurrent)
            SetCurrentIndex(oldCurrent - 1);
        else if (from > oldCurrent && to <= oldCurrent)
            SetCurrentIndex(oldCurrent + 1);
        ItemsChanged();
    }

    [Qml]
    public void RemoveItem(object? item)
    {
        var taken = TakeItemOf(item);
        taken?.Destroy();
    }

    [Qml]
    public Item? TakeItem(int index) => TakeItemOf(index);

    private Item? TakeItemOf(object? which)
    {
        var item = ValueConverter.Unwrap(which) switch
        {
            Item i => i,
            double d when (int)d >= 0 && (int)d < _items.Count => _items[(int)d],
            int n when n >= 0 && n < _items.Count => _items[n],
            _ => null,
        };
        if (item is null)
            return null;
        var index = _items.IndexOf(item);
        if (index < 0)
            return null;
        // Removing the current item selects the previous one, except at index 0, where the
        // next item keeps that index. The index number does not change in that case, so the
        // new item still has to become the checked tab.
        var removedCurrent = index == _currentIndex;
        var followedPrevious = removedCurrent && (index != 0 || _items.Count == 1);
        if (followedPrevious)
            SetCurrentIndex(_currentIndex - 1);
        else if (index < _currentIndex)
        {
            _currentIndex--;
            NotifyChanged("currentIndex");
        }
        _items.RemoveAt(index);
        if (item.ParentItem == ItemHost)
            item.SetParentItem(null);
        NotifyChanged("count");
        NotifyChanged("contentChildren");
        if (removedCurrent && !followedPrevious)
        {
            NotifyChanged("currentItem");
            OnCurrentIndexChanged();
        }
        ItemsChanged();
        return item;
    }

    [Qml]
    public Item? ItemAt(int index) => index >= 0 && index < _items.Count ? _items[index] : null;

    protected virtual void OnItemAdded(Item item, int index)
    {
    }

    /// <summary>Called when items were added, moved or removed.</summary>
    protected virtual void ItemsChanged()
    {
        LayoutChildren();
        UpdateImplicitSize();
    }

    /// <summary>A container's default list: items join the container, other objects are kept as resources.</summary>
    public sealed class ContainerData(Container owner) : IQmlList
    {
        private readonly List<object?> _all = [];

        public int Count => _all.Count;

        public object? this[int index] => _all[index];

        public Type ElementType => typeof(QObject);

        public event Action? Changed;

        public void Append(object? item)
        {
            _all.Add(item);
            switch (item)
            {
                case Item child when owner.AcceptsItem(child):
                    owner.AddItem(child);
                    break;
                case Item other:
                    other.SetParentItem(owner);
                    break;
                case QObject obj:
                    owner.Resources.Add(obj);
                    break;
            }
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (var item in owner._items.ToArray())
                owner.TakeItemOf(item);
            _all.Clear();
            Changed?.Invoke();
        }

        public IEnumerator<object?> GetEnumerator() => _all.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Whether an item declared inside the container is one of its items. A Repeater is not.</summary>
    protected virtual bool AcceptsItem(Item item) => item is not Repeater;
}

/// <summary>QtQuick.Controls' <c>TabBar</c>: a row of tab buttons, one of them current.</summary>
public sealed class TabBar : Container
{
    private readonly TabRow _row;
    private int _position;
    private readonly Dictionary<Item, PropertyWatch> _watches = [];

    [QmlEnum]
    public enum PositionValue
    {
        Header = 0,
        Footer = 1,
    }

    public TabBar()
    {
        _row = Part(new TabRow(this));
        InitContentItem(_row);
        SetDefaultPadding(0, 0, 0, 0);
    }

    [Qml] public int Position { get { Track(); return _position; } set => SetField(ref _position, value); }

    [Qml] public double ContentWidth { get { Track("implicitContentWidth"); return _row.ImplicitWidth; } }

    [Qml] public double ContentHeight { get { Track("implicitContentHeight"); return _row.ImplicitHeight; } }

    protected override Item ItemHost => _row;

    protected override void OnItemAdded(Item item, int index)
    {
        if (item is AbstractButton button)
        {
            _watches[item] = new PropertyWatch(button, () => OnTabChecked(button), "checked");
            button.Checked = index == CurrentIndex || (CurrentIndex < 0 && index == 0);
        }
    }

    private void OnTabChecked(AbstractButton button)
    {
        if (button.Checked)
        {
            var index = Items.ToList().IndexOf(button);
            if (index >= 0)
                SetCurrentIndex(index);
        }
    }

    protected override void OnCurrentIndexChanged()
    {
        if (CurrentItem is AbstractButton { Checked: false } button)
            button.Checked = true;
    }

    protected override void ItemsChanged()
    {
        foreach (var gone in _watches.Keys.Where(k => !Items.Contains(k)).ToList())
        {
            _watches[gone].Dispose();
            _watches.Remove(gone);
        }
        _row.Arrange();
        base.ItemsChanged();
    }

    protected override void LayoutParts() => _row.Arrange();

    protected override void OnDestroy()
    {
        foreach (var w in _watches.Values)
            w.Dispose();
        base.OnDestroy();
    }

    /// <summary>Lays the tabs side by side, sharing the width when the bar is wider than they need.</summary>
    private sealed class TabRow(TabBar bar) : Item
    {
        private bool _arranging;
        private readonly Dictionary<Item, PropertyWatch> _watches = [];

        protected override void OnChildAdded(Item child)
        {
            base.OnChildAdded(child);
            _watches[child] = new PropertyWatch(child, Arrange, "implicitWidth", "implicitHeight", "visible");
        }

        protected override void OnChildRemoved(Item child)
        {
            base.OnChildRemoved(child);
            if (_watches.Remove(child, out var w))
                w.Dispose();
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
                var tabs = bar.Items.Where(i => i.IsVisibleFlag).ToList();
                var spacing = bar.Spacing;
                var implicitTotal = tabs.Sum(t => t.HasExplicitWidth ? t.Width : t.ImplicitWidth) + spacing * Math.Max(0, tabs.Count - 1);
                var maxH = tabs.Count == 0 ? 0 : tabs.Max(t => t.ImplicitHeight);
                ImplicitWidth = implicitTotal;
                ImplicitHeight = maxH;
                var flexible = tabs.Where(t => !t.HasExplicitWidth).ToList();
                var fixedW = tabs.Where(t => t.HasExplicitWidth).Sum(t => t.Width);
                var share = flexible.Count == 0 ? 0 : (Width - fixedW - spacing * Math.Max(0, tabs.Count - 1)) / flexible.Count;
                var stretch = Width > implicitTotal + 0.5 && bar.HasExplicitWidthOrLayout;
                var x = 0.0;
                foreach (var tab in tabs)
                {
                    var w = tab.HasExplicitWidth ? tab.Width : stretch ? share : tab.ImplicitWidth;
                    tab.SetLayoutX(x);
                    tab.SetLayoutY(0);
                    if (!tab.HasExplicitWidth)
                        tab.SetLayoutWidth(w);
                    if (!tab.HasExplicitHeight)
                        tab.SetLayoutHeight(Height > 0 ? Height : maxH);
                    x += w + spacing;
                }
            }
            finally
            {
                _arranging = false;
            }
        }
    }

    internal bool HasExplicitWidthOrLayout => HasExplicitWidth || HasAnchors || (FindAttached(typeof(LayoutAttached)) is LayoutAttached { FillWidth: true });
}

/// <summary>The attached <c>SwipeView.index</c>, <c>SwipeView.isCurrentItem</c> and <c>SwipeView.view</c>.</summary>
public sealed class SwipeViewAttached(QObject owner) : QObject
{
    private int _index = -1;
    private SwipeView? _view;

    [Qml] public int Index { get { Track(); return _index; } }

    [Qml] public bool IsCurrentItem { get { Track(); return _view is not null && _view.CurrentIndex == _index && _index >= 0; } }

    [Qml] public bool IsNextItem { get { Track("isCurrentItem"); return _view is not null && _view.CurrentIndex + 1 == _index; } }

    [Qml] public bool IsPreviousItem { get { Track("isCurrentItem"); return _view is not null && _view.CurrentIndex - 1 == _index; } }

    [Qml] public SwipeView? View { get { Track(); return _view; } }

    internal void Set(SwipeView? view, int index)
    {
        SetField(ref _view, view, nameof(View));
        SetField(ref _index, index, nameof(Index));
        NotifyChanged("isCurrentItem");
        _ = owner;
    }
}

/// <summary>QtQuick.Controls' <c>SwipeView</c>: pages side by side, one shown, swiped between.</summary>
public sealed class SwipeView : Container, IPointerTarget
{
    private readonly Item _strip;
    private int _orientation = 1;
    private bool _interactive = true;
    private double _offset;
    private double _pressX, _pressY;
    private bool _dragging;
    private IDisposable? _settle;

    public SwipeView()
    {
        _strip = Part(new Item());
        InitContentItem(_strip);
        SetDefaultPadding(0, 0, 0, 0);
        Clip = true;
    }

    [Qml] public int Orientation { get { Track(); return _orientation; } set { if (SetField(ref _orientation, value)) LayoutChildren(); } }

    [Qml] public bool Horizontal => _orientation == 1;

    [Qml] public bool Vertical => _orientation == 2;

    [Qml] public bool Interactive { get { Track(); return _interactive; } set => SetField(ref _interactive, value); }

    protected override Item ItemHost => _strip;

    protected override void OnItemAdded(Item item, int index) => UpdateAttached();

    protected override void ItemsChanged()
    {
        UpdateAttached();
        base.ItemsChanged();
    }

    private void UpdateAttached()
    {
        var items = Items;
        for (var i = 0; i < items.Count; i++)
            ((SwipeViewAttached)items[i].GetAttached(typeof(SwipeViewAttached), o => new SwipeViewAttached(o))).Set(this, i);
    }

    protected override void OnCurrentIndexChanged()
    {
        foreach (var item in Items)
            (item.FindAttached(typeof(SwipeViewAttached)) as SwipeViewAttached)?.Set(this, Items.ToList().IndexOf(item));
        LayoutChildren();
    }

    protected override void LayoutParts()
    {
        var w = AvailableWidth;
        var h = AvailableHeight;
        var items = Items;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (_orientation == 2)
            {
                item.SetLayoutX(0);
                item.SetLayoutY((i - CurrentIndex) * (h + Spacing) + _offset);
            }
            else
            {
                item.SetLayoutX((i - CurrentIndex) * (w + Spacing) + _offset);
                item.SetLayoutY(0);
            }
            item.SetLayoutWidth(w);
            item.SetLayoutHeight(h);
            // Pages far from the current one are not drawn.
            item.Visible = Math.Abs(i - CurrentIndex) <= 1 || _dragging;
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        using var _ = DependencyTracker.Suspend();
        var w = Items.Count == 0 ? 0 : Items.Max(i => i.ImplicitWidth);
        var h = Items.Count == 0 ? 0 : Items.Max(i => i.ImplicitHeight);
        return (w + LeftPadding + RightPadding, h + TopPadding + BottomPadding);
    }

    public bool AcceptsButton(int button) => _interactive && Enabled && button == QtEnums.LeftButton;

    public bool WantsToSteal(double sceneDx, double sceneDy)
    {
        var threshold = Theme.Current.DragThreshold;
        return _interactive && (_orientation == 2 ? Math.Abs(sceneDy) > threshold && Math.Abs(sceneDy) > Math.Abs(sceneDx) : Math.Abs(sceneDx) > threshold && Math.Abs(sceneDx) > Math.Abs(sceneDy));
    }

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        _settle?.Dispose();
        _pressX = x;
        _pressY = y;
        _dragging = false;
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        var d = _orientation == 2 ? y - _pressY : x - _pressX;
        if (!_dragging && Math.Abs(d) < Theme.Current.DragThreshold)
            return;
        _dragging = true;
        var atStart = CurrentIndex <= 0 && d > 0;
        var atEnd = CurrentIndex >= Count - 1 && d < 0;
        _offset = atStart || atEnd ? d / 3 : d;
        LayoutChildren();
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        if (!_dragging)
            return;
        _dragging = false;
        var size = _orientation == 2 ? AvailableHeight : AvailableWidth;
        var index = CurrentIndex;
        if (_offset < -size / 4 && index < Count - 1)
            index++;
        else if (_offset > size / 4 && index > 0)
            index--;
        if (index != CurrentIndex)
        {
            _offset += (index - CurrentIndex) * (size + Spacing);
            SetCurrentIndex(index);
        }
        Settle();
    }

    private void Settle()
    {
        _settle = null;
        if (!Theme.Current.AnimationsEnabled)
            _offset = 0;
        else
            _offset *= 0.6;
        if (Math.Abs(_offset) < 0.5 || Engine is null)
            _offset = 0;
        LayoutChildren();
        if (_offset != 0 && Engine is not null)
            _settle = Engine.Loop.Schedule(AnimationClock.FrameInterval, Settle);
    }

    public bool PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel()
    {
        _dragging = false;
        Settle();
    }

    protected override void OnDestroy()
    {
        _settle?.Dispose();
        base.OnDestroy();
    }
}

/// <summary>The attached <c>StackView.index</c>, <c>StackView.status</c> and <c>StackView.view</c>.</summary>
[QmlSignal("activated")]
[QmlSignal("activating")]
[QmlSignal("deactivated")]
[QmlSignal("deactivating")]
[QmlSignal("removed")]
public sealed class StackViewAttached(QObject owner) : QObject
{
    private int _index = -1;
    private int _status;
    private StackView? _view;

    [Qml] public int Index { get { Track(); return _index; } }

    [Qml] public int Status { get { Track(); return _status; } }

    [Qml] public StackView? View { get { Track(); return _view; } }

    [Qml] public bool Visible { get { Track(); return owner is Item { IsVisibleFlag: true }; } set { if (owner is Item i) i.Visible = value; } }

    internal void Set(StackView? view, int index, int status)
    {
        SetField(ref _view, view, nameof(View));
        SetField(ref _index, index, nameof(Index));
        var old = _status;
        if (SetField(ref _status, status, nameof(Status)))
        {
            if (status == 3)
                Emit("activated");
            else if (old == 3)
                Emit("deactivated");
        }
    }
}

/// <summary>QtQuick.Controls' <c>StackView</c>: a stack of pages; the top one is shown.</summary>
public sealed class StackView : Control
{
    private readonly List<(Item Item, bool Owned)> _stack = [];
    private object? _initialItem;

    [QmlEnum]
    public enum Operation
    {
        Transition = -1,
        Immediate = 0,
        PushTransition = 1,
        ReplaceTransition = 2,
        PopTransition = 3,
    }

    [QmlEnum]
    public enum LoadBehavior
    {
        DontLoad = 0,
        ForceLoad = 1,
    }

    [QmlEnum]
    public enum StatusValue
    {
        Inactive = 0,
        Deactivating = 1,
        Activating = 2,
        Active = 3,
    }

    [Qml] public int Depth { get { Track(); return _stack.Count; } }

    [Qml] public bool Empty { get { Track("depth"); return _stack.Count == 0; } }

    [Qml] public bool Busy => false;

    [Qml] public Item? CurrentItem { get { Track("depth"); TrackQml("currentItem"); return _stack.Count == 0 ? null : _stack[^1].Item; } }

    [Qml] public object? InitialItem { get { Track(); return _initialItem; } set => _initialItem = value is Jint.Native.JsValue js ? ValueConverter.Unwrap(js) : value; }

    [Qml] public QObject? PushEnter { get; set; }

    [Qml] public QObject? PushExit { get; set; }

    [Qml] public QObject? PopEnter { get; set; }

    [Qml] public QObject? PopExit { get; set; }

    [Qml] public QObject? ReplaceEnter { get; set; }

    [Qml] public QObject? ReplaceExit { get; set; }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        if (_initialItem is not null && _stack.Count == 0)
            Push(_initialItem);
    }

    /// <summary>Pushes items, components or URLs, each optionally followed by its properties.</summary>
    [Qml]
    public Item? Push(params object?[] args)
    {
        var entries = Entries(args);
        if (entries.Count == 0)
            return null;
        var before = CurrentItem;
        foreach (var (target, properties) in entries)
        {
            var item = Make(target, properties, out var owned);
            if (item is not null)
                _stack.Add((item, owned));
        }
        Changed(before);
        return CurrentItem;
    }

    [Qml]
    public Item? Pop(params object?[] args)
    {
        if (_stack.Count <= 1)
            return null;
        var before = CurrentItem;
        // No argument pops one page. null unwinds to the first page. An item unwinds to that item.
        var unwind = false;
        Item? target = null;
        if (args.Length > 0)
        {
            var first = ValueConverter.Unwrap(args[0]);
            if (first is null)
                unwind = true;
            else if (first is Item page)
                target = page;
        }
        Item? popped = null;
        while (_stack.Count > 1)
        {
            if (target is not null && _stack[^1].Item == target)
                break;
            popped = Remove(_stack.Count - 1);
            if (!unwind && target is null)
                break;
        }
        Changed(before);
        return popped;
    }

    [Qml]
    public Item? Replace(params object?[] args)
    {
        var list = args.Select(ValueConverter.Unwrap).ToList();
        var before = CurrentItem;
        if (list.Count > 0 && list[0] is Item existing && _stack.Any(e => e.Item == existing) && list.Count > 1)
        {
            // replace(target, item, ...): replaces target and everything above it.
            while (_stack.Count > 0 && _stack[^1].Item != existing)
                Remove(_stack.Count - 1);
            if (_stack.Count > 0)
                Remove(_stack.Count - 1);
            list.RemoveAt(0);
        }
        else if (list.Count > 0 && list[0] is null && list.Count > 1)
        {
            while (_stack.Count > 0)
                Remove(_stack.Count - 1);
            list.RemoveAt(0);
        }
        else if (_stack.Count > 0)
        {
            Remove(_stack.Count - 1);
        }
        foreach (var (target, properties) in Entries(list.ToArray()))
        {
            var item = Make(target, properties, out var owned);
            if (item is not null)
                _stack.Add((item, owned));
        }
        Changed(before);
        return CurrentItem;
    }

    [Qml]
    public void Clear(int operation = 0)
    {
        var before = CurrentItem;
        while (_stack.Count > 0)
            Remove(_stack.Count - 1);
        Changed(before);
    }

    [Qml]
    public Item? Get(int index, int behavior = 0) => index >= 0 && index < _stack.Count ? _stack[index].Item : null;

    [Qml]
    public Item? Find(Jint.Native.JsValue callback, int behavior = 0)
    {
        if (Engine is null)
            return null;
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var result = Engine.Js.CallFunction(callback, null, [_stack[i].Item, i]);
            if (ValueConverter.ToBool(ValueConverter.FromJs(result)))
                return _stack[i].Item;
        }
        return null;
    }

    private static List<(object Target, object? Properties)> Entries(object?[] args)
    {
        var result = new List<(object, object?)>();
        var list = args.Select(ValueConverter.Unwrap).ToList();
        if (list.Count == 1 && list[0] is List<object?> array)
            list = array;
        for (var i = 0; i < list.Count; i++)
        {
            var value = list[i];
            if (value is null or double)
                continue;
            object? properties = null;
            if (i + 1 < list.Count && list[i + 1] is Jint.Native.JsValue { } js && js.IsObject() && js is not QObjectWrapper)
            {
                properties = js;
                i++;
            }
            result.Add((value, properties));
        }
        return result;
    }

    private Item? Make(object target, object? properties, out bool owned)
    {
        owned = false;
        switch (target)
        {
            case Item item:
                ApplyProperties(item, properties);
                item.SetParentItem(this);
                return item;
            case QmlComponent component:
                owned = true;
                return component.CreateObject(this, properties) as Item;
            case string url when Engine is not null:
                {
                    var directory = CreationContext?.Imports?.Directory ?? Engine.ShellDir ?? Environment.CurrentDirectory;
                    var path = url.StartsWith("file:///", StringComparison.Ordinal) ? Uri.UnescapeDataString(url[8..]) : Path.Combine(directory, url);
                    try
                    {
                        owned = true;
                        return new QmlComponent(Engine, Engine.LoadDocument(path)).CreateObject(this, properties) as Item;
                    }
                    catch (Exception e) when (e is IOException or Winshell.Qml.QmlSyntaxException)
                    {
                        Engine.Log.Warn("scene", $"StackView: cannot load {url}: {e.Message}");
                        return null;
                    }
                }
            default:
                return null;
        }
    }

    private void ApplyProperties(Item item, object? properties)
    {
        if (properties is not Jint.Native.JsValue js || !js.IsObject())
            return;
        var obj = js.AsObject();
        foreach (var key in obj.GetOwnPropertyKeys())
        {
            var name = key.ToString();
            if (item.HasProperty(name))
                item.WriteProperty(name, obj.Get(key));
        }
        _ = Engine;
    }

    private Item Remove(int index)
    {
        var (item, owned) = _stack[index];
        _stack.RemoveAt(index);
        var attached = item.FindAttached(typeof(StackViewAttached)) as StackViewAttached;
        attached?.Set(null, -1, 0);
        attached?.Emit("removed");
        if (owned)
            item.Destroy();
        else if (item.ParentItem == this)
            item.Visible = false;
        return item;
    }

    private void Changed(Item? before)
    {
        for (var i = 0; i < _stack.Count; i++)
        {
            var item = _stack[i].Item;
            var top = i == _stack.Count - 1;
            item.Visible = top;
            var attached = (StackViewAttached)item.GetAttached(typeof(StackViewAttached), o => new StackViewAttached(o));
            attached.Set(this, i, top ? 3 : 0);
        }
        NotifyChanged("depth");
        if (before != CurrentItem)
            NotifyChanged("currentItem");
        LayoutChildren();
    }

    protected override void LayoutParts()
    {
        foreach (var (item, _) in _stack)
        {
            if (item.HasAnchors)
                continue;
            item.SetLayoutX(LeftPadding);
            item.SetLayoutY(TopPadding);
            if (!item.HasExplicitWidth)
                item.SetLayoutWidth(AvailableWidth);
            if (!item.HasExplicitHeight)
                item.SetLayoutHeight(AvailableHeight);
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        using var _ = DependencyTracker.Suspend();
        var top = _stack.Count == 0 ? null : _stack[^1].Item;
        return ((top?.ImplicitWidth ?? 0) + LeftPadding + RightPadding, (top?.ImplicitHeight ?? 0) + TopPadding + BottomPadding);
    }
}
