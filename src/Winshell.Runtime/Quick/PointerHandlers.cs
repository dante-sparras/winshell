namespace Winshell.Runtime.Quick;

/// <summary>The point a pointer handler reports, as <c>handler.point</c>.</summary>
public sealed class EventPoint : QObject
{
    private QPoint _position;
    private QPoint _scenePosition;
    private QPoint _pressPosition;
    private QPoint _scenePressPosition;

    [Qml] public QPoint Position { get { Track(); return _position; } }

    [Qml] public QPoint ScenePosition { get { Track(); return _scenePosition; } }

    [Qml] public QPoint PressPosition { get { Track(); return _pressPosition; } }

    [Qml] public QPoint ScenePressPosition { get { Track(); return _scenePressPosition; } }

    [Qml] public int Pressure => 1;

    [Qml] public QPoint Velocity => new(0, 0);

    internal void Set(double x, double y, Item item)
    {
        _position = new QPoint(x, y);
        _scenePosition = item.MapToScene(x, y);
        NotifyChanged("position");
        NotifyChanged("scenePosition");
    }

    /// <summary>Records the press at <paramref name="x"/>, <paramref name="y"/>, the point just passed to <see cref="Set"/>.</summary>
    internal void Press(double x, double y)
    {
        _pressPosition = new QPoint(x, y);
        _scenePressPosition = _scenePosition;
        NotifyChanged("pressPosition");
        NotifyChanged("scenePressPosition");
    }
}

/// <summary>The base of HoverHandler, TapHandler and WheelHandler: works on the item it is declared in.</summary>
[QmlSignal("grabChanged", "transition", "point")]
[QmlSignal("canceled", "point")]
public abstract class PointerHandler : QObject
{
    private bool _enabled = true;
    private Item? _target;
    private int _acceptedButtons = QtEnums.LeftButton;
    private int _acceptedModifiers = 0x7fffffff;
    private int _cursorShape = -1;
    private double _margin;

    protected PointerHandler()
    {
        Point = new EventPoint();
    }

    [Qml]
    public bool Enabled { get { Track(); return _enabled; } set => SetField(ref _enabled, value); }

    [Qml]
    public Item? Target { get { Track(); return _target ?? ParentItem; } set => SetField(ref _target, value); }

    [Qml("parent")]
    public Item? ParentItem => Owner as Item;

    [Qml]
    public int AcceptedButtons { get { Track(); return _acceptedButtons; } set => SetField(ref _acceptedButtons, value); }

    [Qml]
    public int AcceptedModifiers { get { Track(); return _acceptedModifiers; } set => SetField(ref _acceptedModifiers, value); }

    [Qml]
    public int AcceptedDevices { get; set; } = 0xff;

    [Qml]
    public int AcceptedPointerTypes { get; set; } = 0xff;

    [Qml]
    public int CursorShape { get { Track(); return _cursorShape; } set => SetField(ref _cursorShape, value); }

    [Qml]
    public double Margin { get { Track(); return _margin; } set => SetField(ref _margin, value); }

    [Qml]
    public EventPoint Point { get; }

    [Qml]
    public bool Active { get; protected set; }

    [Qml]
    public int GrabPermissions { get; set; } = 0x0f;

    protected override void OnOwnerChanged()
    {
        if (Owner is Item item && !item.PointerHandlers.Contains(this))
            item.PointerHandlers.Add(this);
    }

    protected bool Contains(Item item, double x, double y) =>
        x >= -_margin && y >= -_margin && x < item.Width + _margin && y < item.Height + _margin;
}

/// <summary>QtQuick's <c>HoverHandler</c>.</summary>
public sealed class HoverHandler : PointerHandler, IHoverTarget
{
    private bool _hovered;
    private bool _blocking;

    [Qml]
    public bool Hovered { get { Track(); return _hovered; } }

    [Qml]
    public bool Blocking { get { Track(); return _blocking; } set => SetField(ref _blocking, value); }

    public bool AcceptsHover => Enabled;

    int IHoverTarget.CursorShape => CursorShape;

    internal bool HitTest(Item item, double x, double y) => Contains(item, x, y);

    public void HoverEnter(double x, double y, int modifiers)
    {
        if (ParentItem is { } item)
            Point.Set(x, y, item);
        if (SetField(ref _hovered, true, nameof(Hovered)))
        {
            Active = true;
            NotifyChanged("active");
        }
    }

    public void HoverMove(double x, double y, int modifiers)
    {
        if (ParentItem is { } item)
            Point.Set(x, y, item);
    }

    public void HoverLeave()
    {
        if (SetField(ref _hovered, false, nameof(Hovered)))
        {
            Active = false;
            NotifyChanged("active");
        }
    }
}

/// <summary>QtQuick's <c>TapHandler</c>.</summary>
[QmlSignal("tapped", "eventPoint", "button")]
[QmlSignal("singleTapped", "eventPoint", "button")]
[QmlSignal("doubleTapped", "eventPoint", "button")]
[QmlSignal("longPressed")]
[QmlSignal("pressedChanged")]
public sealed class TapHandler : PointerHandler, IPointerTarget
{
    private bool _pressed;
    private int _tapCount;
    private TimeSpan _lastTap;
    private int _gesturePolicy;
    private double _longPressThreshold = 0.8;
    private IDisposable? _longPress;
    private bool _longPressed;

    [QmlEnum]
    public enum GesturePolicyValue
    {
        DragThreshold = 0,
        WithinBounds = 1,
        ReleaseWithinBounds = 2,
        DragWithinBounds = 3,
    }

    [Qml("pressed")]
    public bool IsPressed { get { Track("pressed"); return _pressed; } }

