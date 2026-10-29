namespace Winshell.Runtime.Quick;

/// <summary>The attached <c>Layout</c> object: how an item wants to be sized inside a layout.</summary>
public sealed class LayoutAttached(QObject owner) : QObject
{
    private double _minimumWidth, _minimumHeight;
    private double _preferredWidth = -1, _preferredHeight = -1;
    private double _maximumWidth = double.PositiveInfinity, _maximumHeight = double.PositiveInfinity;
    private bool _fillWidth, _fillHeight;
    private bool _fillWidthSet, _fillHeightSet;
    private int _alignment;
    private double _margins;
    private double? _leftMargin, _rightMargin, _topMargin, _bottomMargin;
    private int _row = -1, _column = -1, _rowSpan = 1, _columnSpan = 1;
    private int _horizontalStretchFactor = -1, _verticalStretchFactor = -1;

    private void Changed() => ((owner as Item)?.ParentItem as LayoutBase)?.Invalidate();

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (SetField(ref field, value, name))
            Changed();
    }

    [Qml] public double MinimumWidth { get { Track(); return _minimumWidth; } set => Set(ref _minimumWidth, value); }

    [Qml] public double MinimumHeight { get { Track(); return _minimumHeight; } set => Set(ref _minimumHeight, value); }

    [Qml] public double PreferredWidth { get { Track(); return _preferredWidth; } set => Set(ref _preferredWidth, value); }

    [Qml] public double PreferredHeight { get { Track(); return _preferredHeight; } set => Set(ref _preferredHeight, value); }

    [Qml] public double MaximumWidth { get { Track(); return _maximumWidth; } set => Set(ref _maximumWidth, value); }

    [Qml] public double MaximumHeight { get { Track(); return _maximumHeight; } set => Set(ref _maximumHeight, value); }

    [Qml]
    public bool FillWidth
    {
        get { Track(); return _fillWidth; }
        set
        {
            _fillWidthSet = true;
            Set(ref _fillWidth, value);
        }
    }

    [Qml]
    public bool FillHeight
    {
        get { Track(); return _fillHeight; }
        set
        {
            _fillHeightSet = true;
            Set(ref _fillHeight, value);
        }
    }

    public bool FillWidthSet => _fillWidthSet;

    public bool FillHeightSet => _fillHeightSet;

    [Qml] public int Alignment { get { Track(); return _alignment; } set => Set(ref _alignment, value); }

    [Qml] public double Margins { get { Track(); return _margins; } set => Set(ref _margins, value); }

    [Qml] public double LeftMargin { get { Track(); return _leftMargin ?? _margins; } set { _leftMargin = value; NotifyChanged("leftMargin"); Changed(); } }

    [Qml] public double RightMargin { get { Track(); return _rightMargin ?? _margins; } set { _rightMargin = value; NotifyChanged("rightMargin"); Changed(); } }

    [Qml] public double TopMargin { get { Track(); return _topMargin ?? _margins; } set { _topMargin = value; NotifyChanged("topMargin"); Changed(); } }

    [Qml] public double BottomMargin { get { Track(); return _bottomMargin ?? _margins; } set { _bottomMargin = value; NotifyChanged("bottomMargin"); Changed(); } }

    [Qml] public int Row { get { Track(); return _row; } set => Set(ref _row, value); }

    [Qml] public int Column { get { Track(); return _column; } set => Set(ref _column, value); }

    [Qml] public int RowSpan { get { Track(); return _rowSpan; } set => Set(ref _rowSpan, value); }

    [Qml] public int ColumnSpan { get { Track(); return _columnSpan; } set => Set(ref _columnSpan, value); }

    [Qml] public int HorizontalStretchFactor { get { Track(); return _horizontalStretchFactor; } set => Set(ref _horizontalStretchFactor, value); }

    [Qml] public int VerticalStretchFactor { get { Track(); return _verticalStretchFactor; } set => Set(ref _verticalStretchFactor, value); }
}

/// <summary>The base of RowLayout, ColumnLayout, GridLayout and StackLayout.</summary>
public abstract class LayoutBase : Item, IPolishable, IPropertyObserver
{
    private static readonly string[] Watched = ["implicitWidth", "implicitHeight", "visible", "width", "height"];
    private bool _arranging;
    private int _layoutDirection;
    private bool _uniformCellSizes;

