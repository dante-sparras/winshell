namespace Winshell.Runtime.Quick;

/// <summary>An object that takes any property name, such as PropertyChanges and ListElement.</summary>
public interface IAcceptsAnyProperty;

/// <summary>QtQuick's <c>Flickable</c>: a viewport over a larger content item, scrolled by wheel and drag.</summary>
[QmlDefaultProperty("flickableData")]
[QmlSignal("movementStarted")]
[QmlSignal("movementEnded")]
[QmlSignal("flickStarted")]
[QmlSignal("flickEnded")]
public class Flickable : Item, IPointerTarget, IWheelTarget, IPropertyObserver
{
    private double _contentWidth = -1;
    private double _contentHeight = -1;
    private double _contentX, _contentY;
    private bool _interactive = true;
    private int _boundsBehavior = 3;
    private int _flickableDirection;
    private bool _movingX, _movingY;
    private double _pressX, _pressY, _pressContentX, _pressContentY;
    private bool _draggingX, _draggingY;
    private double _topMargin, _bottomMargin, _leftMargin, _rightMargin;
    private double _flickDeceleration = 1500;
    private double _maximumFlickVelocity = 2500;
    private IDisposable? _scrollAnimation;
    private double _targetX, _targetY;
    private readonly FlickableData _flickableData;

    public Flickable()
    {
        ContentItem = new Item();
        ContentItem.SetParentItem(this);
        ContentItem.AddObserver("childrenRect", this);
        Clip = false;
        _flickableData = new FlickableData(this);
        VisibleArea = new VisibleAreaGroup(this);
    }

    [QmlEnum]
    public enum BoundsBehaviorValue
    {
        StopAtBounds = 0,
        DragOverBounds = 1,
        OvershootBounds = 2,
        DragAndOvershootBounds = 3,
    }

    [QmlEnum]
    public enum FlickableDirectionValue
    {
        AutoFlickDirection = 0,
        HorizontalFlick = 1,
        VerticalFlick = 2,
        HorizontalAndVerticalFlick = 3,
        AutoFlickIfNeeded = 0xc,
    }

    [Qml]
    public Item ContentItem { get; }

    [Qml]
    public FlickableData FlickableData => _flickableData;

    [Qml]
    public double ContentWidth
    {
        get { Track(); return _contentWidth < 0 ? Width : _contentWidth; }
        set
        {
            if (SetField(ref _contentWidth, value))
            {
                ContentItem.SetLayoutWidth(ContentWidth);
                Clamp();
            }
        }
    }

    [Qml]
    public double ContentHeight
    {
        get { Track(); return _contentHeight < 0 ? Height : _contentHeight; }
        set
        {
            if (SetField(ref _contentHeight, value))
            {
                ContentItem.SetLayoutHeight(ContentHeight);
                Clamp();
            }
        }
    }

    [Qml]
    public double ContentX
    {
        get { Track(); return _contentX; }
        set => SetContentX(value);
    }

    [Qml]
    public double ContentY
    {
        get { Track(); return _contentY; }
        set => SetContentY(value);
    }

    [Qml] public double TopMargin { get { Track(); return _topMargin; } set => SetField(ref _topMargin, value); }

    [Qml] public double BottomMargin { get { Track(); return _bottomMargin; } set => SetField(ref _bottomMargin, value); }

    [Qml] public double LeftMargin { get { Track(); return _leftMargin; } set => SetField(ref _leftMargin, value); }

    [Qml] public double RightMargin { get { Track(); return _rightMargin; } set => SetField(ref _rightMargin, value); }

    [Qml] public double OriginX => 0;

    [Qml] public double OriginY => 0;

    [Qml]
    public bool Interactive { get { Track(); return _interactive; } set => SetField(ref _interactive, value); }

    [Qml]
    public int BoundsBehavior { get { Track(); return _boundsBehavior; } set => SetField(ref _boundsBehavior, value); }

    [Qml]
    public int BoundsMovement { get; set; }

