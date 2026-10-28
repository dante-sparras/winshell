using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>QtQuick.Controls' <c>AbstractButton</c>: press, click, check and the signals around them.</summary>
[QmlSignal("clicked")]
[QmlSignal("pressed")]
[QmlSignal("released")]
[QmlSignal("canceled")]
[QmlSignal("doubleClicked")]
[QmlSignal("pressAndHold")]
[QmlSignal("toggled")]
public class AbstractButton : Control, IPointerTarget, IKeyTarget
{
    private string _text = "";
    private bool _pressed;
    private bool? _down;
    private bool _checked;
    private bool _checkable;
    private bool? _autoExclusive;
    private bool _autoRepeat;
    private int _autoRepeatDelay = 300;
    private int _autoRepeatInterval = 100;
    private int _display = (int)DisplayValue.TextBesideIcon;
    private Item? _indicator;
    private bool _indicatorSet, _defaultIndicator;
    private PropertyWatch? _indicatorWatch;
    private double _pressX, _pressY;
    private IDisposable? _holdTimer, _repeatTimer;
    private bool _held, _doubleClick, _keyPressed;
    private ButtonGroup? _group;

    public AbstractButton()
    {
        Icon = new IconGroup();
        Icon.Changed += () =>
        {
            NotifyChanged("icon");
            SyncLabel();
        };
    }

    [QmlEnum]
    public enum DisplayValue
    {
        IconOnly = 0,
        TextOnly = 1,
        TextBesideIcon = 2,
        TextUnderIcon = 3,
    }

    [Qml]
    public string Text
    {
        get { Track(); return _text; }
        set
        {
            if (SetField(ref _text, value ?? ""))
                SyncLabel();
        }
    }

    [Qml("pressed")]
    public bool IsPressed
    {
        get { Track("pressed"); return _pressed; }
    }

    /// <summary>Whether the button looks pressed. Follows <c>pressed</c> unless set; undefined resets it.</summary>
    [Qml]
    public object? Down
    {
        get { Track(); return IsDown; }
        set
        {
            _down = value is null ? null : ValueConverter.ToBool(value);
            NotifyChanged("down");
            StateChanged();
        }
    }

    public bool IsDown => _down ?? (_pressed || _keyPressed);

    [Qml]
    public bool Checked
    {
        get { Track(); return _checked; }
        set => SetChecked(value, fromUser: false);
    }

    protected void SetChecked(bool value, bool fromUser)
    {
        if (_checked == value)
            return;
        _checked = value;
        NotifyChanged("checked");
        OnCheckedChanged();
        if (value)
            UncheckOthers();
        StateChanged();
        if (fromUser)
            Emit("toggled");
    }

    protected virtual void OnCheckedChanged()
    {
    }

    [Qml]
    public bool Checkable
    {
        get { Track(); return _checkable; }
        set => SetField(ref _checkable, value);
    }

    [Qml]
    public bool AutoExclusive
    {
        get { Track(); return _autoExclusive ?? DefaultAutoExclusive; }
        set { _autoExclusive = value; NotifyChanged("autoExclusive"); }
    }

    protected virtual bool DefaultAutoExclusive => false;

    [Qml]
    public bool AutoRepeat { get { Track(); return _autoRepeat; } set => SetField(ref _autoRepeat, value); }

    [Qml]
    public int AutoRepeatDelay { get { Track(); return _autoRepeatDelay; } set => SetField(ref _autoRepeatDelay, value); }

    [Qml]
    public int AutoRepeatInterval { get { Track(); return _autoRepeatInterval; } set => SetField(ref _autoRepeatInterval, value); }

    [Qml]
    public int Display
    {
        get { Track(); return _display; }
        set
        {
            if (SetField(ref _display, value))
                SyncLabel();
        }
    }

    [Qml]
    public IconGroup Icon { get; }

    [Qml]
    public double PressX { get { Track(); return _pressX; } }

    [Qml]
    public double PressY { get { Track(); return _pressY; } }

