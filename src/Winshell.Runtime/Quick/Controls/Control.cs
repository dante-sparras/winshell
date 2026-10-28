using SkiaSharp;

namespace Winshell.Runtime.Quick.Controls;

/// <summary>
/// QtQuick.Controls' <c>Control</c>: an item with a <c>background</c> and a <c>contentItem</c>, padding between
/// them, and hover. The default visuals are what the two properties hold when QML does not set them.
/// </summary>
public class Control : Item, IHoverTarget
{
    private double _padding;
    private double? _horizontalPadding, _verticalPadding, _leftPadding, _rightPadding, _topPadding, _bottomPadding;
    private double _topInset, _bottomInset, _leftInset, _rightInset;
    private double _spacing;
    private bool _hovered;
    private bool _hoverEnabled = true;
    private Item? _background, _contentItem;
    private bool _backgroundSet, _contentItemSet;
    private bool _defaultBackground, _defaultContent;
    private PropertyWatch? _backgroundWatch, _contentWatch;
    private double? _autoImplicitWidth, _autoImplicitHeight;
    private bool _userImplicitWidth, _userImplicitHeight;
    private bool _settingImplicit;
    private int? _focusPolicy;
    private bool _wheelEnabled;
    private bool _inLayout;

    public Control()
    {
        _fontGroup = new FontGroup(this);
        Font.Changed += () =>
        {
            NotifyChanged("font");
            OnFontChanged();
        };
        Theme.Changed += OnThemeChangedHandler;
    }

    /// <summary>The last pointer position over the control, in its own coordinates.</summary>
    public QPoint LastPointer { get; protected set; }

    // Padding and insets

    [Qml]
    public double Padding
    {
        get { Track(); return _padding; }
        set
        {
            if (SetField(ref _padding, value))
                PaddingChanged();
        }
    }

    [Qml]
    public double HorizontalPadding
    {
        get { Track(); return _horizontalPadding ?? _padding; }
        set { _horizontalPadding = value; PaddingChanged(); }
    }

    [Qml]
    public double VerticalPadding
    {
        get { Track(); return _verticalPadding ?? _padding; }
        set { _verticalPadding = value; PaddingChanged(); }
    }

    [Qml]
    public double LeftPadding
    {
        get { Track(); return _leftPadding ?? _horizontalPadding ?? _padding; }
        set { _leftPadding = value; PaddingChanged(); }
    }

    [Qml]
    public double RightPadding
    {
        get { Track(); return _rightPadding ?? _horizontalPadding ?? _padding; }
        set { _rightPadding = value; PaddingChanged(); }
    }

    [Qml]
    public double TopPadding
    {
        get { Track(); return _topPadding ?? _verticalPadding ?? _padding; }
        set { _topPadding = value; PaddingChanged(); }
    }

    [Qml]
    public double BottomPadding
    {
        get { Track(); return _bottomPadding ?? _verticalPadding ?? _padding; }
        set { _bottomPadding = value; PaddingChanged(); }
    }

    /// <summary>Sets default padding from C#, without counting as set from QML.</summary>
    protected void SetDefaultPadding(double left, double top, double right, double bottom)
    {
        _leftPadding = left;
        _topPadding = top;
        _rightPadding = right;
        _bottomPadding = bottom;
        PaddingChanged();
    }

    private void PaddingChanged()
    {
        foreach (var name in (string[])["padding", "horizontalPadding", "verticalPadding", "leftPadding", "rightPadding", "topPadding", "bottomPadding", "availableWidth", "availableHeight"])
            NotifyChanged(name);
        LayoutChildren();
        UpdateImplicitSize();
    }

    [Qml]
    public double TopInset { get { Track(); return _topInset; } set { if (SetField(ref _topInset, value)) { LayoutChildren(); UpdateImplicitSize(); } } }

    [Qml]
    public double BottomInset { get { Track(); return _bottomInset; } set { if (SetField(ref _bottomInset, value)) { LayoutChildren(); UpdateImplicitSize(); } } }

    [Qml]
    public double LeftInset { get { Track(); return _leftInset; } set { if (SetField(ref _leftInset, value)) { LayoutChildren(); UpdateImplicitSize(); } } }

    [Qml]
    public double RightInset { get { Track(); return _rightInset; } set { if (SetField(ref _rightInset, value)) { LayoutChildren(); UpdateImplicitSize(); } } }