    [Qml]
    public int FlickableDirection { get { Track(); return _flickableDirection; } set => SetField(ref _flickableDirection, value); }

    [Qml]
    public double FlickDeceleration { get { Track(); return _flickDeceleration; } set => SetField(ref _flickDeceleration, value); }

    [Qml]
    public double MaximumFlickVelocity { get { Track(); return _maximumFlickVelocity; } set => SetField(ref _maximumFlickVelocity, value); }

    [Qml]
    public bool PixelAligned { get; set; }

    [Qml]
    public bool Synchronous { get; set; }

    [Qml]
    public int PressDelay { get; set; }

    [Qml] public bool Moving { get { Track(); return _movingX || _movingY; } }

    [Qml] public bool MovingVertically { get { Track(); return _movingY; } }

    [Qml] public bool MovingHorizontally { get { Track(); return _movingX; } }

    [Qml] public bool Flicking => false;

    [Qml] public bool Dragging { get { Track(); return IsDragging; } }

    [Qml] public bool DraggingVertically { get { Track(); return _draggingY; } }

    [Qml] public bool DraggingHorizontally { get { Track(); return _draggingX; } }

    [Qml] public double HorizontalVelocity => 0;

    [Qml] public double VerticalVelocity => 0;

    [Qml] public bool AtXBeginning { get { Track("contentX"); return _contentX <= MinX + 0.5; } }

    [Qml] public bool AtXEnd { get { Track("contentX"); return _contentX >= MaxX - 0.5; } }

    [Qml] public bool AtYBeginning { get { Track("contentY"); return _contentY <= MinY + 0.5; } }

    [Qml] public bool AtYEnd { get { Track("contentY"); return _contentY >= MaxY - 0.5; } }

    [Qml]
    public VisibleAreaGroup VisibleArea { get; }

    private double MinX => -_leftMargin;

    private double MinY => -_topMargin;

    private double MaxX => Math.Max(MinX, ContentWidthValue - Width + _rightMargin);

    private double MaxY => Math.Max(MinY, ContentHeightValue - Height + _bottomMargin);

    private double ContentWidthValue
    {
        get
        {
            using var _ = DependencyTracker.Suspend();
            return _contentWidth < 0 ? Width : _contentWidth;
        }
    }

    private double ContentHeightValue
    {
        get
        {
            using var _ = DependencyTracker.Suspend();
            return _contentHeight < 0 ? Height : _contentHeight;
        }
    }

    private bool CanScrollX => _flickableDirection switch
    {
        1 or 3 => true,
        2 => false,
        _ => ContentWidthValue > Width,
    };

    private bool CanScrollY => _flickableDirection switch
    {
        2 or 3 => true,
        1 => false,
        _ => ContentHeightValue > Height || _flickableDirection == 0,
    };

    protected void SetContentX(double value)
    {
        if (SetField(ref _contentX, value, nameof(ContentX)))
        {
            ContentItem.SetLayoutX(-value);
            NotifyChanged("atXBeginning");
            NotifyChanged("atXEnd");
            VisibleArea.Changed();
            OnContentMoved();
        }
    }

    protected void SetContentY(double value)
    {
        if (SetField(ref _contentY, value, nameof(ContentY)))
        {
            ContentItem.SetLayoutY(-value);
            NotifyChanged("atYBeginning");
            NotifyChanged("atYEnd");
            VisibleArea.Changed();
            OnContentMoved();
        }
    }

