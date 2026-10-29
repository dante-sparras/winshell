using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>Places a text control's background behind it, the size of the control unless set.</summary>
internal static class TextBackground
{
    public static void Replace(Item owner, ref Item? field, Item? value, ref bool isDefault, bool newIsDefault)
    {
        if (field == value)
            return;
        var old = field;
        field = value;
        if (old is not null)
        {
            if (isDefault)
            {
                old.Destroy();
            }
            else if (old.ParentItem == owner)
            {
                old.SetParentItem(null);
                old.Visible = false;
            }
        }
        isDefault = newIsDefault && value is not null;
        if (value is not null)
        {
            if (value.Z == 0)
                value.Z = -1;
            value.SetParentItem(owner);
            Layout(owner, value);
        }
        owner.NotifyChanged("background");
    }

    public static void Layout(Item owner, Item? background)
    {
        if (background is null || background.ParentItem != owner || background.HasAnchors)
            return;
        using var _ = DependencyTracker.Suspend();
        background.SetLayoutX(0);
        background.SetLayoutY(0);
        if (!background.HasExplicitWidth)
            background.SetLayoutWidth(owner.Width);
        if (!background.HasExplicitHeight)
            background.SetLayoutHeight(owner.Height);
    }
}

/// <summary>
/// QtQuick.Controls' <c>Label</c>: a Text with a <c>background</c>. The text draws over its children so the
/// background stays behind it.
/// </summary>
public sealed class Label : Text, IPaintOverChildren
{
    private Item? _background;
    private bool _defaultBackground;

    [Qml]
    public Item? Background
    {
        get { Track(); return _background; }
        set => TextBackground.Replace(this, ref _background, value, ref _defaultBackground, false);
    }

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        TextBackground.Layout(this, _background);
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
    }

    public void PaintOver(SKCanvas canvas, PaintContext context) => base.Paint(canvas, context);
}

/// <summary>
/// QtQuick.Controls' <c>TextField</c>: a TextInput drawn as a Windows 11 text box, with placeholder text and
/// the accent line under it while it has focus.
/// </summary>
public class TextField : TextInput, IPaintOverChildren, IHoverTarget
{
    private string _placeholderText = "";
    private readonly LineLayoutCache _placeholderMeasure = new();
    private readonly LineLayoutCache _placeholderLayout = new();
    private QColor? _placeholderColor;
    private Item? _background;
    private bool _backgroundSet, _defaultBackground;
    private bool _hovered;
    private bool _hoverEnabled = true;

    public TextField()
    {
        LeftPadding = 11;
        RightPadding = 11;
        TopPadding = 5;
        BottomPadding = 7;
        VerticalAlignment = QtEnums.AlignVCenter;
        Theme.Changed += OnThemeChanged;
    }

    protected override bool DefaultActiveFocusOnTab => true;

    private void OnThemeChanged()
    {
        if (!IsDestroyed)
            Update();
    }

    [Qml]
    public string PlaceholderText
    {
        get { Track(); return _placeholderText; }
        set
        {
            if (SetField(ref _placeholderText, value ?? ""))
            {
                Update();
                EnsureMinimumSize();
            }
        }
    }

    [Qml]
    public QColor PlaceholderTextColor
    {
        get { Track(); return _placeholderColor ?? Theme.Current.TextFillColorSecondary; }
        set { _placeholderColor = value; NotifyChanged("placeholderTextColor"); Update(); }
    }

    [Qml]
    public Item? Background
    {
        get { Track(); return _background; }
        set
        {
            _backgroundSet = true;
            TextBackground.Replace(this, ref _background, value, ref _defaultBackground, false);
        }
    }

    [Qml]
    public bool Hovered { get { Track(); return _hovered; } }

    [Qml]
    public bool HoverEnabled { get { Track(); return _hoverEnabled; } set => SetField(ref _hoverEnabled, value); }

    [Qml] public double TopInset { get; set; }

    [Qml] public double BottomInset { get; set; }

    [Qml] public double LeftInset { get; set; }

    [Qml] public double RightInset { get; set; }

    [Qml] public double ImplicitBackgroundWidth => _background?.ImplicitWidth ?? 0;