    // Children are laid out in their order, which a Repeater changes when its model moves a row.
    protected LayoutBase() => Children.Changed += Invalidate;

    public static LayoutAttached? Attached(Item item) => item.FindAttached(typeof(LayoutAttached)) as LayoutAttached;

    [Qml]
    public int LayoutDirection
    {
        get { Track(); return _layoutDirection; }
        set
        {
            if (SetField(ref _layoutDirection, value))
                Invalidate();
        }
    }

    [Qml]
    public bool UniformCellSizes
    {
        get { Track(); return _uniformCellSizes; }
        set
        {
            if (SetField(ref _uniformCellSizes, value))
                Invalidate();
        }
    }

    protected override void OnChildAdded(Item child)
    {
        foreach (var p in Watched)
            child.AddObserver(p, this);
        if (child is Text text)
            text.LayoutControlsWidth = true;
        Invalidate();
    }

    protected override void OnChildRemoved(Item child)
    {
        foreach (var p in Watched)
            child.RemoveObserver(p, this);
        Invalidate();
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        // A layout sets its children's width and height itself; only their wishes matter.
        if (_arranging && property is "width" or "height")
            return;
        if (property is "width" or "height" && sender is Item { HasExplicitWidth: false, HasExplicitHeight: false })
            return;
        Invalidate();
    }

    protected override void OnGeometryChanged()
    {
        if (!_arranging)
            Invalidate();
    }