    protected virtual void OnContentMoved()
    {
    }

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        if (_contentWidth < 0)
            ContentItem.SetLayoutWidth(Width);
        if (_contentHeight < 0)
            ContentItem.SetLayoutHeight(Height);
        VisibleArea.Changed();
        Clamp();
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
    }

    private void Clamp()
    {
        if (IsDragging)
            return;
        SetContentX(Math.Clamp(_contentX, MinX, MaxX));
        SetContentY(Math.Clamp(_contentY, MinY, MaxY));
    }

    [Qml]
    public void ReturnToBounds() => Clamp();

    [Qml]
    public void PositionViewAtBeginning() => SetContentY(MinY);

    [Qml]
    public void PositionViewAtEnd() => SetContentY(MaxY);

    [Qml]
    public void Flick(double xVelocity, double yVelocity)
    {
        ScrollBy(-xVelocity * 0.3, -yVelocity * 0.3, animate: true);
    }

    [Qml]
    public void CancelFlick()
    {
        _scrollAnimation?.Dispose();
        _scrollAnimation = null;
    }

    [Qml]
    public void ResizeContent(double width, double height, object? center)
    {
        ContentWidth = width;
        ContentHeight = height;
    }

    private bool IsDragging => _draggingX || _draggingY;

    private void SetMoving(bool x, bool y)
    {
        var was = _movingX || _movingY;
        SetAxis(ref _movingX, x, "movingHorizontally");
        SetAxis(ref _movingY, y, "movingVertically");
        if (was == (x || y))
            return;
        NotifyChanged("moving");
        Emit(x || y ? "movementStarted" : "movementEnded");
    }

    private void SetDragging(bool x, bool y)
    {
        var was = IsDragging;
        SetAxis(ref _draggingX, x, "draggingHorizontally");
        SetAxis(ref _draggingY, y, "draggingVertically");
        if (was != IsDragging)
            NotifyChanged("dragging");
    }

    private void SetAxis(ref bool field, bool value, string property)
    {
        if (field == value)
            return;
        field = value;
        NotifyChanged(property);
    }

    /// <summary>Scrolls by an amount, smoothly the way a Windows list scrolls by the wheel.</summary>
    public void ScrollBy(double dx, double dy, bool animate)
    {
        if (!animate || Engine is null)
        {
            SetContentX(Math.Clamp(_contentX + dx, MinX, MaxX));
            SetContentY(Math.Clamp(_contentY + dy, MinY, MaxY));
            return;
        }
        if (_scrollAnimation is null)
        {
            _targetX = _contentX;
            _targetY = _contentY;
        }
        _targetX = Math.Clamp(_targetX + dx, MinX, MaxX);
        _targetY = Math.Clamp(_targetY + dy, MinY, MaxY);
        SetMoving(_movingX || dx != 0, _movingY || dy != 0);
        if (_scrollAnimation is null)
            StepScroll();
    }

    private void StepScroll()
    {
        _scrollAnimation = null;
        if (IsDestroyed || Engine is null)
            return;
        var again = true;
        try
        {
            // The content can shrink while it scrolls, so the target is clamped again on every step.
            _targetX = Math.Clamp(_targetX, MinX, MaxX);
            _targetY = Math.Clamp(_targetY, MinY, MaxY);
            var nx = _contentX + (_targetX - _contentX) * 0.3;
            var ny = _contentY + (_targetY - _contentY) * 0.3;
            if (Math.Abs(_targetX - nx) < 0.5 && Math.Abs(_targetY - ny) < 0.5)
            {
                SetContentX(_targetX);
                SetContentY(_targetY);
                SetMoving(false, false);
                again = false;
                return;
            }
            SetContentX(nx);
            SetContentY(ny);
        }
        finally
        {
            if (again && !IsDestroyed && Engine is not null)
                _scrollAnimation = Engine.Loop.Schedule(AnimationClock.FrameInterval, StepScroll);
        }
    }

    public bool AcceptsButton(int button) => _interactive && Enabled && button == QtEnums.LeftButton;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        CancelFlick();
        var scene = MapToScene(x, y);
        _pressX = scene.X;
        _pressY = scene.Y;
        _pressContentX = _contentX;
        _pressContentY = _contentY;
        return true;
    }

    public bool WantsToSteal(double sceneDx, double sceneDy)
    {
        if (!_interactive)
            return false;
        const double threshold = 10;
        return (CanScrollY && Math.Abs(sceneDy) > threshold && ContentHeightValue > Height)
            || (CanScrollX && Math.Abs(sceneDx) > threshold && ContentWidthValue > Width);
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        var scene = MapToScene(x, y);
        var dx = scene.X - _pressX;
        var dy = scene.Y - _pressY;
        if (!IsDragging && Math.Sqrt(dx * dx + dy * dy) < 4)
            return;
        if (!IsDragging)
        {
            SetDragging(CanScrollX, CanScrollY);
            SetMoving(CanScrollX, CanScrollY);
        }
        if (CanScrollX)
            SetContentX(Math.Clamp(_pressContentX - dx, MinX, MaxX));
        if (CanScrollY)
            SetContentY(Math.Clamp(_pressContentY - dy, MinY, MaxY));
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        if (IsDragging)
        {
            SetDragging(false, false);
            SetMoving(false, false);
        }
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel() => PointerRelease(0, 0, 0, 0, 0);

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted)
    {
        // Ctrl+wheel zooms in Windows, so it is left for whatever handles that.
        if (!_interactive || (modifiers & QtEnums.ControlModifier) != 0)
            return false;
        var canY = CanScrollY && ContentHeightValue > Height;
        var canX = CanScrollX && ContentWidthValue > Width;
        // Shift+wheel, or the wheel over content that only scrolls sideways, scrolls sideways.
        if (angleX == 0 && pixelX == 0 && ((modifiers & QtEnums.ShiftModifier) != 0 || (!canY && canX)))
        {
            (angleX, angleY) = (angleY, 0);
            (pixelX, pixelY) = (pixelY, 0);
        }
        var dy = pixelY != 0 ? -pixelY : -angleY / 120 * WheelStep(Height);
        var dx = pixelX != 0 ? -pixelX : -angleX / 120 * WheelStep(Width);
        if ((dy != 0 && canY && ((dy < 0 && _contentY > MinY) || (dy > 0 && _contentY < MaxY)))
            || (dx != 0 && canX && ((dx < 0 && _contentX > MinX) || (dx > 0 && _contentX < MaxX))))
        {
            ScrollBy(canX ? dx : 0, canY ? dy : 0, animate: true);
            return true;
        }
        return false;
    }

    /// <summary>
    /// How far a wheel notch scrolls a viewport this long: the lines Windows sets, 16 px each so the default three
    /// lines are 48 px, or the whole viewport when Windows scrolls a page.
    /// </summary>
    private static double WheelStep(double viewport)
    {
        var lines = Theme.Current.WheelScrollLines;
        return lines == Theme.WheelPageScroll ? viewport : lines * 16.0;
    }

    protected override void OnDestroy()
    {
        CancelFlick();
        base.OnDestroy();
    }
}