    [Qml]
    public object? Action { get; set; }

    internal ButtonGroup? Group
    {
        get => _group;
        set => _group = value;
    }

    internal void SetCheckedFromGroup(bool value) => SetChecked(value, fromUser: true);

    [Qml]
    public Item? Indicator
    {
        get { Track(); return _indicator; }
        set
        {
            _indicatorSet = true;
            SetIndicator(value, false);
        }
    }

    [Qml]
    public double ImplicitIndicatorWidth { get { Track(); return _indicator?.ImplicitWidth ?? 0; } }

    [Qml]
    public double ImplicitIndicatorHeight { get { Track(); return _indicator?.ImplicitHeight ?? 0; } }

    private void SetIndicator(Item? value, bool isDefault)
    {
        if (_indicator == value)
            return;
        var old = _indicator;
        _indicatorWatch?.Dispose();
        _indicatorWatch = null;
        _indicator = value;
        RetirePart(old, _defaultIndicator);
        _defaultIndicator = isDefault && value is not null;
        if (value is not null)
        {
            value.SetParentItem(this);
            _indicatorWatch = new PropertyWatch(value, () =>
            {
                NotifyChanged("implicitIndicatorWidth");
                NotifyChanged("implicitIndicatorHeight");
                LayoutChildren();
                UpdateImplicitSize();
            }, "implicitWidth", "implicitHeight", "width");
        }
        NotifyChanged("indicator");
        NotifyChanged("implicitIndicatorWidth");
        NotifyChanged("implicitIndicatorHeight");
        SyncLabel();
        LayoutChildren();
        UpdateImplicitSize();
    }

    /// <summary>The indicator used when QML sets none, such as a check box's box.</summary>
    protected virtual Item? CreateDefaultIndicator() => null;

    protected override void CreateDefaults()
    {
        if (!_indicatorSet && _indicator is null && CreateDefaultIndicator() is { } indicator)
            SetIndicator(indicator, true);
        base.CreateDefaults();
    }

    /// <summary>Places a check box's or switch's indicator at the left, centered vertically.</summary>
    protected void PlaceIndicatorLeft()
    {
        if (_indicator is not { } ind || ind.ParentItem != this || ind.HasAnchors)
            return;
        using var _ = DependencyTracker.Suspend();
        if (!ind.HasExplicitWidth)
            ind.SetLayoutWidth(ind.ImplicitWidth);
        if (!ind.HasExplicitHeight)
            ind.SetLayoutHeight(ind.ImplicitHeight);
        ind.SetLayoutX(LeftPadding);
        ind.SetLayoutY(TopPadding + (AvailableHeight - ind.Height) / 2);
    }

    protected override (double Width, double Height) ComputeImplicitSize()
    {
        var (w, h) = base.ComputeImplicitSize();
        if (_indicator is { } ind)
        {
            using var _ = DependencyTracker.Suspend();
            w = Math.Max(w, ind.ImplicitWidth + LeftPadding + RightPadding);
            h = Math.Max(h, ind.ImplicitHeight + TopPadding + BottomPadding);
        }
        return (w, h);
    }

    // The default label

    protected override Item? CreateDefaultContentItem()
    {
        var label = new IconLabel
        {
            ColorSource = () => ContentColor,
            IconColorSource = () => Icon.Color.IsValid ? Icon.Color : ContentColor,
            BaseDirectory = CreationContext?.Imports?.Directory ?? Engine?.ShellDir,
        };
        ConfigureLabel(label);
        return Part(label);
    }

    /// <summary>Sets up the default label: alignment, spacing, padding.</summary>
    protected virtual void ConfigureLabel(IconLabel label)
    {
    }

    /// <summary>The color of the default label for the current state.</summary>
    public virtual QColor ContentColor
    {
        get
        {
            var t = Theme.Current;
            if (!Enabled)
                return t.TextDisabled;
            return IsDown ? t.TextSecondary : t.TextColor;
        }
    }