    [Qml]
    public double Spacing
    {
        get { Track(); return _spacing; }
        set
        {
            if (SetField(ref _spacing, value))
            {
                OnSpacingChanged();
                LayoutChildren();
                UpdateImplicitSize();
            }
        }
    }

    protected virtual void OnSpacingChanged()
    {
    }

    [Qml]
    public double AvailableWidth
    {
        get { Track("width"); TrackQml("availableWidth"); using (DependencyTracker.Suspend()) return Math.Max(0, Width - LeftPadding - RightPadding); }
    }

    [Qml]
    public double AvailableHeight
    {
        get { Track("height"); TrackQml("availableHeight"); using (DependencyTracker.Suspend()) return Math.Max(0, Height - TopPadding - BottomPadding); }
    }

    [Qml]
    public virtual double ImplicitContentWidth
    {
        get { Track(); return _contentItem?.ImplicitWidth ?? 0; }
    }

    [Qml]
    public virtual double ImplicitContentHeight
    {
        get { Track(); return _contentItem?.ImplicitHeight ?? 0; }
    }

    [Qml]
    public double ImplicitBackgroundWidth
    {
        get { Track(); return _background?.ImplicitWidth ?? 0; }
    }

    [Qml]
    public double ImplicitBackgroundHeight
    {
        get { Track(); return _background?.ImplicitHeight ?? 0; }
    }

    // Visual parts

    [Qml]
    public Item? Background
    {
        get { Track(); return _background; }
        set
        {
            _backgroundSet = true;
            SetBackground(value, isDefault: false);
        }
    }

    [Qml]
    public Item? ContentItem
    {
        get { Track(); return _contentItem; }
        set
        {
            _contentItemSet = true;
            SetContentItem(value, isDefault: false);
        }
    }

    public bool HasDefaultBackground => _defaultBackground;

    public bool HasDefaultContentItem => _defaultContent;

    private void SetBackground(Item? value, bool isDefault)
    {
        if (_background == value)
            return;
        var old = _background;
        _backgroundWatch?.Dispose();
        _backgroundWatch = null;
        _background = value;
        RetirePart(old, _defaultBackground);
        _defaultBackground = isDefault && value is not null;
        if (value is not null)
        {
            if (value.Z == 0)
                value.Z = -1;
            value.SetParentItem(this);
            _backgroundWatch = new PropertyWatch(value, OnBackgroundImplicitChanged, "implicitWidth", "implicitHeight");
        }
        NotifyChanged("background");
        NotifyChanged("implicitBackgroundWidth");
        NotifyChanged("implicitBackgroundHeight");
        LayoutChildren();
        UpdateImplicitSize();
    }

    private void SetContentItem(Item? value, bool isDefault)
    {
        if (_contentItem == value)
            return;
        var old = _contentItem;
        var wasDefault = _defaultContent;
        _contentWatch?.Dispose();
        _contentWatch = null;
        _contentItem = value;
        _defaultContent = isDefault && value is not null;
        if (value is not null)
        {
            value.SetParentItem(this);
            _contentWatch = new PropertyWatch(value, OnContentImplicitChanged, "implicitWidth", "implicitHeight");
        }
        OnContentItemChanged(old, value);
        RetirePart(old, wasDefault);
        NotifyChanged("contentItem");
        NotifyChanged("implicitContentWidth");
        NotifyChanged("implicitContentHeight");
        LayoutChildren();
        UpdateImplicitSize();
    }

    protected virtual void OnContentItemChanged(Item? oldItem, Item? newItem)
    {
    }

    /// <summary>Takes a replaced part out of the control; a default part is destroyed.</summary>
    protected void RetirePart(Item? old, bool wasDefault)
    {
        if (old is null)
            return;
        if (wasDefault)
        {
            old.Destroy();
        }
        else if (old.ParentItem == this)
        {
            old.SetParentItem(null);
            old.Visible = false;
        }
    }

    private void OnBackgroundImplicitChanged()
    {
        NotifyChanged("implicitBackgroundWidth");
        NotifyChanged("implicitBackgroundHeight");
        LayoutChildren();
        UpdateImplicitSize();
    }

    private void OnContentImplicitChanged()
    {
        NotifyChanged("implicitContentWidth");
        NotifyChanged("implicitContentHeight");
        UpdateImplicitSize();
    }

    /// <summary>The background used when QML sets none.</summary>
    protected virtual Item? CreateDefaultBackground() => null;

