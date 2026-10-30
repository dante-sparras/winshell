namespace Winshell.Runtime.Quick;

/// <summary>The <c>mouse</c> argument of MouseArea's signals.</summary>
public sealed class MouseEvent : QObject
{
    /// <summary>How many events this thread has made, so a test can see a move that makes none.</summary>
    [ThreadStatic]
    internal static int Created;

    public MouseEvent() => Created++;

    [Qml] public double X { get; init; }

    [Qml] public double Y { get; init; }

    [Qml] public int Button { get; init; }

    [Qml] public int Buttons { get; init; }

    [Qml] public int Modifiers { get; init; }

    [Qml] public bool Accepted { get; set; } = true;

    [Qml] public bool WasHeld { get; init; }

    [Qml] public bool IsClick { get; init; }

    [Qml] public int Flags { get; init; }

    [Qml] public int Source { get; init; }
}

/// <summary>The <c>wheel</c> argument of MouseArea's wheel signal.</summary>
public sealed class WheelEvent : QObject
{
    [Qml] public double X { get; init; }

    [Qml] public double Y { get; init; }

    [Qml] public QPoint AngleDelta { get; init; }

    [Qml] public QPoint PixelDelta { get; init; }

    [Qml] public int Buttons { get; init; }

    [Qml] public int Modifiers { get; init; }

    [Qml] public bool Inverted { get; init; }

    [Qml] public bool Accepted { get; set; } = true;
}

/// <summary>The <c>event</c> argument of key handlers.</summary>
public sealed class KeyEvent : QObject
{
    [Qml] public int Key { get; init; }

    [Qml] public string Text { get; init; } = "";

    [Qml] public int Modifiers { get; init; }

    [Qml] public bool IsAutoRepeat { get; init; }

    [Qml] public int Count { get; init; } = 1;

    [Qml] public int NativeScanCode { get; init; }

    [Qml] public bool Accepted { get; set; }

    [Qml]
    public bool Matches(int standardKey) => false;
}

/// <summary>An item that takes pointer presses: MouseArea, Flickable, a control.</summary>
public interface IPointerTarget
{
    bool AcceptsButton(int button);

    /// <summary>Handles a press in item coordinates. Returns whether the item takes the press and what follows it.</summary>
    bool PointerPress(double x, double y, int button, int buttons, int modifiers);

    void PointerMove(double x, double y, int buttons, int modifiers);

    void PointerRelease(double x, double y, int button, int buttons, int modifiers);

    /// <summary>Handles a double-click, which stands in for the second press. Returns whether the item takes it, as <see cref="PointerPress"/> does.</summary>
    bool PointerDoubleClick(double x, double y, int button, int buttons, int modifiers);

    /// <summary>Another item took the press, or the window lost it.</summary>
    void PointerCancel();

    /// <summary>Whether the item wants to take over a press that started elsewhere, as Flickable does when dragged.</summary>
    bool WantsToSteal(double sceneDx, double sceneDy) => false;
}

/// <summary>An item that reacts to the pointer over it without a press.</summary>
public interface IHoverTarget
{
    bool AcceptsHover { get; }

    void HoverEnter(double x, double y, int modifiers);

    void HoverMove(double x, double y, int modifiers);

    void HoverLeave();

    /// <summary>The cursor shape to show over the item, or -1 for no preference.</summary>
    int CursorShape { get; }
}

/// <summary>An item that takes wheel turns.</summary>
public interface IWheelTarget
{
    /// <summary>Handles a wheel turn in item coordinates. Returns whether it used it.</summary>
    bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted);
}

/// <summary>An item that takes keys and typed text while it has focus.</summary>
public interface IKeyTarget
{
    bool KeyPress(KeyEvent e);

    bool KeyRelease(KeyEvent e);
}

/// <summary>QtQuick's <c>MouseArea</c>.</summary>
[QmlSignal("clicked", "mouse")]
[QmlSignal("doubleClicked", "mouse")]
[QmlSignal("pressed", "mouse")]
[QmlSignal("released", "mouse")]
[QmlSignal("positionChanged", "mouse")]
[QmlSignal("pressAndHold", "mouse")]
[QmlSignal("wheel", "wheel")]
[QmlSignal("entered")]
[QmlSignal("exited")]
[QmlSignal("canceled")]
public class MouseArea : Item, IPointerTarget, IHoverTarget, IWheelTarget
{
    private bool _hoverEnabled;
    private bool _containsMouse;
    private bool _pressed;
    private int _pressedButtons;
    private int _acceptedButtons = QtEnums.LeftButton;
    private int _cursorShape = -1;
    private double _mouseX, _mouseY;
    private bool _preventStealing;
    private bool _propagateComposedEvents;
    private bool _scrollGestureEnabled = true;
    private int _pressAndHoldInterval = 800;
    private IDisposable? _holdTimer;
    private bool _held;
    private bool _doubleClicked;
    private double _pressX, _pressY;
    private bool _containsPress;