    /// <summary>Pushes the text, font and icon into the default label.</summary>
    protected void SyncLabel()
    {
        if (ContentItem is not IconLabel label || !HasDefaultContentItem)
            return;
        using var _ = DependencyTracker.Suspend();
        label.Text = _text;
        label.Font = Font.Spec;
        label.Display = _display;
        label.Spacing = LabelSpacing;
        label.SetIcon(Icon.EffectiveSource, Icon.Width, Icon.Height);
        ConfigureLabel(label);
    }

    protected virtual double LabelSpacing => Spacing;

    protected override void OnFontChanged() => SyncLabel();

    protected override void OnSpacingChanged() => SyncLabel();

    protected override void OnContentItemChanged(Item? oldItem, Item? newItem) => SyncLabel();

    // Actions

    [Qml]
    public void Click()
    {
        if (!Enabled)
            return;
        Trigger();
    }

    [Qml]
    public void AnimateClick() => Click();

    [Qml]
    public void Toggle() => SetChecked(!_checked, fromUser: false);

    /// <summary>The checked state a click moves to.</summary>
    protected virtual void NextCheckState()
    {
        if (!_checkable)
            return;
        if (_checked && (AutoExclusive || _group is { Exclusive: true }))
            return;
        SetChecked(!_checked, fromUser: true);
    }

    private void Trigger()
    {
        NextCheckState();
        Emit("clicked");
        _group?.OnButtonClicked(this);
        OnClicked();
    }

    protected virtual void OnClicked()
    {
    }

    private void UncheckOthers()
    {
        if (_group is { Exclusive: true } group)
        {
            group.OnButtonChecked(this);
            return;
        }
        if (!AutoExclusive || ParentItem is not { } parent)
            return;
        foreach (var sibling in parent.ChildItems)
        {
            if (sibling != this && sibling is AbstractButton { _checked: true, Group: null } other && other.AutoExclusive)
                other.SetChecked(false, fromUser: true);
        }
    }

    private void SetPressed(bool value)
    {
        if (_pressed == value)
            return;
        _pressed = value;
        NotifyChanged("pressed");
        if (_down is null)
            NotifyChanged("down");
        StateChanged();
    }

    // Pointer

    public virtual bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public virtual bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        if (!Enabled)
            return false;
        _pressX = x;
        _pressY = y;
        NotifyChanged("pressX");
        NotifyChanged("pressY");
        _held = false;
        _doubleClick = false;
        SetPressed(true);
        Emit("pressed");
        StartTimers();
        return true;
    }

    private void StartTimers()
    {
        _holdTimer?.Dispose();
        _repeatTimer?.Dispose();
        if (Engine is null)
            return;
        if (_autoRepeat)
        {
            _repeatTimer = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(_autoRepeatDelay), Repeat);
            return;
        }
        if (GetSignal("pressAndHold")?.Count > 0)
        {
            _holdTimer = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(800), () =>
            {
                if (!_pressed || IsDestroyed)
                    return;
                _held = true;
                Emit("pressAndHold");
            });
        }
    }

    private void Repeat()
    {
        if (!_pressed || IsDestroyed || Engine is null)
            return;
        Emit("released");
        Emit("clicked");
        Emit("pressed");
        _repeatTimer = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(_autoRepeatInterval), Repeat);
    }

    public virtual void PointerMove(double x, double y, int buttons, int modifiers)
    {
        _pressX = x;
        _pressY = y;
        NotifyChanged("pressX");
        NotifyChanged("pressY");
        SetPressed(HitTest(x, y));
    }

    public virtual void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        _holdTimer?.Dispose();
        _repeatTimer?.Dispose();
        var wasPressed = _pressed;
        SetPressed(false);
        if (!wasPressed)
        {
            Emit("canceled");
            return;
        }
        Emit("released");
        if (HitTest(x, y) && !_held && !_doubleClick)
            Trigger();
        _doubleClick = false;
    }

    public virtual void PointerDoubleClick(double x, double y, int button, int buttons, int modifiers)
    {
        if (GetSignal("doubleClicked")?.Count > 0)
        {
            SetPressed(true);
            Emit("pressed");
            _doubleClick = true;
            Emit("doubleClicked");
            return;
        }
        PointerPress(x, y, button, buttons, modifiers);
    }

    public virtual void PointerCancel()
    {
        _holdTimer?.Dispose();
        _repeatTimer?.Dispose();
        if (_pressed)
        {
            SetPressed(false);
            Emit("canceled");
        }
    }

    // Keys

    public virtual bool KeyPress(KeyEvent e)
    {
        if (e.Key != 0x20 || !Enabled)
            return false;
        if (!e.IsAutoRepeat)
        {
            _keyPressed = true;
            NotifyChanged("down");
            StateChanged();
            Emit("pressed");
        }
        return true;
    }

    public virtual bool KeyRelease(KeyEvent e)
    {
        if (e.Key != 0x20 || !_keyPressed)
            return false;
        _keyPressed = false;
        NotifyChanged("down");
        StateChanged();
        Emit("released");
        Trigger();
        return true;
    }

    protected override void OnDestroy()
    {
        _holdTimer?.Dispose();
        _repeatTimer?.Dispose();
        _indicatorWatch?.Dispose();
        _group?.Remove(this);
        base.OnDestroy();
    }
}

