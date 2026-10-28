using Jint;
using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>Where a press on a control's handle is kept by the scene, so a Flickable around it does not take the drag.</summary>
public interface IKeepsPointerGrab
{
    bool KeepsGrab { get; }
}

/// <summary>QtQuick.Controls' <c>Slider</c>, drawn as a Windows 11 slider.</summary>
[QmlSignal("moved")]
public sealed class Slider : Control, IPointerTarget, IKeyTarget, IWheelTarget, IKeepsPointerGrab
{
    protected override int DefaultFocusPolicy => StrongFocus;

    private double _from, _to = 1, _value, _stepSize;
    private int _snapMode;
    private int _orientation = 1;
    private bool _live = true;
    private bool _pressed;
    private double _position;
    private double _pressOffset;
    private Item? _handle;
    private bool _handleSet, _defaultHandle;
    private PropertyWatch? _handleWatch;

    [QmlEnum]
    public enum SnapModeValue
    {
        NoSnap = 0,
        SnapAlways = 1,
        SnapOnRelease = 2,
    }

    public Slider()
    {
        SetDefaultPadding(0, 0, 0, 0);
    }

    [Qml] public double From { get { Track(); return _from; } set { if (SetField(ref _from, value)) ValueRangeChanged(); } }

    [Qml] public double To { get { Track(); return _to; } set { if (SetField(ref _to, value)) ValueRangeChanged(); } }

    [Qml]
    public double Value
    {
        get { Track(); return _value; }
        set => SetValue(value);
    }

    private void SetValue(double value)
    {
        var clamped = Clamp(value);
        if (_value == clamped && !_pressed)
        {
            SetPositionField(PositionOf(clamped));
            return;
        }
        SetField(ref _value, clamped, nameof(Value));
        if (!_pressed || _live)
            SetPositionField(PositionOf(clamped));
    }

    private double Clamp(double v) => _from <= _to ? Math.Clamp(v, _from, _to) : Math.Clamp(v, _to, _from);

    private double PositionOf(double v) => _to == _from ? 0 : Math.Clamp((v - _from) / (_to - _from), 0, 1);

    private void ValueRangeChanged()
    {
        SetValue(_value);
        SetPositionField(PositionOf(_value));
    }

    [Qml] public double StepSize { get { Track(); return _stepSize; } set => SetField(ref _stepSize, value); }

    [Qml] public int SnapMode { get { Track(); return _snapMode; } set => SetField(ref _snapMode, value); }

    [Qml]
    public int Orientation
    {
        get { Track(); return _orientation; }
        set
        {
            if (SetField(ref _orientation, value))
            {
                NotifyChanged("horizontal");
                NotifyChanged("vertical");
                LayoutChildren();
                UpdateImplicitSize();
            }
        }
    }

    [Qml] public bool Horizontal => Orientation == 1;

    [Qml] public bool Vertical => Orientation == 2;

    [Qml] public bool Live { get { Track(); return _live; } set => SetField(ref _live, value); }

    [Qml("pressed")]
    public bool IsPressed { get { Track("pressed"); return _pressed; } set => SetPressed(value); }

    [Qml] public double Position { get { Track(); return _position; } }

    /// <summary>The position as drawn: from the bottom for a vertical slider.</summary>
    [Qml] public double VisualPosition { get { Track("position"); Track("orientation"); return _orientation == 2 ? 1 - _position : _position; } }

    [Qml] public double TouchDragThreshold { get; set; } = -1;

    [Qml] public double ImplicitHandleWidth { get { Track(); return _handle?.ImplicitWidth ?? 0; } }

    [Qml] public double ImplicitHandleHeight { get { Track(); return _handle?.ImplicitHeight ?? 0; } }

    [Qml]
    public Item? Handle
    {
        get { Track(); return _handle; }
        set
        {
            _handleSet = true;
            SetHandle(value, false);
        }
    }

    private void SetHandle(Item? value, bool isDefault)
    {
        if (_handle == value)
            return;
        var old = _handle;
        _handleWatch?.Dispose();
        _handle = value;
        RetirePart(old, _defaultHandle);
        _defaultHandle = isDefault && value is not null;
        if (value is not null)
        {
            value.SetParentItem(this);
            if (value.Z == 0)
                value.Z = 1;
            _handleWatch = new PropertyWatch(value, () =>
            {
                NotifyChanged("implicitHandleWidth");
                NotifyChanged("implicitHandleHeight");
                LayoutChildren();
                UpdateImplicitSize();
            }, "implicitWidth", "implicitHeight");
        }
        NotifyChanged("handle");
        LayoutChildren();
        UpdateImplicitSize();
    }

    bool IKeepsPointerGrab.KeepsGrab => _pressed;

    private void SetPositionField(double position)
    {
        if (SetField(ref _position, position, nameof(Position)))
        {
            NotifyChanged("visualPosition");
            LayoutChildren();
            Update();
        }
    }