/// <summary>A Flickable's default list: items go into its content item.</summary>
public sealed class FlickableData(Flickable owner) : IQmlList
{
    public int Count => owner.ContentItem.Data.Count;

    public object? this[int index] => owner.ContentItem.Data[index];

    public Type ElementType => typeof(QObject);

    public event Action? Changed;

    public void Append(object? item)
    {
        owner.ContentItem.Data.Append(item);
        Changed?.Invoke();
    }

    public void Clear() => owner.ContentItem.Data.Clear();

    public IEnumerator<object?> GetEnumerator() => owner.ContentItem.Data.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A Flickable's <c>visibleArea</c>: which part of the content shows, as fractions.</summary>
public sealed class VisibleAreaGroup(Flickable owner) : QObject
{
    internal void Changed()
    {
        NotifyChanged("xPosition");
        NotifyChanged("yPosition");
        NotifyChanged("widthRatio");
        NotifyChanged("heightRatio");
    }

    private (double W, double H) Content
    {
        get
        {
            using var _ = DependencyTracker.Suspend();
            return (Math.Max(1, owner.ContentWidth), Math.Max(1, owner.ContentHeight));
        }
    }

    [Qml] public double XPosition { get { Track(); using var _ = DependencyTracker.Suspend(); return owner.ContentX / Content.W; } }

    [Qml] public double YPosition { get { Track(); using var _ = DependencyTracker.Suspend(); return owner.ContentY / Content.H; } }

