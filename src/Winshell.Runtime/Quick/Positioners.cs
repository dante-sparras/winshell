namespace Winshell.Runtime.Quick;

/// <summary>The base of Row, Column, Grid and Flow: watches its children and arranges them.</summary>
[QmlSignal("positioningComplete")]
public abstract class Positioner : Item, IPolishable, IPropertyObserver
{
    private double _spacing;
    private double _padding;
    private double? _topPadding, _bottomPadding, _leftPadding, _rightPadding;
    private int _layoutDirection;
    private static readonly string[] Watched = ["width", "height", "implicitWidth", "implicitHeight", "visible"];

    // Children are placed in their order, which a Repeater changes when its model moves a row.
    protected Positioner() => Children.Changed += Invalidate;

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

    [Qml]
    public double Padding
    {
        get { Track(); return _padding; }
        set
        {
            if (SetField(ref _padding, value))
                Invalidate();
        }
    }

    [Qml]
    public double TopPadding { get { Track(); return _topPadding ?? _padding; } set { _topPadding = value; NotifyChanged("topPadding"); Invalidate(); } }

    [Qml]
    public double BottomPadding { get { Track(); return _bottomPadding ?? _padding; } set { _bottomPadding = value; NotifyChanged("bottomPadding"); Invalidate(); } }

    [Qml]
    public double LeftPadding { get { Track(); return _leftPadding ?? _padding; } set { _leftPadding = value; NotifyChanged("leftPadding"); Invalidate(); } }

    [Qml]
    public double RightPadding { get { Track(); return _rightPadding ?? _padding; } set { _rightPadding = value; NotifyChanged("rightPadding"); Invalidate(); } }

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
    public int EffectiveLayoutDirection => LayoutDirection;

    [Qml]
    public QObject? Add { get; set; }

    [Qml]
    public QObject? Move { get; set; }

    [Qml]
    public QObject? Populate { get; set; }

    [Qml]
    public void ForceLayout()
    {
        using (DependencyTracker.Suspend())
            Arrange();
    }

    protected override void OnChildAdded(Item child)
    {
        foreach (var p in Watched)
            child.AddObserver(p, this);
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
        if (!_arranging)
            Invalidate();
    }

    protected override void OnGeometryChanged()
    {
        if (!_arranging)
            Invalidate();
    }

    protected void Invalidate() => PolishQueue.Request(this, Engine?.Loop);

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Invalidate();
    }

    private bool _arranging;

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
        Emit("positioningComplete");
    }

    /// <summary>The children a positioner places: visible ones with a size.</summary>
    protected List<Item> Placed() =>
        ChildItems.Where(c => c.IsVisibleFlag && c.Width > 0 && c.Height > 0).ToList();

    /// <summary>Qt's right-to-left layout is the left-to-right one mirrored, which also swaps left and right alignment.</summary>
    protected void ApplyLayoutDirection(IReadOnlyList<Item> items)
    {
        if (LayoutDirection != 1)
            return;
        var end = HasExplicitWidth ? Width : ImplicitWidth;
        foreach (var item in items)
            item.SetLayoutX(end - RightPadding - (item.X - LeftPadding) - item.Width);
    }

    protected abstract void Arrange();
}

/// <summary>QtQuick's <c>Row</c>.</summary>
public sealed class Row : Positioner
{
    protected override void Arrange()
    {
        var items = Placed();
        var x = LeftPadding;
        var height = 0.0;
        var rtl = LayoutDirection == 1;
        foreach (var item in items)
            height = Math.Max(height, item.Height);
        var total = items.Sum(i => i.Width) + Spacing * Math.Max(0, items.Count - 1);
        foreach (var item in items)
        {
            item.SetLayoutX(rtl ? LeftPadding + total - (x - LeftPadding) - item.Width : x);
            item.SetLayoutY(TopPadding);
            x += item.Width + Spacing;
        }
        ImplicitWidth = LeftPadding + total + RightPadding;
        ImplicitHeight = TopPadding + height + BottomPadding;
    }
}

/// <summary>QtQuick's <c>Column</c>.</summary>
public sealed class Column : Positioner
{
    protected override void Arrange()
    {
        var items = Placed();
        var y = TopPadding;
        var width = 0.0;
        foreach (var item in items)
        {
            item.SetLayoutY(y);
            item.SetLayoutX(LeftPadding);
            y += item.Height + Spacing;
            width = Math.Max(width, item.Width);
        }
        var total = items.Sum(i => i.Height) + Spacing * Math.Max(0, items.Count - 1);
        ImplicitWidth = LeftPadding + width + RightPadding;
        ImplicitHeight = TopPadding + total + BottomPadding;
    }
}

/// <summary>QtQuick's <c>Grid</c>.</summary>
public sealed class Grid : Positioner
{
    private int _rows = -1;
    private int _columns = -1;
    private double? _rowSpacing, _columnSpacing;
    private int _flow;
    private int _horizontalItemAlignment = QtEnums.AlignLeft;
    private int _verticalItemAlignment = QtEnums.AlignTop;

    [QmlEnum]
    public enum FlowValue
    {
        LeftToRight = 0,
        TopToBottom = 1,
    }

    [Qml]
    public int Rows { get { Track(); return _rows; } set { if (SetField(ref _rows, value)) Invalidate(); } }

    [Qml]
    public int Columns { get { Track(); return _columns; } set { if (SetField(ref _columns, value)) Invalidate(); } }