/// <summary>QtQuick.Controls' <c>Button</c>, drawn as a Windows 11 button.</summary>
public class Button : AbstractButton
{
    private bool _flat;
    private bool _highlighted;

    public Button()
    {
        SetDefaultPadding(12, 5, 12, 7);
        Spacing = 8;
    }

    [Qml]
    public bool Flat { get { Track(); return _flat; } set { if (SetField(ref _flat, value)) StateChanged(); } }

    [Qml]
    public bool Highlighted { get { Track(); return _highlighted; } set { if (SetField(ref _highlighted, value)) StateChanged(); } }

    /// <summary>Whether the button shows the accent color: highlighted, or a checked toggle button.</summary>
    public virtual bool IsAccent => (_highlighted || Checked) && !_flat;

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) => PaintBackground(c, i.Bounds), 120, Fluent.ControlHeight));

    protected virtual float Radius(SKRect bounds) => Fluent.CornerRadius;

    protected void PaintBackground(SKCanvas canvas, SKRect r)
    {
        var t = Theme.Current;
        var radius = Radius(r);
        var down = IsDown;
        if (IsAccent)
        {
            if (!Enabled)
            {
                Fluent.FillRound(canvas, r, radius, t.AccentFillDisabled);
                return;
            }
            var fill = down ? t.AccentFillTertiary : Hovered ? t.AccentFillSecondary : t.AccentFill;
            if (down)
                Fluent.FillRound(canvas, r, radius, fill);
            else
                Fluent.ElevatedRect(canvas, r, radius, fill, StrokeOnAccent, t.AccentStrokeSecondary);
            return;
        }
        if (_flat || IsSubtle)
        {
            var fill = !Enabled ? QColor.Transparent : down ? t.SubtleFillTertiary : Hovered ? t.SubtleFillSecondary : (_highlighted || Checked) ? t.SubtleFillSecondary : QColor.Transparent;
            Fluent.FillRound(canvas, r, radius, fill);
            return;
        }
        if (!Enabled)
        {
            Fluent.ElevatedRect(canvas, r, radius, t.ControlFillDisabled, t.ControlStroke, t.ControlStroke);
            return;
        }
        if (down)
        {
            Fluent.ElevatedRect(canvas, r, radius, t.ControlFillTertiary, t.ControlStroke, t.ControlStroke);
            return;
        }
        Fluent.ElevatedRect(canvas, r, radius, Hovered ? t.ControlFillSecondary : t.ControlFill, t.ControlStroke, t.ControlStrokeSecondary);
    }

    internal static QColor StrokeOnAccent => QColor.Parse("#14ffffff");

    /// <summary>Whether the button has no fill at rest, as a tool button.</summary>
    protected virtual bool IsSubtle => false;

    public override QColor ContentColor
    {
        get
        {
            var t = Theme.Current;
            if (IsAccent)
                return !Enabled ? t.TextOnAccentDisabled : IsDown ? t.TextOnAccentSecondary : t.TextOnAccent;
            return base.ContentColor;
        }
    }
}