    [Qml] public double WidthRatio { get { Track(); using var _ = DependencyTracker.Suspend(); return Math.Min(1, owner.Width / Content.W); } }

    [Qml] public double HeightRatio { get { Track(); using var _ = DependencyTracker.Suspend(); return Math.Min(1, owner.Height / Content.H); } }
}

/// <summary>QtQuick's <c>ListView</c>. Delegates are made for every row and laid out in a line.</summary>
[QmlSignal("add")]
[QmlSignal("remove")]
public sealed class ListView : Flickable, IPolishable
{
    private object? _model;
    private QmlComponent? _delegate;
    private int _orientation = 2;
    private double _spacing;
    private int _currentIndex = -1;
    private QmlComponent? _header, _footer, _highlight;
    private Item? _headerItem, _footerItem, _highlightItem;
    private bool _keyNavigationEnabled = true;
    private bool _keyNavigationWraps;
    private int _verticalLayoutDirection;
    private bool _highlightFollowsCurrentItem = true;
    private readonly DelegateSet _set;
    private IObservableModel? _observed;
    private readonly ItemWatcher _watcher;

    public ListView()
    {
        _set = new DelegateSet(Create,(o, _) => o.Destroy());
        _watcher = new ItemWatcher(this);
    }

    [QmlEnum]
    public enum OrientationValue
    {
        Horizontal = 1,
        Vertical = 2,
    }

    [QmlEnum]
    public enum LayoutDirectionValue
    {
        TopToBottom = 0,
        BottomToTop = 1,
    }

    [QmlEnum]
    public enum PositionMode
    {
        Beginning = 0,
        Center = 1,
        End = 2,
        Visible = 3,
        Contain = 4,
        SnapPosition = 5,
    }

    [Qml]
    public object? Model
    {
        get { Track(); return _model; }
        set
        {
            _model = value is Jint.Native.JsValue js ? ValueConverter.KeepJs(js) : value;
            if (_observed is not null)
            {
                _observed.ModelChanged -= Regenerate;
                _observed.RowChanged -= _set.Refresh;
            }
            _observed = _model as IObservableModel;
            if (_observed is not null)
            {
                _observed.ModelChanged += Regenerate;
                _observed.RowChanged += _set.Refresh;
            }
            NotifyChanged("model");
            Regenerate();
        }
    }

    [Qml]
    public QmlComponent? Delegate
    {
        get { Track(); return _delegate; }
        set
        {
            if (_delegate == value)
                return;
            _delegate = value;
            _set.Clear();
            NotifyChanged("delegate");
            Regenerate();
        }
    }

    [Qml] public int Orientation { get { Track(); return _orientation; } set { if (SetField(ref _orientation, value)) Relayout(); } }

    [Qml] public double Spacing { get { Track(); return _spacing; } set { if (SetField(ref _spacing, value)) Relayout(); } }

    [Qml] public int VerticalLayoutDirection { get { Track(); return _verticalLayoutDirection; } set { if (SetField(ref _verticalLayoutDirection, value)) Relayout(); } }

    [Qml]
    public int CurrentIndex
    {
        get { Track(); return _currentIndex; }
        set
        {
            if (!SetField(ref _currentIndex, value))
                return;
            NotifyChanged("currentItem");
            UpdateHighlight();
        }
    }

    [Qml]
    public Item? CurrentItem
    {
        get { Track(); return _set.At(_currentIndex) as Item; }
    }

    [Qml] public int Count { get { Track(); return _set.Count; } }

    [Qml] public QmlComponent? Header { get => _header; set { _header = value; Relayout(); } }

    [Qml] public QmlComponent? Footer { get => _footer; set { _footer = value; Relayout(); } }

    [Qml] public QmlComponent? Highlight { get => _highlight; set { _highlight = value; UpdateHighlight(); } }

    [Qml] public Item? HeaderItem => _headerItem;

    [Qml] public Item? FooterItem => _footerItem;

