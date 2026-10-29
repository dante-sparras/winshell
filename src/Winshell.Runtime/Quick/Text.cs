using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>QtQuick's <c>Text</c>.</summary>
[QmlSignal("linkActivated", "link")]
[QmlSignal("linkHovered", "link")]
public class Text : Item, IPolishable
{
    private string _text = "";
    private QColor _color = Theme.Current.TextFillColorPrimary;
    private bool _colorSet;
    private int _horizontalAlignment = QtEnums.AlignLeft;
    private int _verticalAlignment = QtEnums.AlignTop;
    private int _wrapMode;
    private int _elide = (int)TextLayout.Elide.None;
    private int _maximumLineCount = int.MaxValue;
    private double _lineHeight = 1;
    private int _lineHeightMode;
    private int _textFormat = (int)TextFormatValue.AutoText;
    private int _style;
    private QColor _styleColor = QColor.Black;
    private QColor? _linkColor;
    private double _topPadding, _bottomPadding, _leftPadding, _rightPadding;
    private double _padding;
    private bool _topSet, _bottomSet, _leftSet, _rightSet;
    private TextLayout? _layout;
    private double _layoutWidth = double.NaN;
    private bool _layoutDirty = true;
    private bool _sizeDirty, _naturalDirty;
    private int _renderType;
    private int _fontSizeMode;
    private double _minimumPixelSize = 12;

    public Text()
    {
        _fontGroup = new FontGroup(this);
        Font.Changed += Relayout;
        Theme.Changed += OnThemeChanged;
    }

    [QmlEnum]
    public enum HAlignment
    {
        AlignLeft = 1,
        AlignRight = 2,
        AlignHCenter = 4,
        AlignJustify = 8,
    }

    [QmlEnum]
    public enum VAlignment
    {
        AlignTop = 0x20,
        AlignBottom = 0x40,
        AlignVCenter = 0x80,
    }

    [QmlEnum]
    public enum WrapModeValue
    {
        NoWrap = 0,
        WordWrap = 1,
        WrapAnywhere = 3,
        Wrap = 4,
        WrapAtWordBoundaryOrAnywhere = 4,
    }

    [QmlEnum]
    public enum ElideValue
    {
        ElideLeft = 0,
        ElideRight = 1,
        ElideMiddle = 2,
        ElideNone = 3,
    }

    [QmlEnum]
    public enum TextFormatValue
    {
        PlainText = 0,
        RichText = 1,
        AutoText = 2,
        MarkdownText = 3,
        StyledText = 4,
    }

    [QmlEnum]
    public enum TextStyle
    {
        Normal = 0,
        Outline = 1,
        Raised = 2,
        Sunken = 3,
    }

    [QmlEnum]
    public enum LineHeightModeValue
    {
        ProportionalHeight = 0,
        FixedHeight = 1,
    }

    [QmlEnum]
    public enum RenderTypeValue
    {
        QtRendering = 0,
        NativeRendering = 1,
        CurveRendering = 2,
    }

    [QmlEnum]
    public enum FontSizeModeValue
    {
        FixedSize = 0,
        HorizontalFit = 1,
        VerticalFit = 2,
        Fit = 3,
    }

    private void OnThemeChanged()
    {
        if (IsDestroyed)
            return;
        _fontGroup.RefreshDefaultSize();
        if (!_colorSet)
        {
            _color = Theme.Current.TextFillColorPrimary;
            NotifyChanged("color");
            Update();
        }
    }

    [Qml("text")]
    public string Text_
    {
        get { Track(nameof(Text)); return _text; }
        set => SetText(value);
    }

    private void SetText(string? value)
    {
        value ??= "";
        if (_text == value)
            return;
        _text = value;
        NotifyChanged("text");
        Relayout();
    }

    [Qml]
    public QColor Color
    {
        get { Track(); return _color; }
        set
        {
            _colorSet = true;
            if (SetField(ref _color, value))
                Update();
        }
    }

    private readonly FontGroup _fontGroup;

    /// <summary>The font. Assigning a whole font, such as <c>font: Theme.typography.body</c>, copies it.</summary>
    [Qml]
    public FontGroup Font { get => _fontGroup; set => _fontGroup.CopyFrom(value); }

    [Qml]
    public int HorizontalAlignment
    {
        get { Track(); return _horizontalAlignment; }
        set
        {
            if (SetField(ref _horizontalAlignment, value))
                Update();
        }
    }

    [Qml]
    public int VerticalAlignment
    {
        get { Track(); return _verticalAlignment; }
        set
        {
            if (SetField(ref _verticalAlignment, value))
                Update();
        }
    }

    [Qml]
    public int EffectiveHorizontalAlignment => HorizontalAlignment;

    [Qml]
    public int WrapMode
    {
        get { Track(); return _wrapMode; }
        set
        {
            if (SetField(ref _wrapMode, value))
                Relayout();
        }
    }