    private void SetPressed(bool value)
    {
        if (SetField(ref _pressed, value, "pressed"))
            StateChanged();
    }

    [Qml]
    public double ValueAt(double position)
    {
        var v = _from + (_to - _from) * Math.Clamp(position, 0, 1);
        return v;
    }

    private double Snap(double value)
    {
        if (_stepSize <= 0)
            return value;
        var steps = Math.Round((value - _from) / _stepSize);
        return Clamp(_from + steps * _stepSize);
    }

    [Qml]
    public void Increase() => SetValue(Snap(_value + (_stepSize > 0 ? _stepSize : (_to - _from) / 10)));

    [Qml]
    public void Decrease() => SetValue(Snap(_value - (_stepSize > 0 ? _stepSize : (_to - _from) / 10)));

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var handle = _handle;
        var hw = (float)(handle?.Width ?? 20);
        var hh = (float)(handle?.Height ?? 20);
        var vertical = _orientation == 2;
        var ox = (float)(LeftPadding - i.X);
        var oy = (float)(TopPadding - i.Y);
        var aw = (float)AvailableWidth;
        var ah = (float)AvailableHeight;
        var trackColor = Enabled ? t.ControlStrongFillColorDefault : t.ControlStrongFillColorDisabled;
        var valueColor = Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled;
        if (!vertical)
        {
            var track = SKRect.Create(ox, oy + ah / 2 - 2, aw, 4);
            Fluent.FillRound(c, track, 2, trackColor);
            var end = ox + hw / 2 + (float)VisualPosition * (aw - hw);
            Fluent.FillRound(c, SKRect.Create(ox, track.Top, end - ox, 4), 2, valueColor);
        }
        else
        {
            var track = SKRect.Create(ox + aw / 2 - 2, oy, 4, ah);
            Fluent.FillRound(c, track, 2, trackColor);
            var start = oy + hh / 2 + (float)VisualPosition * (ah - hh);
            Fluent.FillRound(c, SKRect.Create(track.Left, start, 4, oy + ah - start), 2, valueColor);
        }
    }, 200, Fluent.ControlHeight));

    protected override void CreateDefaults()
    {
        if (!_handleSet && _handle is null)
            SetHandle(Part(new PaintedItem((c, i, _) => PaintThumb(c, i.Bounds), 20, 20)), true);
        base.CreateDefaults();
    }

    private void PaintThumb(SKCanvas c, SKRect r)
    {
        var t = Theme.Current;
        float cx = r.MidX, cy = r.MidY;
        Fluent.FillCircle(c, cx, cy, 10, t.ControlSolidFillColorDefault);
        Fluent.StrokeCircle(c, cx, cy, 10, t.IsDark ? QColor.Parse("#23ffffff") : QColor.Parse("#29000000"));
        var inner = !Enabled ? 6f : _pressed ? 5f : Hovered ? 7f : 6f;
        Fluent.FillCircle(c, cx, cy, inner, Enabled ? (_pressed ? t.AccentFillColorTertiary : Hovered ? t.AccentFillColorSecondary : t.AccentFillColorDefault) : t.AccentFillColorDisabled);
    }

    protected override void LayoutParts()
    {
        if (_handle is not { } h || h.ParentItem != this || h.HasAnchors)
            return;
        if (!h.HasExplicitWidth)
            h.SetLayoutWidth(h.ImplicitWidth);
        if (!h.HasExplicitHeight)
            h.SetLayoutHeight(h.ImplicitHeight);
        if (_orientation == 2)
        {
            h.SetLayoutX(LeftPadding + (AvailableWidth - h.Width) / 2);
            h.SetLayoutY(TopPadding + VisualPosition * (AvailableHeight - h.Height));
        }
        else
        {
            h.SetLayoutX(LeftPadding + VisualPosition * (AvailableWidth - h.Width));
            h.SetLayoutY(TopPadding + (AvailableHeight - h.Height) / 2);
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        using var _ = DependencyTracker.Suspend();
        if (_orientation == 2)
        {
            // A vertical slider is the horizontal one turned.
            var bw = Background?.ImplicitHeight ?? Fluent.ControlHeight;
            var bh = Background?.ImplicitWidth ?? 200;
            w = Math.Max(bw, ImplicitHandleWidth) + LeftPadding + RightPadding;
            h = Math.Max(bh, ImplicitHandleHeight) + TopPadding + BottomPadding;
        }
        else
        {
            w = Math.Max(w, ImplicitHandleWidth + LeftPadding + RightPadding);
            h = Math.Max(h, ImplicitHandleHeight + TopPadding + BottomPadding);
        }
        return (w, h);
    }

    private double PositionAt(double x, double y)
    {
        var hw = _handle?.Width ?? 0;
        var hh = _handle?.Height ?? 0;
        if (_orientation == 2)
        {
            var travel = AvailableHeight - hh;
            if (travel <= 0)
                return 0;
            return 1 - Math.Clamp((y - TopPadding - hh / 2 - _pressOffset) / travel, 0, 1);
        }
        var range = AvailableWidth - hw;
        if (range <= 0)
            return 0;
        return Math.Clamp((x - LeftPadding - hw / 2 - _pressOffset) / range, 0, 1);
    }

    private void MoveTo(double x, double y)
    {
        var pos = PositionAt(x, y);
        if (_snapMode == (int)SnapModeValue.SnapAlways && _stepSize > 0)
            pos = PositionOf(Snap(ValueAt(pos)));
        var old = _value;
        if (_live)
        {
            var value = ValueAt(pos);
            if (_snapMode == (int)SnapModeValue.SnapAlways)
                value = Snap(value);
            SetField(ref _value, Clamp(value), nameof(Value));
        }
        SetPositionField(pos);
        if (_value != old)
            Emit("moved");
    }

    public bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        _pressOffset = 0;
        if (_handle is { } h && x >= h.X && x < h.X + h.Width && y >= h.Y && y < h.Y + h.Height)
            _pressOffset = _orientation == 2 ? y - (h.Y + h.Height / 2) : x - (h.X + h.Width / 2);
        SetPressed(true);
        MoveTo(x, y);
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (_pressed)
            MoveTo(x, y);
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        if (!_pressed)
            return;
        MoveTo(x, y);
        var old = _value;
        var value = ValueAt(_position);
        if (_snapMode != (int)SnapModeValue.NoSnap)
            value = Snap(value);
        SetPressed(false);
        SetField(ref _value, Clamp(value), nameof(Value));
        SetPositionField(PositionOf(_value));
        if (_value != old)
            Emit("moved");
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel() => SetPressed(false);

    public bool KeyPress(KeyEvent e)
    {
        var forward = e.Key is 0x01000014 or 0x01000013;
        var back = e.Key is 0x01000012 or 0x01000015;
        if (!forward && !back)
            return false;
        var old = _value;
        if (forward)
            Increase();
        else
            Decrease();
        if (old != _value)
            Emit("moved");
        return true;
    }

    public bool KeyRelease(KeyEvent e) => false;

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted)
    {
        if (!WheelEnabled)
            return false;
        var old = _value;
        if (angleY + angleX > 0)
            Increase();
        else
            Decrease();
        if (old != _value)
            Emit("moved");
        return true;
    }

    protected override void OnDestroy()
    {
        _handleWatch?.Dispose();
        base.OnDestroy();
    }
}