    public void Invalidate() => PolishQueue.Request(this, Engine?.Loop);

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Invalidate();
    }

    public void UpdatePolish()
    {
        if (IsDestroyed)
            return;
        _arranging = true;
        try
        {
            using (DependencyTracker.Suspend())
                Arrange();
        }
        finally
        {
            _arranging = false;
        }
    }

    protected List<Item> LaidOut() => ChildItems.Where(c => c.IsVisibleFlag).ToList();

    protected abstract void Arrange();

    /// <summary>The minimum, preferred and maximum size of an item along one axis, margins included.</summary>
    internal static (double Min, double Pref, double Max, bool Fill) Hints(Item item, bool horizontal)
    {
        var a = Attached(item);
        var margins = a is null ? 0 : horizontal ? a.LeftMargin + a.RightMargin : a.TopMargin + a.BottomMargin;
        double pref;
        if (a is not null && (horizontal ? a.PreferredWidth : a.PreferredHeight) >= 0)
            pref = horizontal ? a.PreferredWidth : a.PreferredHeight;
        else if (horizontal ? item.HasExplicitWidth : item.HasExplicitHeight)
            pref = horizontal ? item.Width : item.Height;
        else
            pref = horizontal ? item.ImplicitWidth : item.ImplicitHeight;
        var min = a is null ? 0 : horizontal ? a.MinimumWidth : a.MinimumHeight;
        var max = a is null ? double.PositiveInfinity : horizontal ? a.MaximumWidth : a.MaximumHeight;
        // An item that fills defaults to growing and shrinking freely; a fixed one keeps its preferred size.
        var fill = a is not null && (horizontal ? a.FillWidth : a.FillHeight);
        if (!fill)
        {
            max = Math.Min(max, Math.Max(pref, min));
            if (a is null || (horizontal ? a.MinimumWidth : a.MinimumHeight) == 0)
                min = Math.Min(pref, max);
        }
        pref = Math.Clamp(pref, min, Math.Max(min, max));
        return (min + margins, pref + margins, max + margins, fill);
    }

    /// <summary>Shares a length among items with the given hints, the way Qt Quick Layouts do.</summary>
    internal static double[] Distribute(IReadOnlyList<(double Min, double Pref, double Max, bool Fill)> hints, double available, IReadOnlyList<int> stretch)
    {
        var sizes = hints.Select(h => h.Pref).ToArray();
        var total = sizes.Sum();
        if (available > total)
        {
            var extra = available - total;
            var growable = Enumerable.Range(0, hints.Count).Where(i => hints[i].Fill && sizes[i] < hints[i].Max).ToList();
            while (extra > 0.001 && growable.Count > 0)
            {
                var weights = growable.Select(i => (double)Math.Max(1, stretch[i] > 0 ? stretch[i] : 1)).ToArray();
                var weightSum = weights.Sum();
                var used = 0.0;
                var next = new List<int>();
                for (var k = 0; k < growable.Count; k++)
                {
                    var i = growable[k];
                    var share = extra * weights[k] / weightSum;
                    var room = hints[i].Max - sizes[i];
                    var add = Math.Min(share, room);
                    sizes[i] += add;
                    used += add;
                    if (add < room - 0.001)
                        next.Add(i);
                }
                extra -= used;
                if (used < 0.001)
                    break;
                growable = next;
            }
        }
        else if (available < total)
        {
            var deficit = total - available;
            var shrinkable = Enumerable.Range(0, hints.Count).Where(i => sizes[i] > hints[i].Min).ToList();
            while (deficit > 0.001 && shrinkable.Count > 0)
            {
                var share = deficit / shrinkable.Count;
                var removed = 0.0;
                var next = new List<int>();
                foreach (var i in shrinkable)
                {
                    var room = sizes[i] - hints[i].Min;
                    var take = Math.Min(share, room);
                    sizes[i] -= take;
                    removed += take;
                    if (take < room - 0.001)
                        next.Add(i);
                }
                deficit -= removed;
                if (removed < 0.001)
                    break;
                shrinkable = next;
            }
        }
        return sizes;
    }

    /// <summary>Places an item in a cell, inside its margins, aligned as it asks.</summary>
    internal static void Place(Item item, double x, double y, double cellWidth, double cellHeight, bool horizontalLayout)
    {
        var a = Attached(item);
        var lm = a?.LeftMargin ?? 0;
        var rm = a?.RightMargin ?? 0;
        var tm = a?.TopMargin ?? 0;
        var bm = a?.BottomMargin ?? 0;
        var (minW, prefW, maxW, fillW) = Hints(item, true);
        var (minH, prefH, maxH, fillH) = Hints(item, false);
        var innerW = cellWidth - lm - rm;
        var innerH = cellHeight - tm - bm;
        var w = horizontalLayout ? innerW : fillW ? Math.Min(innerW, maxW - lm - rm) : Math.Min(prefW - lm - rm, innerW);
        var h = horizontalLayout ? (fillH ? Math.Min(innerH, maxH - tm - bm) : Math.Min(prefH - tm - bm, innerH)) : innerH;
        w = Math.Max(Math.Max(0, minW - lm - rm), w);
        h = Math.Max(Math.Max(0, minH - tm - bm), h);
        var align = a?.Alignment ?? 0;
        var ix = x + lm;
        var iy = y + tm;
        if ((align & QtEnums.AlignHCenter) != 0)
            ix += (innerW - w) / 2;
        else if ((align & QtEnums.AlignRight) != 0)
            ix += innerW - w;
        if ((align & QtEnums.AlignTop) != 0)
        {
        }
        else if ((align & QtEnums.AlignBottom) != 0)
        {
            iy += innerH - h;
        }
        else
        {
            // Qt Quick Layouts center vertically by default.
            iy += (innerH - h) / 2;
        }
        if (!horizontalLayout && (align & (QtEnums.AlignLeft | QtEnums.AlignHCenter | QtEnums.AlignRight)) == 0)
            ix = x + lm;
        item.SetLayoutX(Math.Round(ix));
        item.SetLayoutY(Math.Round(iy));
        item.SetLayoutWidth(Math.Max(0, w));
        item.SetLayoutHeight(Math.Max(0, h));
    }
}

/// <summary>A row or column layout.</summary>
public abstract class LinearLayout(bool horizontal) : LayoutBase
{
    private double _spacing = 5;

    [Qml]
    public double Spacing
    {
        get { Track(); return _spacing; }
        set
        {
            if (SetField(ref _spacing, value))
                Invalidate();
        }
    }