/// <summary>QtQuick.Controls' <c>RoundButton</c>: a button with fully rounded ends.</summary>
public sealed class RoundButton : Button
{
    private double? _radius;

    public RoundButton()
    {
        SetDefaultPadding(8, 8, 8, 8);
    }

    [Qml("radius")]
    public double Radius_
    {
        get { Track(); using (DependencyTracker.Suspend()) return _radius ?? Math.Min(Width, Height) / 2; }
        set { _radius = value; NotifyChanged("radius"); Update(); }
    }

    protected override float Radius(SKRect bounds) => (float)(_radius ?? Math.Min(bounds.Width, bounds.Height) / 2);

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) => PaintBackground(c, i.Bounds), Fluent.ControlHeight, Fluent.ControlHeight));
}

/// <summary>QtQuick.Controls' <c>ToolButton</c>: a button without a fill until hovered.</summary>
public sealed class ToolButton : Button
{
    public ToolButton()
    {
        SetDefaultPadding(8, 6, 8, 6);
    }

    protected override bool IsSubtle => true;

    public override bool IsAccent => false;

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) => PaintBackground(c, i.Bounds), Fluent.ControlHeight, Fluent.ControlHeight));

    public override QColor ContentColor
    {
        get
        {
            var t = Theme.Current;
            if (!Enabled)
                return t.TextDisabled;
            if (Checked)
                return t.AccentText;
            return IsDown ? t.TextSecondary : t.TextColor;
        }
    }
}

/// <summary>A button with an indicator before its text: check box, radio button, switch.</summary>
public abstract class IndicatorButton : AbstractButton
{
    protected IndicatorButton()
    {
        SetDefaultPadding(0, 6, 0, 6);
        Spacing = 8;
        Checkable = true;
    }

    protected override void ConfigureLabel(IconLabel label)
    {
        label.Alignment = QtEnums.AlignLeft | QtEnums.AlignVCenter;
        using var _ = DependencyTracker.Suspend();
        label.LeftPadding = Indicator is { } ind ? ind.Width + Spacing : 0;
        if (Text.Length == 0)
            label.LeftPadding = Indicator?.Width ?? 0;
    }

    protected override void LayoutParts()
    {
        PlaceIndicatorLeft();
        SyncLabel();
    }

    protected override Item? CreateDefaultBackground() => null;
}

/// <summary>QtQuick.Controls' <c>CheckBox</c>, with a Windows 11 check box.</summary>
public sealed class CheckBox : IndicatorButton
{
    private bool _tristate;
    private int _checkState;
    private bool _syncing;

    [Qml]
    public bool Tristate { get { Track(); return _tristate; } set => SetField(ref _tristate, value); }

    [Qml]
    public int CheckState
    {
        get { Track(); return _checkState; }
        set
        {
            if (!SetField(ref _checkState, value))
                return;
            _syncing = true;
            Checked = value == 2;
            _syncing = false;
            StateChanged();
        }
    }

    [Qml]
    public object? NextCheckStateFunction { get; set; }

    protected override void OnCheckedChanged()
    {
        if (_syncing)
            return;
        _checkState = Checked ? 2 : 0;
        NotifyChanged("checkState");
    }

    protected override void NextCheckState()
    {
        if (!Checkable)
            return;
        if (_tristate)
        {
            var next = (_checkState + 1) % 3;
            _checkState = next;
            _syncing = true;
            SetChecked(next == 2, fromUser: true);
            _syncing = false;
            NotifyChanged("checkState");
            StateChanged();
            if (next == 1)
                Emit("toggled");
            return;
        }
        base.NextCheckState();
    }