/// <summary>One end of a RangeSlider: <c>first</c> or <c>second</c>.</summary>
[QmlSignal("moved")]
public sealed class RangeSliderNode(RangeSlider owner) : QObject
{
    private double _value;
    private bool _pressed, _hovered;
    private Item? _handle;

    [Qml]
    public double Value
    {
        get { Track(); return _value; }
        set => owner.SetNodeValue(this, value);
    }

    internal void SetValueField(double value)
    {
        if (SetField(ref _value, value, nameof(Value)))
        {
            NotifyChanged("position");
            NotifyChanged("visualPosition");
        }
    }

    [Qml] public double Position { get { Track("value"); return owner.PositionOf(_value); } }

    [Qml] public double VisualPosition { get { Track("value"); return owner.Orientation == 2 ? 1 - Position : Position; } }

    [Qml("pressed")] public bool Pressed { get { Track("pressed"); return _pressed; } set => SetPressed(value); }

    internal void SetPressed(bool v) => SetField(ref _pressed, v, "pressed");

    [Qml] public bool Hovered { get { Track(); return _hovered; } }

    internal void SetHovered(bool v) => SetField(ref _hovered, v, nameof(Hovered));

    [Qml]
    public Item? Handle
    {
        get { Track(); return _handle; }
        set
        {
            owner.ReplaceHandle(_handle, value);
            SetField(ref _handle, value);
        }
    }

    internal void SetHandleField(Item? item) => _handle = item;

    [Qml] public double ImplicitHandleWidth => _handle?.ImplicitWidth ?? 0;

    [Qml] public double ImplicitHandleHeight => _handle?.ImplicitHeight ?? 0;

    [Qml] public void Increase() => owner.StepNode(this, 1);

    [Qml] public void Decrease() => owner.StepNode(this, -1);
}

/// <summary>QtQuick.Controls' <c>RangeSlider</c>: a slider with two handles.</summary>
public sealed class RangeSlider : Control, IPointerTarget, IKeepsPointerGrab
{
    private double _from, _to = 1, _stepSize;
    private int _orientation = 1;
    private int _snapMode;
    private RangeSliderNode? _dragging;
    private double _pressOffset;

    public RangeSlider()
    {
        First = new RangeSliderNode(this);
        Second = new RangeSliderNode(this);
        Second.SetValueField(1);
    }

    [Qml] public RangeSliderNode First { get; }

    [Qml] public RangeSliderNode Second { get; }