    [Qml]
    public int Elide
    {
        get { Track(); return _elide; }
        set
        {
            if (SetField(ref _elide, value))
                Relayout();
        }
    }

    [Qml]
    public int MaximumLineCount
    {
        get { Track(); return _maximumLineCount; }
        set
        {
            if (SetField(ref _maximumLineCount, value))
                Relayout();
        }
    }

    [Qml]
    public double LineHeight
    {
        get { Track(); return _lineHeight; }
        set
        {
            if (SetField(ref _lineHeight, value))
                Relayout();
        }
    }

    [Qml]
    public int LineHeightMode
    {
        get { Track(); return _lineHeightMode; }
        set
        {
            if (SetField(ref _lineHeightMode, value))
                Relayout();
        }
    }

    [Qml]
    public int TextFormat
    {
        get { Track(); return _textFormat; }
        set
        {
            if (SetField(ref _textFormat, value))
                Relayout();
        }
    }

    [Qml]
    public int Style
    {
        get { Track(); return _style; }
        set
        {
            if (SetField(ref _style, value))
                Update();
        }
    }

    [Qml]
    public QColor StyleColor
    {
        get { Track(); return _styleColor; }
        set
        {
            if (SetField(ref _styleColor, value))
                Update();
        }
    }

    [Qml]
    public QColor LinkColor
    {
        get { Track(); return _linkColor ?? Theme.Current.AccentTextFillColorPrimary; }
        set
        {
            _linkColor = value;
            NotifyChanged("linkColor");
            Relayout();
        }
    }

    [Qml]
    public int RenderType
    {
        get { Track(); return _renderType; }
        set => SetField(ref _renderType, value);
    }

    [Qml]
    public int FontSizeMode
    {
        get { Track(); return _fontSizeMode; }
        set
        {
            if (SetField(ref _fontSizeMode, value))
                Relayout();
        }
    }

    [Qml]
    public double MinimumPixelSize
    {
        get { Track(); return _minimumPixelSize; }
        set => SetField(ref _minimumPixelSize, value);
    }

    [Qml]
    public double MinimumPointSize { get; set; }

    [Qml]
    public double Padding
    {
        get { Track(); return _padding; }
        set
        {
            if (!SetField(ref _padding, value))
                return;
            if (!_topSet) _topPadding = value;
            if (!_bottomSet) _bottomPadding = value;
            if (!_leftSet) _leftPadding = value;
            if (!_rightSet) _rightPadding = value;
            Relayout();
        }
    }

    [Qml]
    public double TopPadding { get { Track(); return _topPadding; } set { _topSet = true; _topPadding = value; NotifyChanged("topPadding"); Relayout(); } }

    [Qml]
    public double BottomPadding { get { Track(); return _bottomPadding; } set { _bottomSet = true; _bottomPadding = value; NotifyChanged("bottomPadding"); Relayout(); } }

    [Qml]
    public double LeftPadding { get { Track(); return _leftPadding; } set { _leftSet = true; _leftPadding = value; NotifyChanged("leftPadding"); Relayout(); } }

    [Qml]
    public double RightPadding { get { Track(); return _rightPadding; } set { _rightSet = true; _rightPadding = value; NotifyChanged("rightPadding"); Relayout(); } }

    [Qml]
    public double ContentWidth
    {
        get { Track(); SettleSize(); EnsureLayout(); return _layout?.Width ?? 0; }
    }

    [Qml]
    public double ContentHeight
    {
        get { Track(); SettleSize(); EnsureLayout(); return _layout?.Height ?? 0; }
    }

    [Qml]
    public int LineCount
    {
        get { Track(); SettleSize(); EnsureLayout(); return _layout?.Lines.Count ?? 0; }
    }

    [Qml]
    public bool Truncated
    {
        get { Track(); SettleSize(); EnsureLayout(); return _layout?.Truncated ?? false; }
    }

    [Qml]
    public string HoveredLink => "";

    [Qml]
    public string LinkAt(double x, double y) => "";