    /// <summary>The content item used when QML sets none.</summary>
    protected virtual Item? CreateDefaultContentItem() => null;

    /// <summary>Creates the default parts that are still unset. Called when the control is complete.</summary>
    protected virtual void CreateDefaults()
    {
        if (!_backgroundSet && _background is null && CreateDefaultBackground() is { } background)
            SetBackground(background, isDefault: true);
        if (!_contentItemSet && _contentItem is null && CreateDefaultContentItem() is { } content)
            SetContentItem(content, isDefault: true);
    }

    /// <summary>Sets the default content item now, before the control is complete, as panes need for their children.</summary>
    protected void InitContentItem(Item item) => SetContentItem(item, isDefault: true);

    /// <summary>Makes an internal part complete, as the engine does for objects made from QML.</summary>
    protected static T Part<T>(T item) where T : Item
    {
        if (!item.IsComplete)
            item.MarkComplete();
        return item;
    }

    // Layout

    /// <summary>Places the background and the content item for the current size and padding.</summary>
    public void LayoutChildren()
    {
        if (_inLayout || IsDestroyed)
            return;
        _inLayout = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            if (_background is { } bg && bg.ParentItem == this && !bg.HasAnchors)
            {
                bg.SetLayoutX(_leftInset);
                bg.SetLayoutY(_topInset);
                if (!bg.HasExplicitWidth)
                    bg.SetLayoutWidth(Math.Max(0, Width - _leftInset - _rightInset));
                if (!bg.HasExplicitHeight)
                    bg.SetLayoutHeight(Math.Max(0, Height - _topInset - _bottomInset));
            }
            if (_contentItem is { } content && content.ParentItem == this && !content.HasAnchors)
            {
                content.SetLayoutX(LeftPadding);
                content.SetLayoutY(TopPadding);
                if (!content.HasExplicitWidth)
                    content.SetLayoutWidth(AvailableWidth);
                if (!content.HasExplicitHeight)
                    content.SetLayoutHeight(AvailableHeight);
            }
            LayoutParts();
        }
        finally
        {
            _inLayout = false;
        }
    }

    /// <summary>Places the other parts of a control, such as an indicator or a handle.</summary>
    protected virtual void LayoutParts()
    {
    }

    protected override void OnGeometryChanged()
    {
        base.OnGeometryChanged();
        LayoutChildren();
    }

    /// <summary>The size the control wants, from its parts and padding.</summary>
    protected virtual (double Width, double Height) ComputeImplicitSize()
    {
        using var _ = DependencyTracker.Suspend();
        var w = Math.Max(ImplicitBackgroundWidth + _leftInset + _rightInset, ImplicitContentWidth + LeftPadding + RightPadding);
        var h = Math.Max(ImplicitBackgroundHeight + _topInset + _bottomInset, ImplicitContentHeight + TopPadding + BottomPadding);
        return (w, h);
    }

    /// <summary>Recomputes the implicit size, unless QML set it.</summary>
    public void UpdateImplicitSize()
    {
        if (!IsComplete || IsDestroyed || _settingImplicit)
            return;
        var (w, h) = ComputeImplicitSize();
        _settingImplicit = true;
        try
        {
            using var _ = DependencyTracker.Suspend();
            if (!_userImplicitWidth)
            {
                if (GetBinding("implicitWidth") is not null || (_autoImplicitWidth is { } aw ? ImplicitWidth != aw : ImplicitWidth != 0))
                {
                    _userImplicitWidth = true;
                }
                else
                {
                    _autoImplicitWidth = w;
                    ImplicitWidth = w;
                }
            }
            if (!_userImplicitHeight)
            {
                if (GetBinding("implicitHeight") is not null || (_autoImplicitHeight is { } ah ? ImplicitHeight != ah : ImplicitHeight != 0))
                {
                    _userImplicitHeight = true;
                }
                else
                {
                    _autoImplicitHeight = h;
                    ImplicitHeight = h;
                }
            }
        }
        finally
        {
            _settingImplicit = false;
        }
    }

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        CreateDefaults();
        OnFontChanged();
        LayoutChildren();
        UpdateImplicitSize();
    }

    // Hover and state

    [Qml]
    public bool Hovered
    {
        get { Track(); return _hovered; }
    }

    [Qml]
    public bool HoverEnabled
    {
        get { Track(); return _hoverEnabled; }
        set
        {
            if (SetField(ref _hoverEnabled, value) && !value)
                SetHovered(false);
        }
    }

    protected void SetHovered(bool value)
    {
        if (SetField(ref _hovered, value, nameof(Hovered)))
            StateChanged();
    }

    public virtual bool AcceptsHover => _hoverEnabled && Enabled && Visible;

    int IHoverTarget.CursorShape => CursorShapeValue;

    protected virtual int CursorShapeValue => -1;

    public virtual void HoverEnter(double x, double y, int modifiers)
    {
        LastPointer = new QPoint(x, y);
        SetHovered(true);
    }

    public virtual void HoverMove(double x, double y, int modifiers)
    {
        LastPointer = new QPoint(x, y);
        if (!_hovered)
            SetHovered(true);
    }

    public virtual void HoverLeave() => SetHovered(false);

    /// <summary>Called when something the default visuals show changed: hover, press, check, enabled.</summary>
    protected virtual void StateChanged() => Update();

    private readonly FontGroup _fontGroup;

    /// <summary>The font. Assigning a whole font, such as <c>font: WindowsTheme.typography.body</c>, copies it.</summary>
    [Qml]
    public FontGroup Font { get => _fontGroup; set => _fontGroup.CopyFrom(value); }

    /// <summary>Called when the font changes, so the default content can follow it.</summary>
    protected virtual void OnFontChanged()
    {
    }

    /// <summary>Copies the font into the font of a text item.</summary>
    protected static void CopyFont(FontGroup from, FontGroup to)
    {
        using var _ = DependencyTracker.Suspend();
        to.Family = from.Family;
        to.PixelSize = from.PixelSize;
        to.Weight = from.Weight;
        to.Italic = from.Italic;
        to.Underline = from.Underline;
        to.Strikeout = from.Strikeout;
        to.LetterSpacing = from.LetterSpacing;
        to.Capitalization = from.Capitalization;
    }

    private void OnThemeChangedHandler()
    {
        if (IsDestroyed)
            return;
        OnThemeChanged();
        Update();
    }

    protected virtual void OnThemeChanged()
    {
    }

    /// <summary>How the control takes focus: <c>Qt.TabFocus</c>, <c>Qt.ClickFocus</c>, both or neither.</summary>
    [Qml]
    public int FocusPolicy
    {
        get { Track(); return _focusPolicy ?? DefaultFocusPolicy; }
        set
        {
            if (_focusPolicy == value)
                return;
            _focusPolicy = value;
            NotifyChanged("focusPolicy");
            NotifyChanged("activeFocusOnTab");
        }
    }

    /// <summary>The focus policy when QML sets none. Interactive controls take <c>Qt.StrongFocus</c>.</summary>
    protected virtual int DefaultFocusPolicy => 0;

    protected const int StrongFocus = 11;

    protected override bool DefaultActiveFocusOnTab => (FocusPolicy & 1) != 0;

    [Qml]
    public int FocusReason { get; set; }

    /// <summary>Whether the control has focus that came from the keyboard, so it shows a focus rectangle.</summary>
    [Qml]
    public bool VisualFocus
    {
        get { Track(); return ActiveFocus && Host is Scene { FocusVisible: true }; }
    }

    internal void VisualFocusChanged() => NotifyChanged("visualFocus");

    /// <summary>The corner radius of the control's shape, which its focus rectangle follows.</summary>
    public virtual float FocusRadius => Fluent.ControlCornerRadius;

    protected override void OnActiveFocusChanged(bool focused)
    {
        base.OnActiveFocusChanged(focused);
        VisualFocusChanged();
    }

    [Qml]
    public bool WheelEnabled { get { Track(); return _wheelEnabled; } set => SetField(ref _wheelEnabled, value); }

    [Qml]
    public bool Mirrored => false;

    [Qml]
    public object? Locale { get; set; }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnThemeChangedHandler;
        _backgroundWatch?.Dispose();
        _contentWatch?.Dispose();
        base.OnDestroy();
    }
}