    [Qml] public double From { get { Track(); return _from; } set { if (SetField(ref _from, value)) Reclamp(); } }

    [Qml] public double To { get { Track(); return _to; } set { if (SetField(ref _to, value)) Reclamp(); } }

    [Qml] public double StepSize { get { Track(); return _stepSize; } set => SetField(ref _stepSize, value); }

    [Qml] public int SnapMode { get { Track(); return _snapMode; } set => SetField(ref _snapMode, value); }

    [Qml] public int Orientation { get { Track(); return _orientation; } set { if (SetField(ref _orientation, value)) LayoutChildren(); } }

    [Qml] public bool Horizontal => _orientation == 1;

    [Qml] public bool Vertical => _orientation == 2;

    [Qml] public bool Live { get; set; } = true;

    [Qml] public double TouchDragThreshold { get; set; } = -1;

    bool IKeepsPointerGrab.KeepsGrab => _dragging is not null;

    [Qml]
    public void SetValues(double firstValue, double secondValue)
    {
        First.SetValueField(Clamp(Math.Min(firstValue, secondValue)));
        Second.SetValueField(Clamp(Math.Max(firstValue, secondValue)));
        LayoutChildren();
        Update();
    }

    [Qml]
    public double ValueAt(double position) => _from + (_to - _from) * Math.Clamp(position, 0, 1);

    internal double PositionOf(double v) => _to == _from ? 0 : Math.Clamp((v - _from) / (_to - _from), 0, 1);

    private double Clamp(double v) => _from <= _to ? Math.Clamp(v, _from, _to) : Math.Clamp(v, _to, _from);

    private void Reclamp() => SetValues(First.Value, Second.Value);

    internal void SetNodeValue(RangeSliderNode node, double value)
    {
        value = Clamp(value);
        if (node == First && IsComplete)
            value = Math.Min(value, Second.Value);
        if (node == Second && IsComplete)
            value = Math.Max(value, First.Value);
        node.SetValueField(value);
        LayoutChildren();
        Update();
    }

    internal void StepNode(RangeSliderNode node, int direction)
    {
        var step = _stepSize > 0 ? _stepSize : (_to - _from) / 10;
        SetNodeValue(node, node.Value + direction * step);
    }