    [Qml] public double ImplicitBackgroundHeight => _background?.ImplicitHeight ?? 0;

    [Qml] public int FocusReason { get; set; }

    void IHoverTarget.HoverEnter(double x, double y, int modifiers) => SetHovered(true, InClearButton(x, y));

    void IHoverTarget.HoverMove(double x, double y, int modifiers) => SetHovered(true, InClearButton(x, y));

    void IHoverTarget.HoverLeave() => SetHovered(false, false);

    private void SetHovered(bool value, bool overClearButton)
    {
        if (!_hoverEnabled)
            value = false;
        if (_clearHovered != (value && overClearButton))
        {
            _clearHovered = value && overClearButton;
            Update();
        }
        if (SetField(ref _hovered, value, nameof(Hovered)))
            Update();
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        if (!_backgroundSet && _background is null)
        {
            var bg = new PaintedItem((c, i, _) => PaintBackground(c, i.Bounds), 160, Fluent.ControlHeight);
            bg.MarkComplete();
            TextBackground.Replace(this, ref _background, bg, ref _defaultBackground, true);
        }
        EnsureMinimumSize();
    }

    private bool Focused => ActiveFocus && !ReadOnly;

    /// <summary>
    /// The text box: a filled rounded rectangle with a strong line along the bottom, accent and 2 pixels when
    /// focused. A contrast theme draws a whole border instead, 2 pixels when focused.
    /// </summary>
    protected virtual void PaintBackground(SKCanvas canvas, SKRect r)
    {
        var t = Theme.Current;
        bool enabled = Enabled, focused = Focused;
        var fill = FluentResources.TextControlBackground(t).For(enabled, focused, _hovered);
        if (t.IsHighContrast)
        {
            Fluent.FillRound(canvas, r, Fluent.ControlCornerRadius, fill);
            Fluent.StrokeRound(canvas, r, Fluent.ControlCornerRadius, FluentResources.TextControlBorderBrush(t).For(enabled, focused, _hovered), focused ? 2 : 1);
            return;
        }
        Fluent.ElevatedRect(canvas, r, Fluent.ControlCornerRadius, fill, t.ControlStrokeColorDefault, t.ControlStrokeColorDefault);
        if (!enabled)
            return;
        canvas.Save();
        using (var clip = Fluent.Round(r, Fluent.ControlCornerRadius))
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
        var lineHeight = focused ? 2f : 1f;
        using var paint = new SKPaint { Color = FluentResources.TextControlBottomLine(t, focused).ToSk(), IsAntialias = false };
        canvas.DrawRect(SKRect.Create(r.Left, r.Bottom - lineHeight, r.Width, lineHeight), paint);
        canvas.Restore();
    }

    protected override QColor PaintColor => HasExplicitColor ? base.PaintColor : FluentResources.TextControlForeground(Theme.Current).For(Enabled, Focused, _hovered);

    // The clear button WinUI shows at the right of a focused text box that has text.

    private const double ClearButtonWidth = 30;
    private bool _clearHovered, _clearPressed, _clearPainted;

    /// <summary>Whether the clear button shows: focused, editable, single line, not a password, and with text.</summary>
    protected virtual bool ShowsClearButton => Focused && Enabled && EchoMode == 0 && Text_.Length > 0 && Width >= 3 * ClearButtonWidth;

    protected override double ReservedRight => ShowsClearButton ? ClearButtonWidth : 0;

    /// <summary>The button's face: its 30 pixel column less WinUI's 0,4,4,4 margin.</summary>
    private SKRect ClearButtonRect => SKRect.Create((float)(Width - ClearButtonWidth), 4, (float)ClearButtonWidth - 4, (float)Math.Max(0, Height - 8));

    /// <summary>Whether a point is on the button as last drawn: a press that focuses the box does not also clear it.</summary>
    private bool InClearButton(double x, double y) => _clearPainted && ShowsClearButton && ClearButtonRect.Contains((float)x, (float)y);

