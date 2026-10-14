using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Winshell;

/// <summary>
/// What one monitor's Bar shows and hears (ADR 0080): its own copy of every item, the drag that reorders them, the pointer on it,
/// and the presses on the Bar itself. Every element here belongs to this Bar's window and to no other: a WinUI element has one parent.
/// <see cref="BarHost"/> has one of these for each <see cref="BarWindow"/>, and keeps what is one for the whole desk:
/// the plugins, the Panel, the pill, and the toasts.
/// </summary>
internal sealed class BarView
{
    private readonly BarHost _host;
    private readonly Grid _root;
    private readonly Dictionary<string, SlotView> _slots = [];
    private BarReorder? _reorder;
    private IReadOnlyList<string> _left = [];
    private IReadOnlyList<string> _center = [];
    private IReadOnlyList<string> _right = [];
    private FrameworkElement? _centerElement;
    private BarEdge _edge;
    private bool _vertical;
    private bool _hooked;
    private bool _dragging;
    private BarPress? _closedByPress;
    private int _dragX;
    private int _dragY;

    /// <summary>Creates the view of <paramref name="owner"/>. It shows nothing until <see cref="Build"/>.</summary>
    /// <param name="host">The plugin host every Bar shares.</param>
    /// <param name="owner">The Bar's window.</param>
    public BarView(BarHost host, BarWindow owner)
    {
        _host = host;
        Owner = owner;
        _root = owner.Items;
    }

    /// <summary>The Bar's window.</summary>
    public BarWindow Owner { get; }

    /// <summary>The item ids on this Bar, from the left group through the middle to the right. A side bar lists them from the top.</summary>
    /// <returns>The ids, in the order the bar shows them.</returns>
    internal IReadOnlyList<string> PluginIds()
    {
        var ids = new List<string>(_left.Count + _center.Count + _right.Count);
        ids.AddRange(_left);
        ids.AddRange(_center);
        ids.AddRange(_right);
        return ids;
    }

    /// <summary>Which Bar this is among the Bars, in the order they were made. The Panel session knows a Bar by it. The host sets it at each mount.</summary>
    public int Index { get; set; }

    /// <summary>True while the pointer is anywhere in this Bar's window, on an item or between two.</summary>
    public bool PointerOnBar { get; private set; }

    /// <summary>True while the pointer is on this Bar or an item of it still shows its hover, so the pointer's next move is worth a look.</summary>
    public bool Watched => PointerOnBar || AnyHovered();

    /// <summary>What a Panel with no item on the Bar sits against: the middle group, or the whole Bar before one is built.</summary>
    public FrameworkElement Anchor => _centerElement ?? (FrameworkElement)Owner.Content;