    protected override Item? CreateDefaultIndicator() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var box = SKRect.Create(r.MidX - 10, r.MidY - 10, 20, 20);
        var on = _checkState != 0;
        if (on)
        {
            var fill = !Enabled ? t.AccentFillDisabled : IsDown ? t.AccentFillTertiary : Hovered ? t.AccentFillSecondary : t.AccentFill;
            Fluent.FillRound(c, box, Fluent.CornerRadius, fill);
            var mark = !Enabled ? t.TextOnAccentDisabled : IsDown ? t.TextOnAccentSecondary : t.TextOnAccent;
            if (_checkState == 2)
            {
                Fluent.DrawCheck(c, box.MidX, box.MidY, 12, mark);
            }
            else
            {
                using var paint = new SKPaint { Color = mark.ToSk(), IsAntialias = true };
                c.DrawRoundRect(SKRect.Create(box.MidX - 4, box.MidY - 0.5f, 8, 1), 0.5f, 0.5f, paint);
            }
            return;
        }
        var bg = !Enabled ? t.ControlAltFillSecondary : IsDown ? t.ControlAltFillTertiary.Darker(1.1) : Hovered ? t.ControlAltFillTertiary : t.ControlAltFillSecondary;
        Fluent.FillRound(c, box, Fluent.CornerRadius, bg);
        Fluent.StrokeRound(c, box, Fluent.CornerRadius, !Enabled ? t.ControlStrongStrokeDisabled : t.ControlStrongStroke);
    }, 20, 20));
}

/// <summary>QtQuick.Controls' <c>RadioButton</c>, with a Windows 11 radio circle.</summary>
public sealed class RadioButton : IndicatorButton
{
    protected override bool DefaultAutoExclusive => true;

    protected override Item? CreateDefaultIndicator() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        float cx = r.MidX, cy = r.MidY;
        if (Checked)
        {
            var fill = !Enabled ? t.AccentFillDisabled : IsDown ? t.AccentFillTertiary : Hovered ? t.AccentFillSecondary : t.AccentFill;
            Fluent.FillCircle(c, cx, cy, 10, fill);
            var dot = IsDown ? 5f : Hovered ? 7f : 6f;
            Fluent.FillCircle(c, cx, cy, dot, Enabled ? t.TextOnAccent : t.TextOnAccentDisabled);
            return;
        }
        var bg = IsDown ? t.ControlAltFillTertiary.Darker(1.1) : Hovered ? t.ControlAltFillTertiary : t.ControlAltFillSecondary;
        Fluent.FillCircle(c, cx, cy, 10, bg);
        Fluent.StrokeCircle(c, cx, cy, 10, !Enabled ? t.ControlStrongStrokeDisabled : t.ControlStrongStroke);
        if (IsDown && Enabled)
            Fluent.FillCircle(c, cx, cy, 5, t.ControlStrongFill);
    }, 20, 20));
}

/// <summary>QtQuick.Controls' <c>Switch</c>, drawn as a Windows 11 toggle switch.</summary>
public sealed class Switch : IndicatorButton
{
    private double _position;
    private bool _dragging;
    private double _dragStartX;

    public Switch()
    {
        Spacing = 12;
    }

    [Qml]
    public double Position
    {
        get { Track(); return _dragging ? _position : Checked ? 1 : 0; }
    }

    [Qml]
    public double VisualPosition => Position;