/// <summary>A button's <c>icon</c> group.</summary>
public sealed class IconGroup : QObject
{
    private string _name = "";
    private string _source = "";
    private double _width = 16, _height = 16;
    private QColor _color = QColor.Invalid;
    private bool _cache = true;

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    [Qml] public string Name { get { Track(); return _name; } set { if (SetField(ref _name, value ?? "")) Raise(); } }

    [Qml] public string Source { get { Track(); return _source; } set { if (SetField(ref _source, value ?? "")) Raise(); } }

    [Qml] public double Width { get { Track(); return _width; } set { if (SetField(ref _width, value)) Raise(); } }

    [Qml] public double Height { get { Track(); return _height; } set { if (SetField(ref _height, value)) Raise(); } }

    /// <summary>The tint. Unset, the control's text color is used; transparent leaves the icon as it is.</summary>
    [Qml] public QColor Color { get { Track(); return _color; } set { if (SetField(ref _color, value)) Raise(); } }

    [Qml] public bool Cache { get { Track(); return _cache; } set => SetField(ref _cache, value); }

    [Qml] public bool Mask { get; set; }

    public bool IsEmpty => _name.Length == 0 && _source.Length == 0;

    /// <summary>The image source to load: the source, or the theme icon the name refers to.</summary>
    public string EffectiveSource => _source.Length > 0 ? _source : _name.Length > 0 ? "image://icon/" + _name : "";
}