    [Qml]
    public double RowSpacing { get { Track(); return _rowSpacing ?? Spacing; } set { _rowSpacing = value; NotifyChanged("rowSpacing"); Invalidate(); } }

    [Qml]
    public double ColumnSpacing { get { Track(); return _columnSpacing ?? Spacing; } set { _columnSpacing = value; NotifyChanged("columnSpacing"); Invalidate(); } }

    [Qml]
    public int Flow { get { Track(); return _flow; } set { if (SetField(ref _flow, value)) Invalidate(); } }

    [Qml]
    public int HorizontalItemAlignment { get { Track(); return _horizontalItemAlignment; } set { if (SetField(ref _horizontalItemAlignment, value)) Invalidate(); } }

    [Qml]
    public int VerticalItemAlignment { get { Track(); return _verticalItemAlignment; } set { if (SetField(ref _verticalItemAlignment, value)) Invalidate(); } }

    protected override void Arrange()
    {
        var items = Placed();
        var count = items.Count;
        int columns, rows;
        if (_columns > 0)
        {
            columns = _columns;
            rows = _rows > 0 ? _rows : (count + columns - 1) / Math.Max(1, columns);
        }
        else if (_rows > 0)
        {
            rows = _rows;
            columns = (count + rows - 1) / Math.Max(1, rows);
        }
        else
        {
            columns = 4;
            rows = (count + 3) / 4;
        }
        var colWidths = new double[Math.Max(columns, 1)];
        var rowHeights = new double[Math.Max(rows, 1)];
        var placed = new List<Item>(count);
        for (var i = 0; i < count; i++)
        {
            var (r, c) = Cell(i, rows, columns);
            if (r >= rows || c >= columns)
                continue;
            colWidths[c] = Math.Max(colWidths[c], items[i].Width);
            rowHeights[r] = Math.Max(rowHeights[r], items[i].Height);
        }
        for (var i = 0; i < count; i++)
        {
            var (r, c) = Cell(i, rows, columns);
            if (r >= rows || c >= columns)
                continue;
            var x = LeftPadding + colWidths.Take(c).Sum() + ColumnSpacing * c;
            var y = TopPadding + rowHeights.Take(r).Sum() + RowSpacing * r;
            var item = items[i];
            if ((_horizontalItemAlignment & QtEnums.AlignHCenter) != 0)
                x += (colWidths[c] - item.Width) / 2;
            else if ((_horizontalItemAlignment & QtEnums.AlignRight) != 0)
                x += colWidths[c] - item.Width;
            if ((_verticalItemAlignment & QtEnums.AlignVCenter) != 0)
                y += (rowHeights[r] - item.Height) / 2;
            else if ((_verticalItemAlignment & QtEnums.AlignBottom) != 0)
                y += rowHeights[r] - item.Height;
            item.SetLayoutX(x);
            item.SetLayoutY(y);
            placed.Add(item);
        }
        ImplicitWidth = LeftPadding + colWidths.Sum() + ColumnSpacing * Math.Max(0, columns - 1) + RightPadding;
        ImplicitHeight = TopPadding + rowHeights.Sum() + RowSpacing * Math.Max(0, rows - 1) + BottomPadding;
        ApplyLayoutDirection(placed);
    }

    private (int Row, int Column) Cell(int index, int rows, int columns) =>
        _flow == (int)FlowValue.TopToBottom ? (index % rows, index / rows) : (index / columns, index % columns);
}

/// <summary>QtQuick's <c>Flow</c>: places items in rows, wrapping at its width.</summary>
public sealed class Flow : Positioner
{
    private int _flow;

    [QmlEnum]
    public enum FlowValue
    {
        LeftToRight = 0,
        TopToBottom = 1,
    }

    [Qml("flow")]
    public int FlowDirection { get { Track(nameof(Flow)); return _flow; } set { if (SetField(ref _flow, value, nameof(Flow))) Invalidate(); } }

    protected override void Arrange()
    {
        var items = Placed();
        double x = LeftPadding, y = TopPadding, lineSize = 0, maxX = 0, maxY = 0;
        if (_flow == 0)
        {
            var limit = Width > 0 ? Width - RightPadding : double.MaxValue;
            foreach (var item in items)
            {
                if (x > LeftPadding && x + item.Width > limit)
                {
                    x = LeftPadding;
                    y += lineSize + Spacing;
                    lineSize = 0;
                }
                item.SetLayoutX(x);
                item.SetLayoutY(y);
                x += item.Width + Spacing;
                lineSize = Math.Max(lineSize, item.Height);
                maxX = Math.Max(maxX, x - Spacing);
            }
            ImplicitWidth = maxX + RightPadding;
            ImplicitHeight = y + lineSize + BottomPadding;
        }
        else
        {
            var limit = Height > 0 ? Height - BottomPadding : double.MaxValue;
            foreach (var item in items)
            {
                if (y > TopPadding && y + item.Height > limit)
                {
                    y = TopPadding;
                    x += lineSize + Spacing;
                    lineSize = 0;
                }
                item.SetLayoutX(x);
                item.SetLayoutY(y);
                y += item.Height + Spacing;
                lineSize = Math.Max(lineSize, item.Width);
                maxY = Math.Max(maxY, y - Spacing);
            }
            ImplicitWidth = x + lineSize + RightPadding;
            ImplicitHeight = maxY + BottomPadding;
        }
        ApplyLayoutDirection(items);
    }
}