    internal void ReplaceHandle(Item? old, Item? value)
    {
        if (old is not null && old.ParentItem == this)
            old.SetParentItem(null);
        if (value is not null)
        {
            value.SetParentItem(this);
            value.Z = 1;
        }
        LayoutChildren();
    }

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var aw = (float)AvailableWidth;
        var ah = (float)AvailableHeight;
        var ox = (float)(LeftPadding - i.X);
        var oy = (float)(TopPadding - i.Y);
        const float hw = 20;
        var trackColor = Enabled ? t.ControlStrongFillColorDefault : t.ControlStrongFillColorDisabled;
        var valueColor = Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled;
        if (_orientation == 2)
        {
            Fluent.FillRound(c, SKRect.Create(ox + aw / 2 - 2, oy, 4, ah), 2, trackColor);
            var a = oy + hw / 2 + (float)Second.VisualPosition * (ah - hw);
            var b = oy + hw / 2 + (float)First.VisualPosition * (ah - hw);
            Fluent.FillRound(c, SKRect.Create(ox + aw / 2 - 2, a, 4, b - a), 2, valueColor);
        }
        else
        {
            Fluent.FillRound(c, SKRect.Create(ox, oy + ah / 2 - 2, aw, 4), 2, trackColor);
            var a = ox + hw / 2 + (float)First.VisualPosition * (aw - hw);
            var b = ox + hw / 2 + (float)Second.VisualPosition * (aw - hw);
            Fluent.FillRound(c, SKRect.Create(a, oy + ah / 2 - 2, b - a, 4), 2, valueColor);
        }
    }, 200, Fluent.ControlHeight));

    protected override void CreateDefaults()
    {
        foreach (var node in new[] { First, Second })
        {
            if (node.Handle is null)
            {
                var n = node;
                var handle = Part(new PaintedItem((c, i, _) =>
                {
                    var t = Theme.Current;
                    var r = i.Bounds;
                    Fluent.FillCircle(c, r.MidX, r.MidY, 10, t.ControlSolidFillColorDefault);
                    Fluent.StrokeCircle(c, r.MidX, r.MidY, 10, t.IsDark ? QColor.Parse("#23ffffff") : QColor.Parse("#29000000"));
                    var inner = n.Pressed ? 5f : n.Hovered ? 7f : 6f;
                    Fluent.FillCircle(c, r.MidX, r.MidY, inner, Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled);
                }, 20, 20));
                handle.SetParentItem(this);
                handle.Z = 1;
                node.SetHandleField(handle);
            }
        }
        base.CreateDefaults();
    }

    protected override void LayoutParts()
    {
        foreach (var node in new[] { First, Second })
        {
            if (node.Handle is not { } h || h.HasAnchors)
                continue;
            if (!h.HasExplicitWidth)
                h.SetLayoutWidth(h.ImplicitWidth);
            if (!h.HasExplicitHeight)
                h.SetLayoutHeight(h.ImplicitHeight);
            if (_orientation == 2)
            {
                h.SetLayoutX(LeftPadding + (AvailableWidth - h.Width) / 2);
                h.SetLayoutY(TopPadding + node.VisualPosition * (AvailableHeight - h.Height));
            }
            else
            {
                h.SetLayoutX(LeftPadding + node.VisualPosition * (AvailableWidth - h.Width));
                h.SetLayoutY(TopPadding + (AvailableHeight - h.Height) / 2);
            }
        }
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        return _orientation == 2 ? (Math.Max(Fluent.ControlHeight, 20), Math.Max(h, 200)) : (w, Math.Max(h, 20));
    }

    private double PositionAt(double x, double y)
    {
        const double hw = 20;
        if (_orientation == 2)
            return 1 - Math.Clamp((y - TopPadding - hw / 2 - _pressOffset) / Math.Max(1, AvailableHeight - hw), 0, 1);
        return Math.Clamp((x - LeftPadding - hw / 2 - _pressOffset) / Math.Max(1, AvailableWidth - hw), 0, 1);
    }

    private double Snapped(double value)
    {
        if (_stepSize <= 0 || _snapMode == 0)
            return value;
        return Clamp(_from + Math.Round((value - _from) / _stepSize) * _stepSize);
    }

    public bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        var pos = PositionAt(x, y);
        var dFirst = Math.Abs(pos - First.Position);
        var dSecond = Math.Abs(pos - Second.Position);
        _dragging = dFirst < dSecond || (dFirst == dSecond && pos < First.Position) ? First : Second;
        _pressOffset = 0;
        _dragging.SetPressed(true);
        StateChanged();
        PointerMove(x, y, buttons, modifiers);
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (_dragging is null)
            return;
        var old = _dragging.Value;
        SetNodeValue(_dragging, Snapped(ValueAt(PositionAt(x, y))));
        if (old != _dragging.Value)
            _dragging.Emit("moved");
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        if (_dragging is null)
            return;
        PointerMove(x, y, buttons, modifiers);
        _dragging.SetPressed(false);
        _dragging = null;
        StateChanged();
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel()
    {
        _dragging?.SetPressed(false);
        _dragging = null;
    }

    public override void HoverMove(double x, double y, int modifiers)
    {
        base.HoverMove(x, y, modifiers);
        foreach (var node in new[] { First, Second })
        {
            var h = node.Handle;
            node.SetHovered(h is not null && x >= h.X && x < h.X + h.Width && y >= h.Y && y < h.Y + h.Height);
        }
        Update();
    }

    public override void HoverLeave()
    {
        base.HoverLeave();
        First.SetHovered(false);
        Second.SetHovered(false);
    }
}

/// <summary>QtQuick.Controls' <c>ProgressBar</c>, drawn as a Windows 11 progress bar.</summary>
public sealed class ProgressBar : Control
{
    private double _from, _to = 1, _value;
    private bool _indeterminate;

    [Qml] public double From { get { Track(); return _from; } set { if (SetField(ref _from, value)) Changed(); } }

    [Qml] public double To { get { Track(); return _to; } set { if (SetField(ref _to, value)) Changed(); } }

    [Qml] public double Value { get { Track(); return _value; } set { if (SetField(ref _value, value)) Changed(); } }

    [Qml] public bool Indeterminate { get { Track(); return _indeterminate; } set { if (SetField(ref _indeterminate, value)) Update(); } }

    [Qml]
    public double Position
    {
        get
        {
            Track("value");
            Track("from");
            Track("to");
            return _to == _from ? 0 : Math.Clamp((Math.Clamp(_value, Math.Min(_from, _to), Math.Max(_from, _to)) - _from) / (_to - _from), 0, 1);
        }
    }

    [Qml] public double VisualPosition => Position;

    private void Changed()
    {
        NotifyChanged("position");
        NotifyChanged("visualPosition");
        Update();
    }

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        if (_indeterminate)
            return;
        var r = i.Bounds;
        using var paint = new SKPaint { Color = (Enabled ? t.ControlStrongStrokeColorDefault : t.ControlStrongStrokeColorDisabled).ToSk(), IsAntialias = true };
        c.DrawRect(SKRect.Create(r.Left, MathF.Round(r.MidY) - 0.5f, r.Width, 1), paint);
    }, 200, 4));

    protected override Item? CreateDefaultContentItem() => Part(new PaintedItem((c, i, context) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var color = Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled;
        var bar = SKRect.Create(r.Left, r.MidY - 1.5f, r.Width, 3);
        if (_indeterminate)
        {
            // A short bar runs across, then a long one, as WinUI's indeterminate progress bar does.
            var cycle = context.Time.TotalSeconds % 2.0 / 2.0;
            var segment = cycle < 0.5 ? 0.4 : 0.6;
            var phase = cycle < 0.5 ? cycle * 2 : (cycle - 0.5) * 2;
            var start = (float)(-segment + phase * (1 + segment)) * r.Width;
            var segRect = SKRect.Create(Math.Max(r.Left, start), bar.Top, 0, 3);
            segRect.Right = Math.Min(r.Right, start + (float)segment * r.Width);
            if (segRect.Width > 0)
                Fluent.FillRound(c, segRect, 1.5f, color);
            if (Visible && Enabled)
                context.WantsAnotherFrame = true;
            return;
        }
        var w = (float)(Position * r.Width);
        if (w > 0)
            Fluent.FillRound(c, SKRect.Create(r.Left, bar.Top, Math.Max(w, 3), 3), 1.5f, color);
    }, 200, 4));
}