    private void PaintClearButton(SKCanvas canvas)
    {
        _clearPainted = ShowsClearButton;
        if (!_clearPainted)
            return;
        var t = Theme.Current;
        var r = ClearButtonRect;
        Fluent.FillRound(canvas, r, Fluent.ControlCornerRadius, FluentResources.TextControlButtonBackground(t).For(true, _clearPressed, _clearHovered));
        var glyph = FluentResources.TextControlButtonForeground(t).For(true, _clearPressed, _clearHovered);
        if (!Fluent.DrawGlyph(canvas, Fluent.Cancel, r.MidX, r.MidY, 12, glyph))
        {
            using var paint = new SKPaint { Color = glyph.ToSk(), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawLine(r.MidX - 4, r.MidY - 4, r.MidX + 4, r.MidY + 4, paint);
            canvas.DrawLine(r.MidX - 4, r.MidY + 4, r.MidX + 4, r.MidY - 4, paint);
        }
    }

    public override bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        if (!InClearButton(x, y))
            return base.PointerPress(x, y, button, buttons, modifiers);
        _clearPressed = true;
        Update();
        return true;
    }

    public override void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (!_clearPressed)
            base.PointerMove(x, y, buttons, modifiers);
    }

    public override void PointerRelease(double x, double y, int button, int buttons, int modifiers)
    {
        if (!_clearPressed)
        {
            base.PointerRelease(x, y, button, buttons, modifiers);
            return;
        }
        _clearPressed = false;
        if (InClearButton(x, y))
            SetText("", fromUser: true);
        Update();
    }

    protected override void OnActiveFocusChanged(bool focused)
    {
        base.OnActiveFocusChanged(focused);
        Update();
    }

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        TextBackground.Layout(this, _background);
    }

    protected override void OnImplicitSizeChanged()
    {
        base.OnImplicitSizeChanged();
        EnsureMinimumSize();
    }

    private bool _ensuring;

    private void EnsureMinimumSize()
    {
        if (_ensuring || !IsComplete)
            return;
        _ensuring = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            var minW = Math.Max(_background?.ImplicitWidth ?? 0, PlaceholderWidth() + LeftPadding + RightPadding);
            var minH = _background?.ImplicitHeight ?? 0;
            if (ImplicitWidth < minW)
                ImplicitWidth = minW;
            if (ImplicitHeight < minH)
                ImplicitHeight = minH;
        }
        finally
        {
            _ensuring = false;
        }
    }

    private double PlaceholderWidth()
    {
        if (_placeholderText.Length == 0)
            return 0;
        return Math.Ceiling(_placeholderMeasure.Get(_placeholderText, Font.Spec, double.NaN, TextLayout.Elide.None).Width);
    }

    /// <summary>How many placeholder layouts the field has built, to measure and to draw.</summary>
    internal int PlaceholderBuilds => _placeholderMeasure.Builds + _placeholderLayout.Builds;

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
    }

    public void PaintOver(SKCanvas canvas, PaintContext context)
    {
        if (Text_.Length == 0 && _placeholderText.Length > 0)
        {
            var layout = _placeholderLayout.Get(_placeholderText, Font.Spec, Math.Max(0, Width - LeftPadding - RightPadding), TextLayout.Elide.Right);
            var y = (VerticalAlignment & QtEnums.AlignVCenter) != 0 ? (Height - layout.Height) / 2 : TopPadding;
            canvas.Save();
            canvas.Translate((float)LeftPadding, 0);
            layout.Draw(canvas, Math.Max(0, Width - LeftPadding - RightPadding), HorizontalAlignment, Enabled ? PlaceholderTextColor : Theme.Current.TextFillColorDisabled, y, true, paint: context);
            canvas.Restore();
        }
        base.Paint(canvas, context);
        PaintClearButton(canvas);
    }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnThemeChanged;
        _placeholderMeasure.Dispose();
        _placeholderLayout.Dispose();
        base.OnDestroy();
    }
}

/// <summary>QtQuick.Controls' <c>TextArea</c>. Edits as TextField does, and accepts the multi-line properties.</summary>
public sealed class TextArea : TextField
{
    public TextArea()
    {
        VerticalAlignment = QtEnums.AlignTop;
    }

    [Qml] public int TextFormat { get; set; }

    [Qml] public int LineCount => 1;

    [Qml] public double TabStopDistance { get; set; } = 80;
}