    [Qml]
    public void ForceLayout() => Settle();

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        // Moving the text doesn't move its lines. Only a new width to wrap or elide to does.
        if (!_sizeDirty && !LayoutWidth.Equals(_layoutWidth))
            RequestSize();
    }

    private bool HasExplicitOrAnchoredWidth => HasExplicitWidth || (HasAnchors && Anchors.ControlsWidth) || LayoutControlsWidth;

    /// <summary>Set by a layout that gives this item its width, so the text wraps to it.</summary>
    public bool LayoutControlsWidth { get; set; }

    /// <summary>How many DirectWrite layouts this text has built.</summary>
    internal int LayoutBuilds { get; private set; }

    // The width the text wraps or elides to. NaN when it has none, or when it doesn't wrap or elide, which
    // lays it out the same at any width.
    private double LayoutWidth => HasExplicitOrAnchoredWidth && (_wrapMode != 0 || _elide != (int)TextLayout.Elide.None)
        ? Width - _leftPadding - _rightPadding
        : double.NaN;

    // A change marks the text and measures it in the polish pass, so several changes in one turn measure it
    // once. Reading a size before then, outside a binding, measures it at once.
    private void Relayout()
    {
        _layoutDirty = true;
        _naturalDirty = true;
        RequestSize();
    }

    private void RequestSize()
    {
        _sizeDirty = true;
        PolishQueue.Request(this, Engine?.Loop);
        Update();
    }

    void IPolishable.UpdatePolish() => Settle();

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        // Measure once every binding has set the text, so onCompleted sees the size.
        Settle();
    }

    protected override void SettleSize()
    {
        // A binding that reads the size gets the new one when the polish pass notifies it. Measuring here
        // would notify the binding while it is still evaluating, which reads as a binding loop.
        if (!DependencyTracker.IsTracking)
            Settle();
    }

    private void Settle()
    {
        if (!_sizeDirty)
            return;
        _sizeDirty = false;
        using var _ = DependencyTracker.Suspend();
        if (_naturalDirty)
        {
            _naturalDirty = false;
            ImplicitWidth = Math.Ceiling(NaturalWidth()) + _leftPadding + _rightPadding;
        }
        EnsureLayout();
        ImplicitHeight = Math.Ceiling(_layout!.Height) + _topPadding + _bottomPadding;
        NotifyChanged("contentWidth");
        NotifyChanged("contentHeight");
        NotifyChanged("lineCount");
        NotifyChanged("truncated");
    }

    // The implicit width is the width of the text without wrapping, like Qt's.
    private double NaturalWidth()
    {
        if (_wrapMode == 0 && _maximumLineCount == int.MaxValue && double.IsNaN(LayoutWidth))
        {
            // The drawn layout is this one.
            EnsureLayout();
            return _layout!.Width;
        }
        using var natural = TextLayout.Build(Spans(), double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, LineHeightValue, ProportionalLineHeight);
        LayoutBuilds++;
        return natural.Width;
    }

    private double LineHeightValue => _lineHeight;

    private bool ProportionalLineHeight => _lineHeightMode == (int)LineHeightModeValue.ProportionalHeight;

    private List<TextSpan> Spans()
    {
        var spec = Font.Spec;
        var rich = _textFormat switch
        {
            (int)TextFormatValue.PlainText => false,
            (int)TextFormatValue.RichText or (int)TextFormatValue.StyledText => true,
            (int)TextFormatValue.MarkdownText => false,
            _ => TextLayout.MightBeRichText(_text),
        };
        if (rich)
            return TextLayout.ParseRichText(_text, spec, _linkColor ?? Theme.Current.AccentTextFillColorPrimary);
        return [new TextSpan(_text, spec)];
    }

    private void EnsureLayout()
    {
        using var _ = DependencyTracker.Suspend();
        var available = LayoutWidth;
        // Equals, not ==, so a NaN width matches itself.
        if (!_layoutDirty && _layout is not null && available.Equals(_layoutWidth))
            return;
        _layoutDirty = false;
        _layoutWidth = available;
        var wrap = (TextLayout.Wrap)(_wrapMode == 2 ? 1 : _wrapMode);
        var elide = (TextLayout.Elide)_elide;
        var maxLines = _maximumLineCount == int.MaxValue ? 0 : _maximumLineCount;
        _layout?.Dispose();
        _layout = TextLayout.Build(Spans(), available, wrap, elide, maxLines, LineHeightValue, ProportionalLineHeight);
        LayoutBuilds++;
    }

    public TextLayout? CurrentLayout
    {
        get
        {
            SettleSize();
            EnsureLayout();
            return _layout;
        }
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        SettleSize();
        EnsureLayout();
        if (_layout is null || _text.Length == 0)
            return;
        var boxWidth = Width - _leftPadding - _rightPadding;
        var boxHeight = Height - _topPadding - _bottomPadding;
        var offsetY = _topPadding;
        if ((_verticalAlignment & QtEnums.AlignBottom) != 0)
            offsetY += boxHeight - _layout.Height;
        else if ((_verticalAlignment & QtEnums.AlignVCenter) != 0)
            offsetY += (boxHeight - _layout.Height) / 2;
        canvas.Save();
        canvas.Translate((float)_leftPadding, 0);
        _layout.Draw(canvas, boxWidth, _horizontalAlignment, _color, offsetY, Antialiasing, paint: context, textStyle: _style, styleColor: _styleColor);
        canvas.Restore();
    }

    protected override void OnDestroy()
    {
        _layout?.Dispose();
        _layout = null;
        Theme.Changed -= OnThemeChanged;
        base.OnDestroy();
    }
}