    public MouseArea()
    {
        Drag = new DragGroup(this);
    }

    [Qml]
    public bool HoverEnabled
    {
        get { Track(); return _hoverEnabled; }
        set
        {
            if (SetField(ref _hoverEnabled, value) && !value && _containsMouse && !_pressed)
                SetContainsMouse(false);
        }
    }

    [Qml]
    public bool ContainsMouse
    {
        get { Track(); return _containsMouse; }
    }

    [Qml]
    public bool ContainsPress
    {
        get { Track(); return _containsPress; }
    }

    [Qml("pressed")]
    public bool IsPressed
    {
        get { Track("pressed"); return _pressed; }
    }

    [Qml]
    public int PressedButtons
    {
        get { Track(); return _pressedButtons; }
    }

    [Qml]
    public int AcceptedButtons
    {
        get { Track(); return _acceptedButtons; }
        set => SetField(ref _acceptedButtons, value);
    }

    [Qml]
    public int CursorShape
    {
        get { Track(); return _cursorShape; }
        set => SetField(ref _cursorShape, value);
    }

    [Qml]
    public double MouseX
    {
        get { Track(); return _mouseX; }
    }

    [Qml]
    public double MouseY
    {
        get { Track(); return _mouseY; }
    }

    [Qml]
    public bool PreventStealing
    {
        get { Track(); return _preventStealing; }
        set => SetField(ref _preventStealing, value);
    }

    [Qml]
    public bool PropagateComposedEvents
    {
        get { Track(); return _propagateComposedEvents; }
        set => SetField(ref _propagateComposedEvents, value);
    }

    [Qml]
    public bool ScrollGestureEnabled
    {
        get { Track(); return _scrollGestureEnabled; }
        set => SetField(ref _scrollGestureEnabled, value);
    }

    [Qml]
    public int PressAndHoldInterval
    {
        get { Track(); return _pressAndHoldInterval; }
        set => SetField(ref _pressAndHoldInterval, value);
    }

    [Qml]
    public DragGroup Drag { get; }

    public bool AcceptsHover => _hoverEnabled && Enabled;

    int IHoverTarget.CursorShape => _cursorShape;

    public bool AcceptsButton(int button) => Enabled && (_acceptedButtons & button) != 0;

    private void SetPosition(double x, double y)
    {
        if (_mouseX != x)
        {
            _mouseX = x;
            NotifyChanged("mouseX");
        }
        if (_mouseY != y)
        {
            _mouseY = y;
            NotifyChanged("mouseY");
        }
    }

    private void SetContainsMouse(bool value)
    {
        if (_containsMouse == value)
            return;
        _containsMouse = value;
        NotifyChanged("containsMouse");
        Emit(value ? "entered" : "exited");
    }