    protected override Item? CreateDefaultIndicator() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var track = SKRect.Create(r.MidX - 20, r.MidY - 10, 40, 20);
        var pos = _dragging ? _position : Checked ? 1 : 0;
        var on = pos >= 0.5;
        if (on)
        {
            var fill = !Enabled ? t.AccentFillDisabled : IsDown ? t.AccentFillTertiary : Hovered ? t.AccentFillSecondary : t.AccentFill;
            Fluent.FillRound(c, track, 10, fill);
        }
        else
        {
            var fill = !Enabled ? t.ControlAltFillSecondary : IsDown ? t.ControlAltFillTertiary.Darker(1.1) : Hovered ? t.ControlAltFillTertiary : t.ControlAltFillSecondary;
            Fluent.FillRound(c, track, 10, fill);
            Fluent.StrokeRound(c, track, 10, !Enabled ? t.ControlStrongStrokeDisabled : t.ControlStrongStroke);
        }
        float kw = IsDown ? 17 : Hovered ? 14 : 12;
        float kh = IsDown ? 14 : Hovered ? 14 : 12;
        var minX = track.Left + 4 + kh / 2;
        var maxX = track.Right - 4 - kh / 2;
        var cx = (float)(minX + (maxX - minX) * pos);
        if (IsDown)
            cx += on ? -(kw - kh) / 2 : (kw - kh) / 2;
        var knob = SKRect.Create(cx - kw / 2, track.MidY - kh / 2, kw, kh);
        var knobColor = on ? (Enabled ? t.TextOnAccent : t.TextOnAccentDisabled) : (Enabled ? t.TextSecondary : t.TextDisabled);
        Fluent.FillRound(c, knob, kh / 2, knobColor);
    }, 40, 20));

    public override bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        _dragStartX = x;
        return base.PointerPress(x, y, button, buttons, modifiers);
    }

    public override void PointerMove(double x, double y, int buttons, int modifiers)
    {
        base.PointerMove(x, y, buttons, modifiers);
        if (!IsPressed && !_dragging)
            return;
        if (!_dragging && Math.Abs(x - _dragStartX) < 4)
            return;
        _dragging = true;
        var start = Checked ? 1 : 0;
        _position = Math.Clamp(start + (x - _dragStartX) / 20, 0, 1);
        NotifyChanged("position");
        NotifyChanged("visualPosition");
        Update();
    }

    public override void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        if (_dragging)
        {
            _dragging = false;
            var on = _position >= 0.5;
            base.PointerCancel();
            Emit("released");
            if (on != Checked)
            {
                SetChecked(on, fromUser: true);
                Emit("clicked");
            }
            NotifyChanged("position");
            NotifyChanged("visualPosition");
            return;
        }
        base.PointerRelease(x, y, button, buttons, modifiers);
    }

    protected override void OnCheckedChanged()
    {
        NotifyChanged("position");
        NotifyChanged("visualPosition");
    }
}

/// <summary>QtQuick.Controls' <c>ItemDelegate</c>: a row of a list, highlighted on hover.</summary>
public class ItemDelegate : AbstractButton
{
    private bool _highlighted;

    public ItemDelegate()
    {
        SetDefaultPadding(16, 9, 12, 11);
        Spacing = 12;
        TopInset = 2;
        BottomInset = 2;
        LeftInset = 4;
        RightInset = 4;
    }

    [Qml]
    public bool Highlighted { get { Track(); return _highlighted; } set { if (SetField(ref _highlighted, value)) StateChanged(); } }

    protected override void ConfigureLabel(IconLabel label) => label.Alignment = QtEnums.AlignLeft | QtEnums.AlignVCenter;

    /// <summary>Whether to show the accent pill of a selected row.</summary>
    protected virtual bool ShowsSelection => _highlighted || Checked;

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        var fill = !Enabled ? QColor.Transparent : IsDown ? t.SubtleFillTertiary : Hovered || ShowsSelection ? t.SubtleFillSecondary : QColor.Transparent;
        Fluent.FillRound(c, r, Fluent.CornerRadius, fill);
        if (ShowsSelection && Enabled)
        {
            var h = IsDown ? 10f : 16f;
            Fluent.FillRound(c, SKRect.Create(r.Left, r.MidY - h / 2, 3, h), 1.5f, t.AccentFill);
        }
    }, 100, 40));
}

/// <summary>QtQuick.Controls' <c>TabButton</c>: a tab with an accent bar under the current one.</summary>
public sealed class TabButton : AbstractButton
{
    public TabButton()
    {
        SetDefaultPadding(12, 6, 12, 8);
        Checkable = true;
        Spacing = 8;
    }

    protected override bool DefaultAutoExclusive => true;

