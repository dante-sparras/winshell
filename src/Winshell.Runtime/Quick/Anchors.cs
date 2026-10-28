namespace Winshell.Runtime.Quick;

/// <summary>
/// An item's <c>anchors</c>: attaches its edges to the edges of its parent or siblings, and keeps them
/// attached as those move.
/// </summary>
public sealed class Anchors : QObject, IPropertyObserver
{
    private readonly Item _item;
    private AnchorLine? _left, _right, _top, _bottom, _horizontalCenter, _verticalCenter, _baseline;
    private Item? _fill, _centerIn;
    private double _margins;
    private double? _leftMargin, _rightMargin, _topMargin, _bottomMargin;
    private double _horizontalCenterOffset, _verticalCenterOffset, _baselineOffset;
    private bool _alignWhenCentered = true;
    private bool _active;
    private readonly List<(QObject Object, string Property)> _watched = [];
    private bool _updating;

    public Anchors(Item item)
    {
        _item = item;
    }

    public bool IsActive => _left is not null || _right is not null || _top is not null || _bottom is not null
        || _horizontalCenter is not null || _verticalCenter is not null || _baseline is not null || _fill is not null || _centerIn is not null;

    public bool AnchorsHorizontally => _left is not null || _right is not null || _horizontalCenter is not null || _fill is not null || _centerIn is not null;

    public bool AnchorsVertically => _top is not null || _bottom is not null || _verticalCenter is not null || _baseline is not null || _fill is not null || _centerIn is not null;

    /// <summary>Whether the anchors decide the width, so a layout must leave it alone.</summary>
    public bool ControlsWidth => _fill is not null || (_left is not null && _right is not null);

    public bool ControlsHeight => _fill is not null || (_top is not null && _bottom is not null);

    [Qml]
    public AnchorLine? Left { get { Track(); return _left; } set => SetLine(ref _left, value); }

    [Qml]
    public AnchorLine? Right { get { Track(); return _right; } set => SetLine(ref _right, value); }

    [Qml]
    public AnchorLine? Top { get { Track(); return _top; } set => SetLine(ref _top, value); }

    [Qml]
    public AnchorLine? Bottom { get { Track(); return _bottom; } set => SetLine(ref _bottom, value); }

    [Qml]
    public AnchorLine? HorizontalCenter { get { Track(); return _horizontalCenter; } set => SetLine(ref _horizontalCenter, value); }

    [Qml]
    public AnchorLine? VerticalCenter { get { Track(); return _verticalCenter; } set => SetLine(ref _verticalCenter, value); }

    [Qml]
    public AnchorLine? Baseline { get { Track(); return _baseline; } set => SetLine(ref _baseline, value); }

    [Qml]
    public Item? Fill
    {
        get { Track(); return _fill; }
        set
        {
            if (SetField(ref _fill, value))
                Changed();
        }
    }

    [Qml]
    public Item? CenterIn
    {
        get { Track(); return _centerIn; }
        set
        {
            if (SetField(ref _centerIn, value))
                Changed();
        }
    }

    [Qml]
    public double Margins
    {
        get { Track(); return _margins; }
        set
        {
            if (SetField(ref _margins, value))
                Changed();
        }
    }

    [Qml]
    public double LeftMargin { get { Track(); return _leftMargin ?? _margins; } set { _leftMargin = value; NotifyChanged("leftMargin"); Changed(); } }

    [Qml]
    public double RightMargin { get { Track(); return _rightMargin ?? _margins; } set { _rightMargin = value; NotifyChanged("rightMargin"); Changed(); } }

    [Qml]
    public double TopMargin { get { Track(); return _topMargin ?? _margins; } set { _topMargin = value; NotifyChanged("topMargin"); Changed(); } }

    [Qml]
    public double BottomMargin { get { Track(); return _bottomMargin ?? _margins; } set { _bottomMargin = value; NotifyChanged("bottomMargin"); Changed(); } }

    [Qml]
    public double HorizontalCenterOffset
    {
        get { Track(); return _horizontalCenterOffset; }
        set
        {
            if (SetField(ref _horizontalCenterOffset, value))
                Changed();
        }
    }

    [Qml]
    public double VerticalCenterOffset
    {
        get { Track(); return _verticalCenterOffset; }
        set
        {
            if (SetField(ref _verticalCenterOffset, value))
                Changed();
        }
    }

    [Qml]
    public double BaselineOffset
    {
        get { Track(); return _baselineOffset; }
        set
        {
            if (SetField(ref _baselineOffset, value))
                Changed();
        }
    }

    [Qml]
    public bool AlignWhenCentered
    {
        get { Track(); return _alignWhenCentered; }
        set => SetField(ref _alignWhenCentered, value);
    }

    private void SetLine(ref AnchorLine? field, AnchorLine? value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (field == value)
            return;
        field = value;
        NotifyChanged(Names.ToQml(name));
        Changed();
    }

    /// <summary>Starts following the anchors once the item's component is complete.</summary>
    internal void Activate()
    {
        _active = true;
        Changed();
    }

    internal void Detach()
    {
        foreach (var (obj, prop) in _watched)
            obj.RemoveObserver(prop, this);
        _watched.Clear();
        _active = false;
    }

    private void Changed()
    {
        if (!_active && !_item.IsComplete)
            return;
        _active = true;
        Rewatch();
        UpdateGeometry();
    }