    private void SetPressed(bool value, int buttons)
    {
        var changed = _pressed != value;
        _pressed = value;
        if (_pressedButtons != buttons)
        {
            _pressedButtons = buttons;
            NotifyChanged("pressedButtons");
        }
        if (changed)
            NotifyChanged("pressed");
        var contains = value && HitTest(_mouseX, _mouseY);
        if (_containsPress != contains)
        {
            _containsPress = contains;
            NotifyChanged("containsPress");
        }
    }

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        _doubleClicked = false;
        SetPosition(x, y);
        _pressX = x;
        _pressY = y;
        _held = false;
        var e = new MouseEvent { X = x, Y = y, Button = button, Buttons = buttons, Modifiers = modifiers, Engine = Engine };
        SetPressed(true, buttons);
        if (!_containsMouse)
            SetContainsMouse(true);
        Emit("pressed", e);
        if (!e.Accepted)
        {
            SetPressed(false, 0);
            if (!_hoverEnabled)
                SetContainsMouse(false);
            return false;
        }
        Drag.Begin(x, y);
        _holdTimer?.Dispose();
        if (Engine is not null && GetSignal("pressAndHold")?.Count > 0)
        {
            _holdTimer = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(_pressAndHoldInterval), () =>
            {
                if (!_pressed || IsDestroyed)
                    return;
                _held = true;
                Emit("pressAndHold", new MouseEvent { X = _mouseX, Y = _mouseY, Button = button, Buttons = _pressedButtons, Modifiers = modifiers, WasHeld = true, Engine = Engine });
            });
        }
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        SetPosition(x, y);
        if (_pressed)
        {
            var contains = HitTest(x, y);
            if (_containsPress != contains)
            {
                _containsPress = contains;
                NotifyChanged("containsPress");
            }
            if (!_hoverEnabled && _containsMouse != contains)
                SetContainsMouse(contains);
            Drag.Move(x, y);
        }
        EmitPositionChanged(x, y, buttons, modifiers);
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        SetPosition(x, y);
        _holdTimer?.Dispose();
        var inside = HitTest(x, y);
        var dragged = Drag.End();
        var suppressClick = _doubleClicked;
        _doubleClicked = false;
        SetPressed(buttons != 0, buttons);
        var e = new MouseEvent { X = x, Y = y, Button = button, Buttons = buttons, Modifiers = modifiers, WasHeld = _held, IsClick = inside && !dragged && !suppressClick, Engine = Engine };
        Emit("released", e);
        if (inside && !_held && !dragged && !suppressClick)
            Emit("clicked", new MouseEvent { X = x, Y = y, Button = button, Buttons = buttons, Modifiers = modifiers, Engine = Engine });
        if (!_hoverEnabled || !inside)
            SetContainsMouse(false);
    }

    public bool PointerDoubleClick(double x, double y, int button, int buttons, int modifiers)
    {
        if (GetSignal("doubleClicked")?.Count > 0)
        {
            if (!PointerPress(x, y, button, buttons, modifiers))
                return false;
            _doubleClicked = true;
            Emit("doubleClicked", new MouseEvent { X = x, Y = y, Button = button, Buttons = buttons, Modifiers = modifiers, Engine = Engine });
            return true;
        }
        return PointerPress(x, y, button, buttons, modifiers);
    }

    public void PointerCancel()
    {
        _doubleClicked = false;
        _holdTimer?.Dispose();
        Drag.End();
        if (_pressed)
        {
            SetPressed(false, 0);
            Emit("canceled");
        }
        if (!_hoverEnabled)
            SetContainsMouse(false);
    }

    public void HoverEnter(double x, double y, int modifiers)
    {
        SetPosition(x, y);
        SetContainsMouse(true);
        EmitPositionChanged(x, y, 0, modifiers);
    }

    public void HoverMove(double x, double y, int modifiers)
    {
        if (_pressed)
            return;
        SetPosition(x, y);
        EmitPositionChanged(x, y, 0, modifiers);
    }

    /// <summary>Emits positionChanged, making its event only when something handles it, since moves are frequent.</summary>
    private void EmitPositionChanged(double x, double y, int buttons, int modifiers)
    {
        if (!IsDestroyed && GetSignal("positionChanged") is { Count: > 0 } slot)
            slot.Emit([new MouseEvent { X = x, Y = y, Buttons = buttons, Modifiers = modifiers, Engine = Engine }]);
    }

    public void HoverLeave()
    {
        if (!_pressed)
            SetContainsMouse(false);
    }

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted)
    {
        if (!Enabled || GetSignal("wheel")?.Count is null or 0)
            return false;
        var e = new WheelEvent
        {
            X = x,
            Y = y,
            AngleDelta = new QPoint(angleX, angleY),
            PixelDelta = new QPoint(pixelX, pixelY),
            Buttons = buttons,
            Modifiers = modifiers,
            Inverted = inverted,
            Engine = Engine,
        };
        Emit("wheel", e);
        return e.Accepted;
    }

    protected override void OnDestroy()
    {
        _holdTimer?.Dispose();
        base.OnDestroy();
    }
}

/// <summary>MouseArea's <c>drag</c> group: moves a target item with the pointer.</summary>
public sealed class DragGroup(MouseArea area) : QObject
{
    private Item? _target;
    private int _axis = 3;
    private double _minimumX = double.MinValue, _maximumX = double.MaxValue, _minimumY = double.MinValue, _maximumY = double.MaxValue;
    private bool _active;
    private double _threshold = Theme.Current.DragThreshold;
    private bool _filterChildren;
    private double _startX, _startY, _targetX, _targetY;
    private bool _moved;

    [QmlEnum]
    public enum AxisValue
    {
        XAxis = 1,
        YAxis = 2,
        XAndYAxis = 3,
    }

    [Qml] public Item? Target { get { Track(); return _target; } set => SetField(ref _target, value); }

    [Qml] public int Axis { get { Track(); return _axis; } set => SetField(ref _axis, value); }

    [Qml] public double MinimumX { get { Track(); return _minimumX; } set => SetField(ref _minimumX, value); }

    [Qml] public double MaximumX { get { Track(); return _maximumX; } set => SetField(ref _maximumX, value); }

    [Qml] public double MinimumY { get { Track(); return _minimumY; } set => SetField(ref _minimumY, value); }