    [Qml]
    public int TapCount { get { Track(); return _tapCount; } }

    [Qml]
    public int GesturePolicy { get { Track(); return _gesturePolicy; } set => SetField(ref _gesturePolicy, value); }

    [Qml]
    public double LongPressThreshold { get { Track(); return _longPressThreshold; } set => SetField(ref _longPressThreshold, value); }

    [Qml]
    public double TimeHeld => 0;

    [Qml]
    public int ExclusiveSignals { get; set; }

    public bool AcceptsButton(int button) => Enabled && (AcceptedButtons & button) != 0;

    private void SetPressed(bool value)
    {
        if (_pressed == value)
            return;
        _pressed = value;
        Active = value;
        NotifyChanged("pressed");
        NotifyChanged("active");
        Emit("pressedChanged");
    }

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        if (ParentItem is not { } item)
            return false;
        Point.Set(x, y, item);
        Point.Press(x, y);
        SetPressed(true);
        _longPressed = false;
        _pressButton = button;
        _longPress?.Dispose();
        if (Engine is not null && _longPressThreshold > 0)
        {
            _longPress = Engine.Loop.Schedule(TimeSpan.FromSeconds(_longPressThreshold), () =>
            {
                if (!_pressed || IsDestroyed)
                    return;
                _longPressed = true;
                Emit("longPressed");
            });
        }
        return true;
    }

    private int _pressButton;

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (ParentItem is { } item)
            Point.Set(x, y, item);
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        _longPress?.Dispose();
        var item = ParentItem;
        SetPressed(false);
        if (item is null || _longPressed || !Contains(item, x, y))
            return;
        var now = Engine?.Loop.Now ?? TimeSpan.Zero;
        var interval = TimeSpan.FromMilliseconds(500);
        _tapCount = now - _lastTap < interval ? _tapCount + 1 : 1;
        _lastTap = now;
        NotifyChanged("tapCount");
        Emit("tapped", Point, _pressButton);
        if (_tapCount == 2)
            Emit("doubleTapped", Point, _pressButton);
        else
            Emit("singleTapped", Point, _pressButton);
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel()
    {
        _longPress?.Dispose();
        SetPressed(false);
        Emit("canceled", Point);
    }
}

/// <summary>QtQuick's <c>WheelHandler</c>.</summary>
[QmlSignal("wheel", "event")]
public sealed class WheelHandler : PointerHandler, IWheelTarget
{
    private int _orientation = 2;
    private string _property = "";
    private double _rotation;
    private double _rotationScale = 1;
    private bool _invertible = true;

    [Qml] public int Orientation { get { Track(); return _orientation; } set => SetField(ref _orientation, value); }

    [Qml] public string Property { get { Track(); return _property; } set => SetField(ref _property, value ?? ""); }

    [Qml] public double Rotation { get { Track(); return _rotation; } set => SetField(ref _rotation, value); }

    [Qml] public double RotationScale { get { Track(); return _rotationScale; } set => SetField(ref _rotationScale, value); }

    [Qml] public bool Invertible { get { Track(); return _invertible; } set => SetField(ref _invertible, value); }

    [Qml] public bool TargetScaleMultiplier { get; set; }

    [Qml] public bool TargetTransformAroundCursor { get; set; }

    [Qml] public bool ActiveTimeout { get; set; }

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted)
    {
        if (!Enabled)
            return false;
        // A wheel on the other axis is left for whatever is underneath.
        var horizontal = _orientation == 1;
        var angle = horizontal ? angleX : angleY;
        var pixel = horizontal ? pixelX : pixelY;
        if (angle == 0 && pixel == 0)
            return false;
        var step = angle / 8 * _rotationScale;
        _rotation += step;
        NotifyChanged("rotation");
        if (_property.Length > 0 && Target is { } target && target.HasProperty(_property))
        {
            using (DependencyTracker.Suspend())
                target.WriteProperty(_property, ValueConverter.ToDouble(target.ReadProperty(_property), "real") + (pixel != 0 ? pixel : step));
        }
        Emit("wheel", new WheelEvent { X = x, Y = y, AngleDelta = new QPoint(angleX, angleY), PixelDelta = new QPoint(pixelX, pixelY), Modifiers = modifiers, Inverted = inverted, Engine = Engine });
        return true;
    }
}

/// <summary>QtQuick's <c>DragHandler</c>: moves its target with the pointer.</summary>
public sealed class DragHandler : PointerHandler, IPointerTarget
{
    private double _startX, _startY, _targetX, _targetY;
    private bool _dragging;

    [Qml] public int XAxis { get; set; }

    [Qml] public int YAxis { get; set; }

    [Qml] public double DragThreshold { get; set; } = 4;

    [Qml] public QPoint Translation => new(0, 0);

    public bool AcceptsButton(int button) => Enabled && (AcceptedButtons & button) != 0;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        if (Target is not { } t || ParentItem is not { } item)
            return false;
        var scene = item.MapToScene(x, y);
        (_startX, _startY) = (scene.X, scene.Y);
        (_targetX, _targetY) = (t.X, t.Y);
        _dragging = false;
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (Target is not { } t || ParentItem is not { } item)
            return;
        var scene = item.MapToScene(x, y);
        var dx = scene.X - _startX;
        var dy = scene.Y - _startY;
        if (!_dragging && Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
            return;
        if (!_dragging)
        {
            _dragging = true;
            Active = true;
            NotifyChanged("active");
        }
        t.X = _targetX + dx;
        t.Y = _targetY + dy;
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        _dragging = false;
        Active = false;
        NotifyChanged("active");
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers)
    {
    }

    public void PointerCancel() => PointerRelease(0, 0, 0, 0, 0);
}