/// <summary>QtQuick.Controls' <c>BusyIndicator</c>, drawn as a Windows 11 progress ring.</summary>
public sealed class BusyIndicator : Control
{
    private bool _running = true;

    public BusyIndicator()
    {
        Padding = 0;
    }

    [Qml] public bool Running { get { Track(); return _running; } set { if (SetField(ref _running, value)) Update(); } }

    protected override Item? CreateDefaultContentItem() => Part(new PaintedItem((c, i, context) =>
    {
        if (!_running)
            return;
        var t = Theme.Current;
        var r = i.Bounds;
        var size = Math.Min(r.Width, r.Height);
        var stroke = Math.Max(1.5f, size / 8);
        var oval = SKRect.Create(r.MidX - size / 2 + stroke / 2, r.MidY - size / 2 + stroke / 2, size - stroke, size - stroke);
        // The arc turns while it grows and shrinks, as WinUI's ProgressRing does.
        var seconds = context.Time.TotalSeconds;
        var phase = seconds % 2.0 / 2.0;
        var grow = phase < 0.5 ? Ease(phase * 2) : 1 - Ease((phase - 0.5) * 2);
        var sweep = (float)(10 + 260 * grow);
        var start = (float)(seconds * 360 % 360 + (phase < 0.5 ? 0 : 260 * Ease((phase - 0.5) * 2)));
        using var paint = new SKPaint
        {
            Color = (Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled).ToSk(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
        };
        c.DrawArc(oval, start - 90, sweep, false, paint);
        if (Visible)
            context.WantsAnotherFrame = true;
    }, 32, 32));

    private static double Ease(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
}

/// <summary>QtQuick.Controls' <c>Dial</c>: a knob turned by dragging around it.</summary>
[QmlSignal("moved")]
public sealed class Dial : Control, IPointerTarget, IKeepsPointerGrab
{
    private double _from, _to = 1, _value, _stepSize;
    private bool _pressed;
    private bool _wrap;
    private int _snapMode;
    private Item? _handle;

    [Qml] public double From { get { Track(); return _from; } set { if (SetField(ref _from, value)) Changed(); } }

    [Qml] public double To { get { Track(); return _to; } set { if (SetField(ref _to, value)) Changed(); } }

    [Qml] public double Value { get { Track(); return _value; } set { if (SetField(ref _value, Math.Clamp(value, Math.Min(_from, _to), Math.Max(_from, _to)))) Changed(); } }

    [Qml] public double StepSize { get { Track(); return _stepSize; } set => SetField(ref _stepSize, value); }

    [Qml] public int SnapMode { get { Track(); return _snapMode; } set => SetField(ref _snapMode, value); }

    [Qml] public bool Wrap { get { Track(); return _wrap; } set => SetField(ref _wrap, value); }

    [Qml] public bool Live { get; set; } = true;

    [Qml] public int InputMode { get; set; }

    [Qml("pressed")] public bool IsPressed { get { Track("pressed"); return _pressed; } }

    [Qml] public double Position { get { Track("value"); return _to == _from ? 0 : (_value - _from) / (_to - _from); } }

    /// <summary>The angle of the value, from -140 to 140 degrees, 0 being up.</summary>
    [Qml] public double Angle { get { Track("value"); return -140 + Position * 280; } }

    [Qml] public Item? Handle { get { Track(); return _handle; } set { _handle?.SetParentItem(null); SetField(ref _handle, value); value?.SetParentItem(this); } }

    bool IKeepsPointerGrab.KeepsGrab => _pressed;

    private void Changed()
    {
        NotifyChanged("position");
        NotifyChanged("angle");
        Update();
    }

    [Qml] public void Increase() => Value = _value + (_stepSize > 0 ? _stepSize : (_to - _from) / 10);

    [Qml] public void Decrease() => Value = _value - (_stepSize > 0 ? _stepSize : (_to - _from) / 10);

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var size = Math.Min(r.Width, r.Height) - 8;
        var oval = SKRect.Create(r.MidX - size / 2, r.MidY - size / 2, size, size);
        using var track = new SKPaint { Color = t.ControlStrongFillColorDefault.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round };
        c.DrawArc(oval, 130, 280, false, track);
        using var value = new SKPaint { Color = (Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled).ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round };
        if (Position > 0)
            c.DrawArc(oval, 130, (float)(280 * Position), false, value);
        var a = (Angle - 90) * Math.PI / 180;
        var hx = r.MidX + (float)Math.Cos(a) * size / 2;
        var hy = r.MidY + (float)Math.Sin(a) * size / 2;
        Fluent.FillCircle(c, hx, hy, 10, t.ControlSolidFillColorDefault);
        Fluent.StrokeCircle(c, hx, hy, 10, t.IsDark ? QColor.Parse("#23ffffff") : QColor.Parse("#29000000"));
        Fluent.FillCircle(c, hx, hy, _pressed ? 5 : Hovered ? 7 : 6, Enabled ? t.AccentFillColorDefault : t.AccentFillColorDisabled);
    }, 120, 120));

    private void SetFrom(double x, double y)
    {
        var dx = x - Width / 2;
        var dy = y - Height / 2;
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        angle = Math.Clamp(angle, -140, 140);
        var value = _from + (angle + 140) / 280 * (_to - _from);
        if (_stepSize > 0 && _snapMode != 0)
            value = _from + Math.Round((value - _from) / _stepSize) * _stepSize;
        var old = _value;
        Value = value;
        if (old != _value)
            Emit("moved");
    }

    public bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        SetField(ref _pressed, true, "pressed");
        SetFrom(x, y);
        StateChanged();
        return true;
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (_pressed)
            SetFrom(x, y);
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        SetField(ref _pressed, false, "pressed");
        StateChanged();
    }

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel() => SetField(ref _pressed, false, "pressed");
}