    /// <summary>
    /// Builds this Bar's own copy of every item in <paramref name="layout"/>. The window is the same one from mount to mount,
    /// so a pointer that was on the Bar still is.
    /// </summary>
    /// <param name="edge">The edge every Bar is on.</param>
    /// <param name="vertical">True on a Bar along the left or right edge.</param>
    /// <param name="layout">The resolved bar.</param>
    /// <param name="catalog">Discovered manifests, by id, for each item's name.</param>
    /// <returns>The slot of each item, in the order <see cref="BarHost.StartOrder"/> lists the items.</returns>
    public List<SlotView> Build(BarEdge edge, bool vertical, ResolvedLayout layout, IReadOnlyDictionary<string, PluginManifest> catalog)
    {
        _edge = edge;
        _vertical = vertical;
        _dragging = false;
        _reorder?.Cancel();
        _reorder = new BarReorder(_root, vertical, RowGap, Spans, Dropped);
        _slots.Clear();
        _left = Ids(layout.Left);
        _center = CenterIds(layout);
        _right = Ids(layout.Right);
        _root.Children.Clear();
        var left = Row(vertical);
        var right = Row(vertical);
        // The three groups share one cell. Along a vertical bar the first sits at the top and the last at the bottom.
        if (vertical)
        {
            left.VerticalAlignment = VerticalAlignment.Top;
            right.VerticalAlignment = VerticalAlignment.Bottom;
        }
        else
        {
            right.HorizontalAlignment = HorizontalAlignment.Right;
        }

        var made = new List<SlotView>();
        var center = BuildCenter(layout, vertical, catalog, made);
        Fill(left, layout.Left, catalog, made);
        Fill(right, layout.Right, catalog, made);
        _root.Children.Add(left);
        _root.Children.Add(center);
        _root.Children.Add(right);
        _centerElement = center;
        // A grid with no brush is invisible to hit testing, so an empty-bar drag falls through the window.
        _root.Background ??= Paint.Hit;
        if (!_hooked)
        {
            _hooked = true;
            _root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSurfacePressed), true);
            _root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSurfaceReleased), true);

            // The bar's padding lies outside the slots' grid, so the right click that opens the bar's own panel is heard on the whole bar.
            if (Owner.Content is UIElement bar)
            {
                bar.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPaddingPressed), true);
                bar.RightTapped += OnBarRightTapped;
                bar.DoubleTapped += OnBarDoubleTapped;

                // Heard on the whole bar and not on its items, so crossing from one item to the next, over the gap between them, is neither a leave nor a new arrival.
                bar.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => EnterBar()), true);
                bar.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => EnterBar()), true);
                bar.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) =>
                {
                    if (!CursorInsideBar())
                    {
                        LeaveBar();
                    }
                }), true);
            }
        }

        return made;
    }

    /// <summary>Paints the open-panel state on <paramref name="id"/>'s item here, and takes it off every other.</summary>
    /// <param name="id">The open panel's plugin id, or null when none is open.</param>
    public void MarkOpen(string? id)
    {
        foreach (var pair in _slots)
        {
            pair.Value.SetActive(id is not null && string.Equals(pair.Key, id, StringComparison.Ordinal));
        }
    }

    /// <summary>Reads the getters of every item on this Bar that a plugin draws itself.</summary>
    public void RefreshItems()
    {
        foreach (var slot in _slots.Values)
        {
            slot.RefreshKit();
        }
    }

    /// <summary>Reads the getters of <paramref name="id"/>'s own drawing on this Bar, when it has one.</summary>
    /// <param name="id">The id the item is shown under.</param>
    public void RefreshItem(string id)
    {
        if (_slots.TryGetValue(id, out var slot))
        {
            slot.RefreshKit();
        }
    }

    /// <summary>Lets the hovers go and hears that the pointer left, when the cursor is no longer in this Bar's window. The bar's tick asks.</summary>
    public void CheckPointer()
    {
        if (Watched && !CursorInsideBar())
        {
            ReleaseHovers();
            LeaveBar();
        }
    }

    private FrameworkElement BuildCenter(ResolvedLayout layout, bool vertical, IReadOnlyDictionary<string, PluginManifest> catalog, List<SlotView> made)
    {
        if (layout.CenterAsGroup)
        {
            var group = Row(vertical);
            group.HorizontalAlignment = HorizontalAlignment.Center;
            Fill(group, layout.CenterBefore, catalog, made);
            return group;
        }

        // The anchor and its neighbours sit in separate cells, so the cell gap is the gap a row puts between slots.
        // Along a vertical bar those cells are rows, so the anchor stays at the bar's middle.
        var before = Row(vertical);
        var anchor = Row(vertical);
        var after = Row(vertical);
        Grid grid;
        if (vertical)
        {
            grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, RowSpacing = RowGap };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            before.VerticalAlignment = VerticalAlignment.Bottom;
            after.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(before, 0);
            Grid.SetRow(anchor, 1);
            Grid.SetRow(after, 2);
        }
        else
        {
            grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, ColumnSpacing = RowGap };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            before.HorizontalAlignment = HorizontalAlignment.Right;
            after.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(before, 0);
            Grid.SetColumn(anchor, 1);
            Grid.SetColumn(after, 2);
        }

        Fill(before, layout.CenterBefore, catalog, made);
        if (layout.Anchor is not null)
        {
            Fill(anchor, [layout.Anchor], catalog, made);
        }

        Fill(after, layout.CenterAfter, catalog, made);
        grid.Children.Add(before);
        grid.Children.Add(anchor);
        grid.Children.Add(after);
        return grid;
    }

    /// <summary>Adds a slot for each item, in order. The host binds each to its plugin or its program.</summary>
    private void Fill(Panel panel, IReadOnlyList<BarSlot> slots, IReadOnlyDictionary<string, PluginManifest> catalog, List<SlotView> made)
    {
        foreach (var slot in slots)
        {
            var label = catalog.TryGetValue(slot.Id, out var named) && named.Name.Length > 0 ? named.Name : slot.Id;
            var view = new SlotView(slot.Id, label, _vertical, _edge, _host.Session, () => Index);
            view.UseBar(CursorInsideBar);
            view.WatchHover(_host.ArmHover, _host.DisarmHover);
            if (_reorder is not null)
            {
                view.WatchDrag(_reorder);
                view.WatchTray((key, args) => DropTray(slot.Id, key, args));
            }

            panel.Children.Add(view.Host);
            _slots[slot.Id] = view;
            made.Add(view);
        }
    }

    private bool CursorInsideBar()
    {
        var hwnd = Owner.Hwnd;
        if (hwnd == 0 || !PointerProbe.TryCursor(out var x, out var y) || !PointerProbe.TryWindow(hwnd, out var rect))
        {
            return true;
        }

        return rect.Contains(x, y);
    }

    /// <summary>
    /// The pointer moved while it was on a Bar. An item whose pointer left this Bar without crossing its edge lets its hover go,
    /// and the Bar hears that the pointer is gone.
    /// </summary>
    /// <returns>False when the pointer was not on this Bar and no item of it showed a hover, so there was nothing to look at.</returns>
    public bool PointerMoved()
    {
        if (!Watched)
        {
            return false;
        }

        if (!CursorInsideBar())
        {
            ReleaseHovers();
            LeaveBar();
        }

        return true;
    }

    /// <summary>
    /// The pointer is over the bar's window. A pointer an item holds still reports its moves here from outside the bar,
    /// so the cursor's own place is asked before the first one is believed.
    /// </summary>
    private void EnterBar()
    {
        if (PointerOnBar || !CursorInsideBar())
        {
            return;
        }

        PointerOnBar = true;
        _host.ArmHover();
        _host.Attend();
    }

    /// <summary>The cursor is outside the bar's window. Call only after <see cref="CursorInsideBar"/> said so.</summary>
    private void LeaveBar()
    {
        // The places a held item can land were measured with the cluster as wide as it was, so the cluster keeps that width until the drop.
        if (!PointerOnBar || _reorder is { Held: true })
        {
            return;
        }

        PointerOnBar = false;
        _host.DisarmHover();
        _host.Attend();
    }

    /// <summary>True when the pointer is over a slot. A loop, because the bar tick asks this five times a second.</summary>
    private bool AnyHovered()
    {
        foreach (var slot in _slots.Values)
        {
            if (slot.Hovered)
            {
                return true;
            }
        }

        return false;
    }

    private void ReleaseHovers()
    {
        foreach (var slot in _slots.Values)
        {
            slot.ReleaseHover();
        }
    }

    private void OnSurfacePressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && InSlot(source))
        {
            _dragging = false;
            return;
        }

        _closedByPress = _host.Session.BarPressed(Index);
        if (sender is not UIElement element
            || !args.GetCurrentPoint(element).Properties.IsLeftButtonPressed
            || !TryPointerScreen(args, out var x, out var y))
        {
            _dragging = false;
            return;
        }

        _dragging = true;
        _dragX = x;
        _dragY = y;
        element.CapturePointer(args.Pointer);
    }

    /// <summary>A press on the bar's padding, which the slots' grid never hears. It closes the open panel like any press on the bar itself.</summary>
    private void OnPaddingPressed(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, sender))
        {
            _closedByPress = _host.Session.BarPressed(Index);
        }
    }

    /// <summary>A right click on the bar itself opens the bar's own panel at the pointer. An item hears its own right click and handles it.</summary>
    private void OnBarRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.Handled || sender is not UIElement bar || args.OriginalSource is DependencyObject source && InSlot(source))
        {
            return;
        }

        args.Handled = true;
        var press = _closedByPress ?? default;
        _closedByPress = null;
        var pointer = args.GetPosition(bar);
        var opened = _host.ClickBar(this, new Rect(pointer.X, pointer.Y, 0, 0), press);
        Log.Information("Bar", "right click on the bar " + (opened ? "opened its panel" : "left its panel closed")
            + (press.Closed is null ? "" : ", and its press closed " + press.Closed)
            + (press.Only ? ", which was on another monitor" : ""));
    }

    /// <summary>A double click on the bar outside every item sends winoma bar transparent toggle. An item keeps its own clicks.</summary>
    private void OnBarDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (args.Handled || args.OriginalSource is DependencyObject source && InSlot(source))
        {
            return;
        }

        args.Handled = true;
        _host.ToggleTransparent();
    }

    private void OnSurfaceReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _root.ReleasePointerCaptures();
        if (!TryPointerScreen(args, out var x, out var y))
        {
            return;
        }

        var screen = ScreenMetrics.Of(Owner.Hwnd);
        var edge = BarEdgeDrag.Choose(_dragX, _dragY, x, y, new PixelRect(screen.X, screen.Y, screen.Width, screen.Height));
        if (edge is BarEdge chosen && chosen != _edge)
        {
            _host.ChooseEdge(chosen);
        }
    }

    // The drop goes to the plugin whose item the icon was dragged from, which is a clone's id when one stands in for the tray.
    private void DropTray(string id, string key, PointerRoutedEventArgs args)
    {
        if (key.Length == 0 || !TryPointerScreen(args, out var x, out var y))
        {
            return;
        }

        _host.DropTray(id, key, x, y);
    }

    private bool TryPointerScreen(PointerRoutedEventArgs args, out int x, out int y)
    {
        x = 0;
        y = 0;
        var hwnd = Owner.Hwnd;
        if (hwnd == 0 || !PointerProbe.TryWindow(hwnd, out var rect))
        {
            return false;
        }

        // Measured from the window's own corner. The slots' grid starts inside the bar's padding, so a point in it falls short by that much.
        var local = args.GetCurrentPoint(null).Position;
        var scale = ScreenMetrics.Scale(hwnd);
        x = rect.Left + (int)Math.Round(local.X * scale);
        y = rect.Top + (int)Math.Round(local.Y * scale);
        return true;
    }

    /// <summary>Every shown item as it lies along the bar, section by section.</summary>
    private List<BarSpan> Spans()
    {
        var spans = new List<BarSpan>();
        Measure("left", _left);
        Measure("center", _center);
        Measure("right", _right);
        return spans;

        void Measure(string section, IReadOnlyList<string> ids)
        {
            foreach (var id in ids)
            {
                if (!_slots.TryGetValue(id, out var view) || !view.Shown)
                {
                    continue;
                }

                var origin = view.Host.TransformToVisual(_root).TransformPoint(new Point(0, 0));
                spans.Add(_vertical
                    ? new BarSpan(id, section, origin.Y, origin.Y + view.Host.ActualHeight)
                    : new BarSpan(id, section, origin.X, origin.X + view.Host.ActualWidth));
            }
        }
    }

    /// <summary>An item was let go over a new place. The move runs after the release has finished, since it builds every Bar again.</summary>
    private void Dropped(string id, BarDropTarget target)
    {
        _slots.TryGetValue(id, out var view);
        _host.Enqueue(() =>
        {
            _host.MoveItem(id, target);
            // The same view still on the bar means nothing was written, so the item goes back to its place.
            if (view is not null && _slots.TryGetValue(id, out var now) && ReferenceEquals(now, view))
            {
                BarReorder.Home(view.Host);
            }
        });
    }

    private static List<string> Ids(IReadOnlyList<BarSlot> slots) => slots.Select(slot => slot.Id).ToList();

    private static List<string> CenterIds(ResolvedLayout layout)
    {
        var ids = Ids(layout.CenterBefore);
        if (layout.Anchor is not null)
        {
            ids.Add(layout.Anchor.Id);
        }

        ids.AddRange(Ids(layout.CenterAfter));
        return ids;
    }

    private bool InSlot(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not FrameworkElement element)
            {
                continue;
            }

            foreach (var slot in _slots.Values)
            {
                if (ReferenceEquals(slot.Host, element))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private const double RowGap = 4;

    private static StackPanel Row(bool vertical) => new()
    {
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
        Spacing = RowGap,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