    private void Rewatch()
    {
        foreach (var (obj, prop) in _watched)
            obj.RemoveObserver(prop, this);
        _watched.Clear();
        void Watch(QObject? obj, params string[] props)
        {
            if (obj is null)
                return;
            foreach (var p in props)
            {
                obj.AddObserver(p, this);
                _watched.Add((obj, p));
            }
        }
        foreach (var target in new[] { _left?.Item, _right?.Item, _horizontalCenter?.Item, _fill, _centerIn })
        {
            if (target is not null)
                Watch(target, "x", "width");
        }
        foreach (var target in new[] { _top?.Item, _bottom?.Item, _verticalCenter?.Item, _baseline?.Item, _fill, _centerIn })
        {
            if (target is not null)
                Watch(target, "y", "height");
        }
        Watch(_item, "width", "height", "implicitWidth", "implicitHeight", "parent");
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        if (sender == _item && property == "parent")
            Rewatch();
        UpdateGeometry();
    }

    /// <summary>Where a line of a target item is, in the coordinates of this item's parent.</summary>
    private double Position(Item target, AnchorEdge edge)
    {
        var parent = _item.ParentItem;
        double origin;
        double size;
        bool horizontal = edge is AnchorEdge.Left or AnchorEdge.Right or AnchorEdge.HorizontalCenter;
        using (DependencyTracker.Suspend())
        {
            if (target == parent)
            {
                origin = 0;
            }
            else if (target.ParentItem == parent)
            {
                origin = horizontal ? target.X : target.Y;
            }
            else
            {
                var scene = target.MapToScene(0, 0);
                var local = parent?.MapFromScene(scene.X, scene.Y) ?? scene;
                origin = horizontal ? local.X : local.Y;
            }
            size = horizontal ? target.Width : target.Height;
            return edge switch
            {
                AnchorEdge.Left or AnchorEdge.Top => origin,
                AnchorEdge.Right or AnchorEdge.Bottom => origin + size,
                AnchorEdge.HorizontalCenter or AnchorEdge.VerticalCenter => origin + size / 2,
                AnchorEdge.Baseline => origin + target.BaselineOffset,
                _ => origin,
            };
        }
    }

    private void UpdateGeometry()
    {
        if (_updating || _item.IsDestroyed || !IsActive)
            return;
        _updating = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            UpdateHorizontal();
            UpdateVertical();
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateHorizontal()
    {
        var left = _fill is not null ? new AnchorLine(_fill, AnchorEdge.Left) : _left;
        var right = _fill is not null ? new AnchorLine(_fill, AnchorEdge.Right) : _right;
        var center = _centerIn is not null ? new AnchorLine(_centerIn, AnchorEdge.HorizontalCenter) : _horizontalCenter;
        var lm = LeftMargin;
        var rm = RightMargin;
        if (left is not null && right is not null)
        {
            var l = Position(left.Item, left.Edge) + lm;
            var r = Position(right.Item, right.Edge) - rm;
            _item.SetLayoutX(l);
            _item.SetLayoutWidth(Math.Max(0, r - l));
        }
        else if (left is not null)
        {
            var l = Position(left.Item, left.Edge) + lm;
            _item.SetLayoutX(l);
            if (center is not null)
                _item.SetLayoutWidth(Math.Max(0, (Position(center.Item, center.Edge) + _horizontalCenterOffset - l) * 2));
        }
        else if (right is not null)
        {
            var r = Position(right.Item, right.Edge) - rm;
            if (center is not null)
            {
                var c = Position(center.Item, center.Edge) + _horizontalCenterOffset;
                _item.SetLayoutWidth(Math.Max(0, (r - c) * 2));
            }
            _item.SetLayoutX(r - _item.Width);
        }
        else if (center is not null)
        {
            var x = Position(center.Item, center.Edge) + _horizontalCenterOffset - _item.Width / 2;
            _item.SetLayoutX(_alignWhenCentered ? Math.Round(x) : x);
        }
    }

    private void UpdateVertical()
    {
        var top = _fill is not null ? new AnchorLine(_fill, AnchorEdge.Top) : _top;
        var bottom = _fill is not null ? new AnchorLine(_fill, AnchorEdge.Bottom) : _bottom;
        var center = _centerIn is not null ? new AnchorLine(_centerIn, AnchorEdge.VerticalCenter) : _verticalCenter;
        var tm = TopMargin;
        var bm = BottomMargin;
        if (top is not null && bottom is not null)
        {
            var t = Position(top.Item, top.Edge) + tm;
            var b = Position(bottom.Item, bottom.Edge) - bm;
            _item.SetLayoutY(t);
            _item.SetLayoutHeight(Math.Max(0, b - t));
        }
        else if (top is not null)
        {
            var t = Position(top.Item, top.Edge) + tm;
            _item.SetLayoutY(t);
            if (center is not null)
                _item.SetLayoutHeight(Math.Max(0, (Position(center.Item, center.Edge) + _verticalCenterOffset - t) * 2));
        }
        else if (bottom is not null)
        {
            var b = Position(bottom.Item, bottom.Edge) - bm;
            _item.SetLayoutY(b - _item.Height);
        }
        else if (center is not null)
        {
            var y = Position(center.Item, center.Edge) + _verticalCenterOffset - _item.Height / 2;
            _item.SetLayoutY(_alignWhenCentered ? Math.Round(y) : y);
        }
        else if (_baseline is not null)
        {
            _item.SetLayoutY(Position(_baseline.Item, _baseline.Edge) + _baselineOffset - _item.BaselineOffset);
        }
    }
}