    protected override void Arrange()
    {
        var items = LaidOut();
        var main = items.Select(i => Hints(i, horizontal)).ToList();
        var cross = items.Select(i => Hints(i, !horizontal)).ToList();
        var spacing = _spacing * Math.Max(0, items.Count - 1);
        ImplicitWidth = horizontal ? main.Sum(h => h.Pref) + spacing : cross.Count == 0 ? 0 : cross.Max(h => h.Pref);
        ImplicitHeight = horizontal ? cross.Count == 0 ? 0 : cross.Max(h => h.Pref) : main.Sum(h => h.Pref) + spacing;
        var available = (horizontal ? Width : Height) - spacing;
        var stretch = items.Select(i => horizontal ? Attached(i)?.HorizontalStretchFactor ?? -1 : Attached(i)?.VerticalStretchFactor ?? -1).ToList();
        var sizes = Distribute(main, available, stretch);
        var pos = 0.0;
        var rtl = horizontal && LayoutDirection == 1;
        for (var i = 0; i < items.Count; i++)
        {
            if (horizontal)
            {
                var x = rtl ? Width - pos - sizes[i] : pos;
                Place(items[i], x, 0, sizes[i], Height, true);
            }
            else
            {
                Place(items[i], 0, pos, Width, sizes[i], false);
            }
            pos += sizes[i] + _spacing;
        }
    }
}

public sealed class RowLayout() : LinearLayout(true);

public sealed class ColumnLayout() : LinearLayout(false);

/// <summary>QtQuick.Layouts' <c>GridLayout</c>.</summary>
public sealed class GridLayout : LayoutBase
{
    private int _columns = -1;
    private int _rows = -1;
    private double _rowSpacing = 5;
    private double _columnSpacing = 5;
    private int _flow;

    [QmlEnum]
    public enum FlowValue
    {
        LeftToRight = 0,
        TopToBottom = 1,
    }

    [Qml] public int Columns { get { Track(); return _columns; } set { if (SetField(ref _columns, value)) Invalidate(); } }

    [Qml] public int Rows { get { Track(); return _rows; } set { if (SetField(ref _rows, value)) Invalidate(); } }

    [Qml] public double RowSpacing { get { Track(); return _rowSpacing; } set { if (SetField(ref _rowSpacing, value)) Invalidate(); } }

    [Qml] public double ColumnSpacing { get { Track(); return _columnSpacing; } set { if (SetField(ref _columnSpacing, value)) Invalidate(); } }

    [Qml] public int Flow { get { Track(); return _flow; } set { if (SetField(ref _flow, value)) Invalidate(); } }

    protected override void Arrange()
    {
        var items = LaidOut();
        // Assign cells, honoring Layout.row and Layout.column where set.
        var cells = new List<(Item Item, int Row, int Col, int RowSpan, int ColSpan)>();
        var occupied = new HashSet<(int, int)>();
        var columns = _columns > 0 ? _columns : int.MaxValue;
        var rowsLimit = _rows > 0 ? _rows : int.MaxValue;
        int r = 0, c = 0;
        foreach (var item in items)
        {
            var a = Attached(item);
            var rs = Math.Max(1, a?.RowSpan ?? 1);
            var cs = Math.Max(1, a?.ColumnSpan ?? 1);
            if (a is { Row: >= 0 } || a is { Column: >= 0 })
            {
                var row = a.Row >= 0 ? a.Row : r;
                var col = a.Column >= 0 ? a.Column : c;
                cells.Add((item, row, col, rs, cs));
                Occupy(row, col, rs, cs);
                continue;
            }
            while (occupied.Contains((r, c)))
                Advance();
            cells.Add((item, r, c, rs, cs));
            Occupy(r, c, rs, cs);
            Advance();
        }
        void Occupy(int row, int col, int rs, int cs)
        {
            for (var i = 0; i < rs; i++)
            {
                for (var j = 0; j < cs; j++)
                    occupied.Add((row + i, col + j));
            }
        }
        void Advance()
        {
            if (_flow == 0)
            {
                c++;
                if (c >= columns)
                {
                    c = 0;
                    r++;
                }
            }
            else
            {
                r++;
                if (r >= rowsLimit)
                {
                    r = 0;
                    c++;
                }
            }
        }
        var colCount = cells.Count == 0 ? 0 : cells.Max(x => x.Col + x.ColSpan);
        var rowCount = cells.Count == 0 ? 0 : cells.Max(x => x.Row + x.RowSpan);
        var colHints = Enumerable.Range(0, colCount).Select(_ => (Min: 0.0, Pref: 0.0, Max: 0.0, Fill: false)).ToArray();
        var rowHints = Enumerable.Range(0, rowCount).Select(_ => (Min: 0.0, Pref: 0.0, Max: 0.0, Fill: false)).ToArray();
        foreach (var (item, row, col, rs, cs) in cells)
        {
            var h = Hints(item, true);
            var v = Hints(item, false);
            if (cs == 1)
                colHints[col] = (Math.Max(colHints[col].Min, h.Min), Math.Max(colHints[col].Pref, h.Pref), Math.Max(colHints[col].Max, h.Max), colHints[col].Fill || h.Fill);
            if (rs == 1)
                rowHints[row] = (Math.Max(rowHints[row].Min, v.Min), Math.Max(rowHints[row].Pref, v.Pref), Math.Max(rowHints[row].Max, v.Max), rowHints[row].Fill || v.Fill);
        }
        var colSpacing = _columnSpacing * Math.Max(0, colCount - 1);
        var rowSpacing = _rowSpacing * Math.Max(0, rowCount - 1);
        ImplicitWidth = colHints.Sum(h => h.Pref) + colSpacing;
        ImplicitHeight = rowHints.Sum(h => h.Pref) + rowSpacing;
        var widths = Distribute(colHints, Width - colSpacing, colHints.Select(_ => -1).ToList());
        var heights = Distribute(rowHints, Height - rowSpacing, rowHints.Select(_ => -1).ToList());
        foreach (var (item, row, col, rs, cs) in cells)
        {
            var x = widths.Take(col).Sum() + _columnSpacing * col;
            var y = heights.Take(row).Sum() + _rowSpacing * row;
            var w = widths.Skip(col).Take(cs).Sum() + _columnSpacing * (cs - 1);
            var h = heights.Skip(row).Take(rs).Sum() + _rowSpacing * (rs - 1);
            PlaceInCell(item, x, y, w, h);
        }
    }