    [Qml] public Item? HighlightItem => _highlightItem;

    [Qml] public bool HighlightFollowsCurrentItem { get => _highlightFollowsCurrentItem; set => _highlightFollowsCurrentItem = value; }

    [Qml] public int HighlightMoveDuration { get; set; } = -1;

    [Qml] public double HighlightMoveVelocity { get; set; } = 400;

    [Qml] public int HighlightRangeMode { get; set; }

    [Qml] public double PreferredHighlightBegin { get; set; }

    [Qml] public double PreferredHighlightEnd { get; set; }

    [Qml] public bool KeyNavigationEnabled { get => _keyNavigationEnabled; set => _keyNavigationEnabled = value; }

    [Qml] public bool KeyNavigationWraps { get => _keyNavigationWraps; set => _keyNavigationWraps = value; }

    [Qml] public double CacheBuffer { get; set; }

    [Qml] public int SnapMode { get; set; }

    [Qml] public bool ReuseItems { get; set; }

    [Qml] public QObject? Section { get; set; }

    [Qml] public QObject? Populate { get; set; }

    [Qml] public QObject? Add { get; set; }

    [Qml] public QObject? Remove { get; set; }

    [Qml] public QObject? Displaced { get; set; }

    [Qml] public QObject? Move { get; set; }

    [Qml] public QObject? AddDisplaced { get; set; }

    [Qml] public QObject? RemoveDisplaced { get; set; }

    [Qml] public QObject? MoveDisplaced { get; set; }

    [Qml]
    public Item? ItemAtIndex(int index) => _set.At(index) as Item;

    [Qml]
    public Item? ItemAt(double x, double y)
    {
        foreach (var o in _set.Instances)
        {
            if (o is Item i && x >= i.X && x < i.X + i.Width && y >= i.Y && y < i.Y + i.Height)
                return i;
        }
        return null;
    }

    [Qml]
    public int IndexAt(double x, double y) => ItemAt(x, y) is { } item ? _set.Instances.ToList().IndexOf(item) : -1;

    [Qml]
    public void IncrementCurrentIndex()
    {
        if (_set.Count == 0)
            return;
        CurrentIndex = _currentIndex + 1 < _set.Count ? _currentIndex + 1 : _keyNavigationWraps ? 0 : _currentIndex;
        PositionViewAtIndex(_currentIndex, 4);
    }

    [Qml]
    public void DecrementCurrentIndex()
    {
        if (_set.Count == 0)
            return;
        CurrentIndex = _currentIndex > 0 ? _currentIndex - 1 : _keyNavigationWraps ? _set.Count - 1 : _currentIndex;
        PositionViewAtIndex(_currentIndex, 4);
    }

    [Qml]
    public void PositionViewAtIndex(int index, int mode)
    {
        UpdatePolish();
        if (_set.At(index) is not Item item)
            return;
        var vertical = _orientation == 2;
        var pos = vertical ? item.Y : item.X;
        var size = vertical ? item.Height : item.Width;
        var view = vertical ? Height : Width;
        var current = vertical ? ContentY : ContentX;
        double target = mode switch
        {
            0 => pos,
            1 => pos - (view - size) / 2,
            2 => pos + size - view,
            _ => pos < current ? pos : pos + size > current + view ? pos + size - view : current,
        };
        var max = Math.Max(0, (vertical ? ContentHeight : ContentWidth) - view);
        target = Math.Clamp(target, 0, max);
        if (vertical)
            SetContentY(target);
        else
            SetContentX(target);
    }

    [Qml]
    public void ForceLayout() => UpdatePolish();

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        Regenerate();
    }

    private void Regenerate()
    {
        if (!IsComplete || IsDestroyed)
            return;
        var rows = _delegate is null ? [] : ModelReader.Read(_model);
        var before = _set.Count;
        _set.Sync(rows);
        if (before != _set.Count)
            NotifyChanged("count");
        if (_currentIndex < 0 && _set.Count > 0)
            CurrentIndex = 0;
        else if (_currentIndex >= _set.Count)
            CurrentIndex = _set.Count - 1;
        NotifyChanged("currentItem");
        Relayout();
    }