/// <summary>SpinBox's <c>up</c> and <c>down</c> groups.</summary>
public sealed class SpinButton : QObject
{
    private bool _pressed, _hovered;

    [Qml("pressed")] public bool Pressed { get { Track("pressed"); return _pressed; } set => SetField(ref _pressed, value, "pressed"); }

    [Qml] public bool Hovered { get { Track(); return _hovered; } set => SetField(ref _hovered, value); }

    [Qml] public Item? Indicator { get; set; }

    [Qml] public double ImplicitIndicatorWidth => 32;

    [Qml] public double ImplicitIndicatorHeight => 32;
}

/// <summary>QtQuick.Controls' <c>SpinBox</c>: a number with buttons to step it.</summary>
[QmlSignal("valueModified")]
public sealed class SpinBox : Control, IPointerTarget, IKeyTarget, IWheelTarget
{
    protected override int DefaultFocusPolicy => StrongFocus;

    private int _from, _to = 99, _value, _stepSize = 1;
    private bool _editable, _wrap;
    private object? _textFromValue, _valueFromText;
    private IDisposable? _repeat;
    private int _repeatDirection;

    public SpinBox()
    {
        Up = new SpinButton();
        Down = new SpinButton();
        SetDefaultPadding(11, 5, 80, 7);
    }

    [Qml] public int From { get { Track(); return _from; } set { if (SetField(ref _from, value)) Value = _value; } }

    [Qml] public int To { get { Track(); return _to; } set { if (SetField(ref _to, value)) Value = _value; } }

    [Qml]
    public int Value
    {
        get { Track(); return _value; }
        set
        {
            var v = Math.Clamp(value, Math.Min(_from, _to), Math.Max(_from, _to));
            if (SetField(ref _value, v))
            {
                NotifyChanged("displayText");
                SyncText();
            }
        }
    }

    [Qml] public int StepSize { get { Track(); return _stepSize; } set => SetField(ref _stepSize, value); }

    [Qml] public bool Editable { get { Track(); return _editable; } set => SetField(ref _editable, value); }

    [Qml] public bool Wrap { get { Track(); return _wrap; } set => SetField(ref _wrap, value); }

    [Qml] public SpinButton Up { get; }

    [Qml] public SpinButton Down { get; }

    [Qml] public object? Validator { get; set; }

    [Qml] public int InputMethodHints { get; set; }

    [Qml] public bool InputMethodComposing => false;

    [Qml]
    public object? TextFromValue
    {
        get { Track(); return _textFromValue; }
        set
        {
            _textFromValue = value is Jint.Native.JsValue js ? js : value;
            NotifyChanged("textFromValue");
            NotifyChanged("displayText");
            SyncText();
        }
    }

    [Qml]
    public object? ValueFromText
    {
        get { Track(); return _valueFromText; }
        set { _valueFromText = value; NotifyChanged("valueFromText"); }
    }

    [Qml]
    public string DisplayText
    {
        get
        {
            Track("value");
            Track("textFromValue");
            return FormatValue(_value);
        }
    }