    public override QColor ContentColor
    {
        get
        {
            var t = Theme.Current;
            if (!Enabled)
                return t.TextDisabled;
            if (Checked)
                return t.TextColor;
            return IsDown ? t.TextTertiary : Hovered ? t.TextColor : t.TextSecondary;
        }
    }

    protected override Item? CreateDefaultBackground() => Part(new PaintedItem((c, i, _) =>
    {
        var t = Theme.Current;
        var r = i.Bounds;
        if (Hovered && !Checked && Enabled)
            Fluent.FillRound(c, SKRect.Inflate(r, -2, -2), Fluent.CornerRadius, t.SubtleFillSecondary);
        if (Checked)
        {
            var w = IsDown ? 10f : 16f;
            Fluent.FillRound(c, SKRect.Create(r.MidX - w / 2, r.Bottom - 3, w, 3), 1.5f, Enabled ? t.AccentFill : t.AccentFillDisabled);
        }
    }, 60, Fluent.ControlHeight + 4));
}

/// <summary>QtQuick.Controls' <c>ButtonGroup</c>: makes buttons exclusive without sharing a parent.</summary>
[QmlSignal("clicked", "button")]
[QmlDefaultProperty("buttons")]
public sealed class ButtonGroup : QObject
{
    private bool _exclusive = true;
    private AbstractButton? _checkedButton;
    private readonly List<AbstractButton> _buttons = [];

    [Qml]
    public object Buttons
    {
        get { Track(); return _buttons.ToList(); }
        set
        {
            foreach (var b in _buttons.ToArray())
                Remove(b);
            foreach (var item in ValueConverter.ToList(value))
            {
                if (item is AbstractButton button)
                    AddButton(button);
            }
        }
    }

    [Qml]
    public bool Exclusive { get { Track(); return _exclusive; } set => SetField(ref _exclusive, value); }

    [Qml]
    public AbstractButton? CheckedButton
    {
        get { Track(); return _checkedButton; }
        set
        {
            if (value is not null)
                value.Checked = true;
            else
                _checkedButton?.SetCheckedFromGroup(false);
        }
    }

    [Qml]
    public int CheckState
    {
        get
        {
            Track("checkedButton");
            var count = _buttons.Count(b => b.Checked);
            return count == 0 ? 0 : count == _buttons.Count ? 2 : 1;
        }
    }

    [Qml]
    public void AddButton(AbstractButton button)
    {
        if (_buttons.Contains(button))
            return;
        button.Group?.Remove(button);
        _buttons.Add(button);
        button.Group = this;
        NotifyChanged("buttons");
        if (button.Checked)
            OnButtonChecked(button);
    }

    [Qml]
    public void RemoveButton(AbstractButton button) => Remove(button);

    internal void Remove(AbstractButton button)
    {
        if (!_buttons.Remove(button))
            return;
        if (button.Group == this)
            button.Group = null;
        if (_checkedButton == button)
            SetCheckedButton(null);
        NotifyChanged("buttons");
    }

    internal void OnButtonChecked(AbstractButton button)
    {
        if (_exclusive)
        {
            foreach (var other in _buttons)
            {
                if (other != button && other.Checked)
                    other.SetCheckedFromGroup(false);
            }
        }
        SetCheckedButton(button);
    }

    internal void OnButtonClicked(AbstractButton button) => Emit("clicked", button);

    private void SetCheckedButton(AbstractButton? button) => SetField(ref _checkedButton, button, nameof(CheckedButton));
}

/// <summary>The attached <c>ButtonGroup.group</c> property.</summary>
public sealed class ButtonGroupAttached(QObject owner) : QObject
{
    private ButtonGroup? _group;

    [Qml]
    public ButtonGroup? Group
    {
        get { Track(); return _group; }
        set
        {
            if (owner is not AbstractButton button)
                return;
            if (_group == value)
                return;
            _group?.RemoveButton(button);
            _group = value;
            value?.AddButton(button);
            NotifyChanged("group");
        }
    }
}