    private static void PlaceInCell(Item item, double x, double y, double w, double h)
    {
        var a = Attached(item);
        var (_, prefW, maxW, fillW) = Hints(item, true);
        var (_, prefH, maxH, fillH) = Hints(item, false);
        var lm = a?.LeftMargin ?? 0;
        var tm = a?.TopMargin ?? 0;
        var iw = fillW ? Math.Min(w, maxW) : Math.Min(prefW, w);
        var ih = fillH ? Math.Min(h, maxH) : Math.Min(prefH, h);
        var align = a?.Alignment ?? 0;
        var ix = x + lm;
        var iy = y + tm + (h - ih) / 2;
        if ((align & QtEnums.AlignHCenter) != 0)
            ix = x + (w - iw) / 2;
        else if ((align & QtEnums.AlignRight) != 0)
            ix = x + w - iw;
        if ((align & QtEnums.AlignTop) != 0)
            iy = y + tm;
        else if ((align & QtEnums.AlignBottom) != 0)
            iy = y + h - ih;
        item.SetLayoutX(Math.Round(ix));
        item.SetLayoutY(Math.Round(iy));
        item.SetLayoutWidth(iw - lm - (a?.RightMargin ?? 0));
        item.SetLayoutHeight(ih - tm - (a?.BottomMargin ?? 0));
    }
}

/// <summary>QtQuick.Layouts' <c>StackLayout</c>: shows one child at a time.</summary>
public sealed class StackLayout : LayoutBase
{
    private int _currentIndex;

    [Qml]
    public int CurrentIndex
    {
        get { Track(); return _currentIndex; }
        set
        {
            if (SetField(ref _currentIndex, value))
                Invalidate();
        }
    }

    [Qml]
    public int Count
    {
        get { Track(nameof(Children)); return ChildItems.Count; }
    }

    [Qml]
    public Item? ItemAt(int index) => index >= 0 && index < ChildItems.Count ? ChildItems[index] : null;

    protected override void Arrange()
    {
        var items = ChildItems;
        double w = 0, h = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            w = Math.Max(w, Hints(item, true).Pref);
            h = Math.Max(h, Hints(item, false).Pref);
            item.Visible = i == _currentIndex;
            item.SetLayoutX(0);
            item.SetLayoutY(0);
            item.SetLayoutWidth(Width);
            item.SetLayoutHeight(Height);
        }
        ImplicitWidth = w;
        ImplicitHeight = h;
    }
}