    private string FormatValue(int value)
    {
        if (_textFromValue is Jint.Native.JsValue fn && fn.IsCallable() && Engine is not null)
        {
            try
            {
                return Engine.Js.CallFunction(fn, null, [value, null]).ToString();
            }
            catch (Jint.Runtime.JavaScriptException e)
            {
                Engine.Log.Warn("scene", e.Message);
            }
        }
        return value.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    [Qml]
    public void Increase() => Step(1);

    [Qml]
    public void Decrease() => Step(-1);

    private void Step(int direction)
    {
        var old = _value;
        var next = _value + direction * _stepSize;
        var lo = Math.Min(_from, _to);
        var hi = Math.Max(_from, _to);
        if (_wrap)
        {
            if (next > hi)
                next = lo;
            else if (next < lo)
                next = hi;
        }
        Value = next;
        if (old != _value)
            Emit("valueModified");
    }

    private void SyncText()
    {
        if (ContentItem is Text text && HasDefaultContentItem)
            text.Text_ = FormatValue(_value);
    }

    protected override Item? CreateDefaultContentItem()
    {
        var text = new Text { VerticalAlignment = QtEnums.AlignVCenter };
        CopyFont(Font, text.Font);
        text.Text_ = FormatValue(_value);
        return Part(text);
    }

    protected override void OnFontChanged()
    {
        if (ContentItem is Text text && HasDefaultContentItem)
            CopyFont(Font, text.Font);
    }

    private SKRect UpRect => SKRect.Create((float)Width - 76, 4, 36, (float)Height - 8);

    private SKRect DownRect => SKRect.Create((float)Width - 40, 4, 36, (float)Height - 8);

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var fill = !Enabled ? t.ControlFillColorDisabled : Hovered ? t.ControlFillColorSecondary : t.ControlFillColorDefault;
        Fluent.ElevatedRect(c, r, Fluent.ControlCornerRadius, fill, t.ControlStrokeColorDefault, t.ControlStrongStrokeColorDefault);
        foreach (var (rect, button, glyph) in new[] { (UpRect, Up, Fluent.ChevronUp), (DownRect, Down, Fluent.ChevronDown) })
        {
            var rr = rect;
            rr.Offset(-(float)i.X, -(float)i.Y);
            if (button.Pressed)
                Fluent.FillRound(c, rr, Fluent.ControlCornerRadius, t.SubtleFillColorTertiary);
            else if (button.Hovered)
                Fluent.FillRound(c, rr, Fluent.ControlCornerRadius, t.SubtleFillColorSecondary);
            Fluent.DrawChevron(c, glyph, rr.MidX, rr.MidY, 12, Enabled ? (button.Pressed ? t.TextFillColorTertiary : t.TextFillColorSecondary) : t.TextFillColorDisabled);
        }
    }, 160, Fluent.ControlHeight));

    private SpinButton? ButtonAt(double x, double y)
    {
        if (UpRect.Contains((float)x, (float)y))
            return Up;
        if (DownRect.Contains((float)x, (float)y))
            return Down;
        return null;
    }

    public bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        var b = ButtonAt(x, y);
        if (b is null)
            return true;
        b.Pressed = true;
        _repeatDirection = b == Up ? 1 : -1;
        Step(_repeatDirection);
        _repeat?.Dispose();
        if (Engine is not null)
            _repeat = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(300), RepeatStep);
        Update();
        return true;
    }

    private void RepeatStep()
    {
        if (IsDestroyed || Engine is null || !(Up.Pressed || Down.Pressed))
            return;
        Step(_repeatDirection);
        _repeat = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(100), RepeatStep);
    }

    public void PointerMove(double x, double y, int buttons, int modifiers)
    {
    }

    public void PointerRelease(double x, double y, int button, int buttons, int modifiers) => PointerCancel();

    public void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers) => PointerPress(x, y, button, buttons, modifiers);

    public void PointerCancel()
    {
        _repeat?.Dispose();
        Up.Pressed = false;
        Down.Pressed = false;
        Update();
    }

    public override void HoverMove(double x, double y, int modifiers)
    {
        base.HoverMove(x, y, modifiers);
        var b = ButtonAt(x, y);
        Up.Hovered = b == Up;
        Down.Hovered = b == Down;
        Update();
    }

    public override void HoverLeave()
    {
        base.HoverLeave();
        Up.Hovered = false;
        Down.Hovered = false;
    }

    public bool KeyPress(KeyEvent e)
    {
        switch (e.Key)
        {
            case 0x01000013:
                Increase();
                return true;
            case 0x01000015:
                Decrease();
                return true;
        }
        return false;
    }

    public bool KeyRelease(KeyEvent e) => false;

    public bool Wheel(double x, double y, double angleX, double angleY, double pixelX, double pixelY, int buttons, int modifiers, bool inverted)
    {
        if (!WheelEnabled)
            return false;
        Step(angleY > 0 ? 1 : -1);
        return true;
    }

    protected override void OnDestroy()
    {
        _repeat?.Dispose();
        base.OnDestroy();
    }
}