    private QObject? Create(ModelEntry entry, int index)
    {
        if (_delegate is null)
            return null;
        var context = ModelReader.ContextFor(entry, index);
        context["ListView"] = null;
        var obj = _delegate.Create(null, null, context, o =>
        {
            foreach (var (k, v) in context)
            {
                if (o.GetDynamicProperty(k) is { IsRequired: true })
                    o.WriteProperty(k, v);
            }
            o.SetOwner(this);
            if (o is Item item)
            {
                item.SetParentItem(ContentItem);
                item.AddObserver("width", _watcher);
                item.AddObserver("height", _watcher);
                item.AddObserver("visible", _watcher);
            }
        });
        return obj;
    }

    private void Relayout() => PolishQueue.Request(this, Engine?.Loop);

    public void UpdatePolish()
    {
        if (IsDestroyed)
            return;
        using var _ = DependencyTracker.Suspend();
        _headerItem ??= _header?.Create(ContentItem, null, null) as Item;
        _footerItem ??= _footer?.Create(ContentItem, null, null) as Item;
        var vertical = _orientation == 2;
        var pos = 0.0;
        if (_headerItem is not null)
        {
            if (vertical)
                _headerItem.SetLayoutY(pos);
            else
                _headerItem.SetLayoutX(pos);
            pos += vertical ? _headerItem.Height : _headerItem.Width;
        }
        var maxCross = 0.0;
        foreach (var o in _set.Instances)
        {
            if (o is not Item item || !item.IsVisibleFlag)
                continue;
            if (vertical)
            {
                item.SetLayoutY(pos);
                pos += item.Height + _spacing;
                maxCross = Math.Max(maxCross, item.Width);
            }
            else
            {
                item.SetLayoutX(pos);
                pos += item.Width + _spacing;
                maxCross = Math.Max(maxCross, item.Height);
            }
        }
        if (_set.Count > 0)
            pos -= _spacing;
        if (_footerItem is not null)
        {
            if (vertical)
                _footerItem.SetLayoutY(pos);
            else
                _footerItem.SetLayoutX(pos);
            pos += vertical ? _footerItem.Height : _footerItem.Width;
        }
        if (vertical)
        {
            if (_verticalLayoutDirection == 1 && pos < Height)
            {
                // Bottom to top: rows start at the bottom of the view.
                var shift = Height - pos;
                foreach (var o in _set.Instances)
                {
                    if (o is Item item)
                        item.SetLayoutY(item.Y + shift);
                }
            }
            ContentHeight = pos;
            ImplicitHeight = pos;
            ImplicitWidth = maxCross;
        }
        else
        {
            ContentWidth = pos;
            ImplicitWidth = pos;
            ImplicitHeight = maxCross;
        }
        UpdateHighlight();
    }

    void IPolishable.UpdatePolish() => UpdatePolish();

    private void UpdateHighlight()
    {
        if (_highlight is null)
            return;
        _highlightItem ??= _highlight.Create(ContentItem, null, null) as Item;
        if (_highlightItem is null)
            return;
        _highlightItem.Z = -1;
        if (CurrentItem is { } current && _highlightFollowsCurrentItem)
        {
            _highlightItem.Visible = true;
            _highlightItem.X = current.X;
            _highlightItem.Y = current.Y;
            if (!_highlightItem.HasExplicitWidth)
                _highlightItem.SetLayoutWidth(current.Width);
            if (!_highlightItem.HasExplicitHeight)
                _highlightItem.SetLayoutHeight(current.Height);
        }
    }

    protected override void OnDestroy()
    {
        if (_observed is not null)
        {
            _observed.ModelChanged -= Regenerate;
            _observed.RowChanged -= _set.Refresh;
        }
        _set.Clear();
        base.OnDestroy();
    }

    private sealed class ItemWatcher(ListView view) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => view.Relayout();
    }
}