    [Qml] public double MaximumY { get { Track(); return _maximumY; } set => SetField(ref _maximumY, value); }

    [Qml] public bool Active { get { Track(); return _active; } }

    [Qml] public double Threshold { get { Track(); return _threshold; } set => SetField(ref _threshold, value); }

    [Qml] public bool FilterChildren { get { Track(); return _filterChildren; } set => SetField(ref _filterChildren, value); }

    [Qml] public bool Smoothed { get; set; } = true;

    internal void Begin(double x, double y)
    {
        _moved = false;
        if (_target is null)
            return;
        var scene = area.MapToScene(x, y);
        _startX = scene.X;
        _startY = scene.Y;
        _targetX = _target.X;
        _targetY = _target.Y;
    }

    internal void Move(double x, double y)
    {
        if (_target is null)
            return;
        var scene = area.MapToScene(x, y);
        var dx = scene.X - _startX;
        var dy = scene.Y - _startY;
        if (!_active && Math.Sqrt(dx * dx + dy * dy) < _threshold)
            return;
        if (!_active)
        {
            _active = true;
            NotifyChanged("active");
        }
        _moved = true;
        if ((_axis & 1) != 0)
            _target.X = Math.Clamp(_targetX + dx, _minimumX, _maximumX);
        if ((_axis & 2) != 0)
            _target.Y = Math.Clamp(_targetY + dy, _minimumY, _maximumY);
    }

    /// <summary>Ends a drag. Returns whether the press moved the target.</summary>
    internal bool End()
    {
        if (_active)
        {
            _active = false;
            NotifyChanged("active");
        }
        var moved = _moved;
        _moved = false;
        return moved;
    }
}

/// <summary>The attached <c>Keys</c> object: key handlers on an item.</summary>
[QmlSignal("pressed", "event")]
[QmlSignal("released", "event")]
[QmlSignal("returnPressed", "event")]
[QmlSignal("enterPressed", "event")]
[QmlSignal("escapePressed", "event")]
[QmlSignal("tabPressed", "event")]
[QmlSignal("backtabPressed", "event")]
[QmlSignal("spacePressed", "event")]
[QmlSignal("upPressed", "event")]
[QmlSignal("downPressed", "event")]
[QmlSignal("leftPressed", "event")]
[QmlSignal("rightPressed", "event")]
[QmlSignal("deletePressed", "event")]
[QmlSignal("backPressed", "event")]
[QmlSignal("digit0Pressed", "event")]
[QmlSignal("digit1Pressed", "event")]
[QmlSignal("digit2Pressed", "event")]
[QmlSignal("digit3Pressed", "event")]
[QmlSignal("digit4Pressed", "event")]
[QmlSignal("digit5Pressed", "event")]
[QmlSignal("digit6Pressed", "event")]
[QmlSignal("digit7Pressed", "event")]
[QmlSignal("digit8Pressed", "event")]
[QmlSignal("digit9Pressed", "event")]
public sealed class KeysAttached : QObject
{
    private bool _enabled = true;
    private int _priority;

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set => SetField(ref _enabled, value);
    }

    [Qml]
    public int Priority
    {
        get { Track(); return _priority; }
        set => SetField(ref _priority, value);
    }

    [Qml]
    public object? ForwardTo { get; set; }

    /// <summary>Runs the key handlers for a press. Returns whether one accepted it.</summary>
    public bool Press(KeyEvent e)
    {
        if (!_enabled)
            return false;
        var specific = e.Key switch
        {
            0x01000004 => "returnPressed",
            0x01000005 => "enterPressed",
            0x01000000 => "escapePressed",
            0x01000001 => "tabPressed",
            0x01000002 => "backtabPressed",
            0x20 => "spacePressed",
            0x01000013 => "upPressed",
            0x01000015 => "downPressed",
            0x01000012 => "leftPressed",
            0x01000014 => "rightPressed",
            0x01000007 => "deletePressed",
            >= '0' and <= '9' => $"digit{(char)e.Key}Pressed",
            _ => null,
        };
        if (specific is not null && GetSignal(specific) is { Count: > 0 } slot)
        {
            e.Accepted = true;
            slot.Emit([e]);
            if (e.Accepted)
                return true;
        }
        if (GetSignal("pressed") is { Count: > 0 } pressed)
        {
            e.Accepted = true;
            pressed.Emit([e]);
            return e.Accepted;
        }
        return false;
    }

    public bool Release(KeyEvent e)
    {
        if (!_enabled || GetSignal("released") is not { Count: > 0 } released)
            return false;
        e.Accepted = true;
        released.Emit([e]);
        return e.Accepted;
    }
}