/// <summary>
/// The default content of a button: an icon and a text side by side, under each other, or alone, as the
/// button's <c>display</c> says.
/// </summary>
public sealed class IconLabel : Item
{
    private string _text = "";
    private FontSpec _font = FontSpec.Default;
    private string _iconSource = "";
    private double _iconWidth = 16, _iconHeight = 16;
    private int _display = (int)AbstractButton.DisplayValue.TextBesideIcon;
    private double _spacing = 8;
    private int _alignment = QtEnums.AlignCenter;
    private double _leftPadding, _rightPadding;
    private SKImage? _icon;
    private (string Source, int W, int H) _iconKey;
    private TextLayout? _layout;

    /// <summary>The text color, read each time the label draws.</summary>
    public Func<QColor> ColorSource { get; set; } = () => Theme.Current.TextFillColorPrimary;

    /// <summary>The icon tint, read each time the label draws. Transparent draws the icon untinted.</summary>
    public Func<QColor> IconColorSource { get; set; } = () => QColor.Transparent;

    public string? BaseDirectory { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == (value ?? ""))
                return;
            _text = value ?? "";
            Relayout();
        }
    }

    public FontSpec Font
    {
        get => _font;
        set
        {
            if (_font == value)
                return;
            _font = value;
            Relayout();
        }
    }

    public void SetIcon(string source, double width, double height)
    {
        if (_iconSource == source && _iconWidth == width && _iconHeight == height)
            return;
        _iconSource = source;
        _iconWidth = width;
        _iconHeight = height;
        _icon = null;
        Relayout();
    }

    public int Display { get => _display; set { if (_display != value) { _display = value; Relayout(); } } }

    public double Spacing { get => _spacing; set { if (_spacing != value) { _spacing = value; Relayout(); } } }

    public int Alignment { get => _alignment; set { _alignment = value; Update(); } }

    public double LeftPadding { get => _leftPadding; set { if (_leftPadding != value) { _leftPadding = value; Relayout(); } } }

    public double RightPadding { get => _rightPadding; set { if (_rightPadding != value) { _rightPadding = value; Relayout(); } } }

    private bool ShowsIcon => _iconSource.Length > 0 && _display != (int)AbstractButton.DisplayValue.TextOnly;

    private bool ShowsText => _text.Length > 0 && _display != (int)AbstractButton.DisplayValue.IconOnly;

    private void Relayout()
    {
        _layout?.Dispose();
        _layout = _text.Length == 0 ? null : TextLayout.Build([new TextSpan(_text, _font)], double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, 1, true);
        double w = 0, h = 0;
        var textW = ShowsText ? Math.Ceiling(_layout!.Width) : 0;
        var textH = ShowsText ? Math.Ceiling(_layout!.Height) : 0;
        var iconW = ShowsIcon ? _iconWidth : 0;
        var iconH = ShowsIcon ? _iconHeight : 0;
        if (_display == (int)AbstractButton.DisplayValue.TextUnderIcon)
        {
            w = Math.Max(textW, iconW);
            h = textH + iconH + (ShowsIcon && ShowsText ? _spacing : 0);
        }
        else
        {
            w = textW + iconW + (ShowsIcon && ShowsText ? _spacing : 0);
            h = Math.Max(textH, iconH);
        }
        if (!ShowsText && !ShowsIcon)
        {
            using var probe = TextLayout.Build([new TextSpan("Ag", _font)], double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, 1, true);
            h = Math.Ceiling(probe.Height);
        }
        ImplicitWidth = w + _leftPadding + _rightPadding;
        ImplicitHeight = h;
        Update();
    }

    private SKImage? Icon(double dpr)
    {
        if (_iconSource.Length == 0)
            return null;
        var key = (_iconSource, (int)Math.Ceiling(_iconWidth * dpr), (int)Math.Ceiling(_iconHeight * dpr));
        if (_icon is null || _iconKey != key)
        {
            _iconKey = key;
            _icon = ImageLoader.IsRemote(_iconSource) ? null : ImageLoader.Load(_iconSource, BaseDirectory, key.Item2, key.Item3);
        }
        return _icon;
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        var showIcon = ShowsIcon;
        var showText = ShowsText && _layout is not null;
        if (!showIcon && !showText)
            return;
        var boxX = _leftPadding;
        var boxW = Math.Max(0, Width - _leftPadding - _rightPadding);
        var under = _display == (int)AbstractButton.DisplayValue.TextUnderIcon;
        var iconW = showIcon ? _iconWidth : 0;
        var iconH = showIcon ? _iconHeight : 0;
        var gap = showIcon && showText ? _spacing : 0;
        var textAvail = under ? boxW : Math.Max(0, boxW - iconW - gap);
        var layout = _layout;
        TextLayout? elided = null;
        if (showText && layout!.Width > textAvail + 0.5)
        {
            elided = TextLayout.Build([new TextSpan(_text, _font)], textAvail, TextLayout.Wrap.NoWrap, TextLayout.Elide.Right, 1, 1, true);
            layout = elided;
        }
        var textW = showText ? Math.Min(layout!.Width, textAvail) : 0;
        var textH = showText ? layout!.Height : 0;
        var contentW = under ? Math.Max(iconW, textW) : iconW + gap + textW;
        var contentH = under ? iconH + gap + textH : Math.Max(iconH, textH);
        var x = (_alignment & QtEnums.AlignLeft) != 0 ? boxX : (_alignment & QtEnums.AlignRight) != 0 ? boxX + boxW - contentW : boxX + (boxW - contentW) / 2;
        var y = (_alignment & QtEnums.AlignTop) != 0 ? 0 : (_alignment & QtEnums.AlignBottom) != 0 ? Height - contentH : (Height - contentH) / 2;
        x = Math.Round(x);
        y = Math.Round(y);
        if (showIcon && Icon(context.DevicePixelRatio) is { } icon)
        {
            var ix = under ? x + (contentW - iconW) / 2 : x;
            var iy = under ? y : y + (contentH - iconH) / 2;
            using var paint = new SKPaint { IsAntialias = true };
            var tint = IconColorSource();
            if (tint.IsValid && tint.A > 0)
                paint.ColorFilter = SKColorFilter.CreateBlendMode(tint.ToSk(), SKBlendMode.SrcIn);
            canvas.DrawImage(icon, SKRect.Create((float)ix, (float)iy, (float)iconW, (float)iconH), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
        if (showText)
        {
            var tx = under ? x + (contentW - textW) / 2 : x + iconW + gap;
            var ty = under ? y + iconH + gap : y + (contentH - textH) / 2;
            canvas.Save();
            canvas.Translate((float)tx, 0);
            layout!.Draw(canvas, textW, QtEnums.AlignLeft, ColorSource(), ty, true, paint: context);
            canvas.Restore();
        }
        elided?.Dispose();
    }

    protected override void OnDestroy()
    {
        _layout?.Dispose();
        _layout = null;
        base.OnDestroy();
    }
}

/// <summary>An item that draws itself with a function, for the default visuals of controls.</summary>
public sealed class PaintedItem : Item
{
    private readonly Action<SKCanvas, PaintedItem, PaintContext> _paint;

    public PaintedItem(Action<SKCanvas, PaintedItem, PaintContext> paint, double implicitWidth = 0, double implicitHeight = 0)
    {
        _paint = paint;
        ImplicitWidth = implicitWidth;
        ImplicitHeight = implicitHeight;
    }

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        if (Width > 0 && Height > 0)
            _paint(canvas, this, context);
    }

    public SKRect Bounds => SKRect.Create(0, 0, (float)Width, (float)Height);
}
