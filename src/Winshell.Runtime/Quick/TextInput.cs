using SkiaSharp;

namespace Winshell.Runtime.Quick;

/// <summary>QtQuick's <c>TextInput</c>: one line of editable text.</summary>
[QmlSignal("accepted")]
[QmlSignal("editingFinished")]
[QmlSignal("textEdited")]
public class TextInput : Item, IKeyTarget, ITextTarget, IFocusOnPress, IPointerTarget, IHoverTarget
{
    private string _text = "";
    private QColor _color = Theme.Current.TextFillColorPrimary;
    private bool _colorSet;
    private QColor? _selectionColor;
    private QColor? _selectedTextColor;
    private int _cursorPosition;
    private int _selectionStart;
    private int _selectionEnd;
    private int _echoMode;
    private bool _readOnly;
    private int _maximumLength = 32767;
    private int _horizontalAlignment = QtEnums.AlignLeft;
    private int _verticalAlignment = QtEnums.AlignTop;
    private bool _cursorVisible;
    private bool _selectByMouse = true;
    private string _passwordCharacter = "●";
    private double _scrollX;
    private IDisposable? _blink;
    private bool _blinkOn = true;
    private long _blinkElapsed;
    private readonly List<(string Text, int Cursor)> _undo = [];
    private double _padding, _leftPadding, _rightPadding, _topPadding, _bottomPadding;
    private int _dragAnchor = -1;
    private string _composition = "";
    private TextLayout? _visual;
    private bool _visualDirty = true;
    private (FontSpec Font, double Height)? _lineHeight;

    public TextInput()
    {
        _fontGroup = new FontGroup(this);
        Font.Changed += UpdateImplicit;
        Theme.Changed += OnTheme;
    }

    [QmlEnum]
    public enum EchoModeValue
    {
        Normal = 0,
        NoEcho = 1,
        Password = 2,
        PasswordEchoOnEdit = 3,
    }

    private void OnTheme()
    {
        if (!_colorSet && !IsDestroyed)
        {
            _color = Theme.Current.TextFillColorPrimary;
            NotifyChanged("color");
        }
        if (_selectionColor is null)
            NotifyChanged("selectionColor");
        if (_selectedTextColor is null)
            NotifyChanged("selectedTextColor");
        Update();
    }

    [Qml("text")]
    public string Text_
    {
        get { Track("text"); return _text; }
        set => SetText(value ?? "", fromUser: false);
    }

    protected void SetText(string value, bool fromUser)
    {
        if (value.Length > _maximumLength)
            value = TextClusters.Limit(value, _maximumLength);
        if (_text == value)
            return;
        _composition = "";
        if (fromUser)
        {
            _undo.Add((_text, _cursorPosition));
            if (_undo.Count > 100)
                _undo.RemoveAt(0);
        }
        _text = value;
        NotifyChanged("text");
        NotifyChanged("displayText");
        NotifyChanged("length");
        if (_cursorPosition > _text.Length)
            CursorPosition = _text.Length;
        ClampSelection();
        UpdateImplicit();
        if (fromUser)
            Emit("textEdited");
    }

    [Qml]
    public string DisplayText
    {
        get
        {
            Track("text");
            return _echoMode switch
            {
                1 => "",
                2 or 3 => Mask(_text),
                _ => _text,
            };
        }
    }

    [Qml]
    public int Length { get { Track("text"); return _text.Length; } }

    /// <summary>The IME preedit. It is drawn, and it is not part of <see cref="Text_"/> until it commits.</summary>
    internal string CompositionText => _composition;

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

    /// <summary>Whether QML set <c>color</c>, so a control's default colors leave it alone.</summary>
    protected bool HasExplicitColor => _colorSet;

    /// <summary>The color the text and caret are drawn in. A control can give its default text a state's color.</summary>
    protected virtual QColor PaintColor => _color;

    /// <summary>Room at the right that the text does not use, on top of <c>rightPadding</c>, such as for a button.</summary>
    protected virtual double ReservedRight => 0;

    /// <summary>The fill behind selected text. The theme's accent until QML sets it.</summary>
    [Qml]
    public QColor SelectionColor
    {
        get { Track(); return _selectionColor ?? Theme.Current.AccentFillColorDefault; }
        set { _selectionColor = value; NotifyChanged("selectionColor"); Update(); }
    }

    /// <summary>The color selected text is drawn in. The theme's text on accent until QML sets it.</summary>
    [Qml]
    public QColor SelectedTextColor
    {
        get { Track(); return _selectedTextColor ?? Theme.Current.TextOnAccentFillColorPrimary; }
        set { _selectedTextColor = value; NotifyChanged("selectedTextColor"); Update(); }
    }

    private readonly FontGroup _fontGroup;

    /// <summary>The font. Assigning a whole font, such as <c>font: Theme.typography.body</c>, copies it.</summary>
    [Qml]
    public FontGroup Font { get => _fontGroup; set => _fontGroup.CopyFrom(value); }

    [Qml]
    public int CursorPosition
    {
        get { Track(); return _cursorPosition; }
        set
        {
            value = TextClusters.Snap(_text, value);
            if (SetField(ref _cursorPosition, value))
            {
                if (_composition.Length > 0)
                    _visualDirty = true;
                RestartBlink();
                EnsureCursorVisible();
                NotifyChanged("cursorRectangle");
                Update();
            }
        }
    }

    [Qml]
    public QRect CursorRectangle
    {
        get
        {
            Track("cursorPosition");
            return PositionToRectangle(_cursorPosition);
        }
    }

    [Qml] public int SelectionStart { get { Track(); return Math.Min(_selectionStart, _selectionEnd); } }

    [Qml] public int SelectionEnd { get { Track(); return Math.Max(_selectionStart, _selectionEnd); } }

    [Qml]
    public string SelectedText
    {
        get
        {
            Track("selectionStart");
            return _text.Substring(SelectionStart, SelectionEnd - SelectionStart);
        }
    }

    [Qml] public int EchoMode { get { Track(); return _echoMode; } set { if (SetField(ref _echoMode, value)) { NotifyChanged("displayText"); UpdateImplicit(); } } }

    [Qml] public string PasswordCharacter { get { Track(); return _passwordCharacter; } set { if (SetField(ref _passwordCharacter, string.IsNullOrEmpty(value) ? "●" : TextClusters.First(value))) UpdateImplicit(); } }

    [Qml] public int PasswordMaskDelay { get; set; }

    [Qml] public bool ReadOnly { get { Track(); return _readOnly; } set => SetField(ref _readOnly, value); }

    [Qml] public int MaximumLength { get { Track(); return _maximumLength; } set => SetField(ref _maximumLength, Math.Max(0, value)); }

    [Qml] public int HorizontalAlignment { get { Track(); return _horizontalAlignment; } set { if (SetField(ref _horizontalAlignment, value)) { NotifyChanged("cursorRectangle"); Update(); } } }

    [Qml] public int VerticalAlignment { get { Track(); return _verticalAlignment; } set { if (SetField(ref _verticalAlignment, value)) { NotifyChanged("cursorRectangle"); Update(); } } }

    [Qml] public bool CursorVisible { get { Track(); return _cursorVisible; } set { if (SetField(ref _cursorVisible, value)) Update(); } }

    [Qml] public bool SelectByMouse { get { Track(); return _selectByMouse; } set => SetField(ref _selectByMouse, value); }

    [Qml] public bool PersistentSelection { get; set; }

    [Qml] public bool AutoScroll { get; set; } = true;

    [Qml] public int InputMethodHints { get; set; }

    [Qml] public bool AcceptableInput => true;

    [Qml] public object? Validator { get; set; }

    [Qml] public string InputMask { get; set; } = "";

    [Qml] public QmlComponent? CursorDelegate { get; set; }

    [Qml] public int WrapMode { get; set; }

    [Qml] public bool OverwriteMode { get; set; }

    [Qml] public bool CanUndo { get { Track(); return _undo.Count > 0; } }

    [Qml] public bool CanPaste => true;

    [Qml] public double ContentWidth { get { Track("text"); return OffsetOf(_text.Length); } }

    [Qml] public double ContentHeight => LineHeight;

    [Qml] public double Padding { get { Track(); return _padding; } set { _padding = _leftPadding = _rightPadding = _topPadding = _bottomPadding = value; NotifyChanged("padding"); UpdateImplicit(); } }

    [Qml] public double LeftPadding { get { Track(); return _leftPadding; } set { _leftPadding = value; NotifyChanged("leftPadding"); UpdateImplicit(); } }

    [Qml] public double RightPadding { get { Track(); return _rightPadding; } set { _rightPadding = value; NotifyChanged("rightPadding"); UpdateImplicit(); } }

    [Qml] public double TopPadding { get { Track(); return _topPadding; } set { _topPadding = value; NotifyChanged("topPadding"); UpdateImplicit(); } }

    [Qml] public double BottomPadding { get { Track(); return _bottomPadding; } set { _bottomPadding = value; NotifyChanged("bottomPadding"); UpdateImplicit(); } }

    public bool FocusOnPress => ActiveFocusOnPress && (!_readOnly || _selectByMouse);

    [Qml]
    public bool ActiveFocusOnPress { get; set; } = true;

    public bool AcceptsHover => Enabled;

    int IHoverTarget.CursorShape => 4;

    public void HoverEnter(double x, double y, int modifiers)
    {
    }

    public void HoverMove(double x, double y, int modifiers)
    {
    }

    public void HoverLeave()
    {
    }

    [Qml]
    public void SelectAll() => Select(0, _text.Length);

    [Qml]
    public void Select(int start, int end)
    {
        _selectionStart = Math.Clamp(start, 0, _text.Length);
        _selectionEnd = Math.Clamp(end, 0, _text.Length);
        _cursorPosition = _selectionEnd;
        NotifySelection();
    }

    [Qml]
    public void Deselect()
    {
        _selectionStart = _selectionEnd = _cursorPosition;
        NotifySelection();
    }

    [Qml]
    public void SelectWord()
    {
        var (s, e) = WordAt(_cursorPosition);
        Select(s, e);
    }

    [Qml]
    public void Clear() => SetText("", fromUser: false);

    [Qml]
    public void Insert(int position, string text)
    {
        position = TextClusters.Snap(_text, Math.Clamp(position, 0, _text.Length));
        SetText(_text.Insert(position, text), fromUser: false);
    }

    [Qml]
    public void Remove(int start, int end)
    {
        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, start, _text.Length);
        if (start == end)
            return;
        start = TextClusters.Snap(_text, start);
        end = TextClusters.Ceil(_text, end);
        SetText(_text.Remove(start, end - start), fromUser: false);
    }

    [Qml]
    public string GetText(int start, int end)
    {
        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, start, _text.Length);
        if (start == end)
            return "";
        return _text[TextClusters.Snap(_text, start)..TextClusters.Ceil(_text, end)];
    }

    [Qml]
    public void Undo()
    {
        if (_readOnly || _undo.Count == 0)
            return;
        var (text, cursor) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _composition = "";
        _text = text;
        NotifyChanged("text");
        NotifyChanged("displayText");
        NotifyChanged("length");
        NotifyChanged("canUndo");
        CursorPosition = Math.Clamp(cursor, 0, _text.Length);
        Deselect();
        UpdateImplicit();
        Emit("textEdited");
    }

    [Qml]
    public void Redo()
    {
    }

    [Qml]
    public void Copy()
    {
        if (_echoMode == 0 && SelectionEnd > SelectionStart)
            Engine?.GetService<IClipboard>()?.SetText(SelectedText);
    }

    [Qml]
    public void Cut()
    {
        Copy();
        if (!_readOnly)
            DeleteSelection(fromUser: true);
    }

    [Qml]
    public void Paste()
    {
        if (_readOnly)
            return;
        var text = Engine?.GetService<IClipboard>()?.GetText();
        if (!string.IsNullOrEmpty(text))
            InsertText(text.Replace("\r", "").Replace('\n', ' '));
    }

    [Qml]
    public int PositionAt(double x, double y = 0, int position = 0)
    {
        x -= TextOrigin(LineHeight).X;
        var layout = VisualLayout();
        if (layout is null)
            return 0;
        return FromVisual(layout.PositionAt(x, 0));
    }

    [Qml]
    public QRect PositionToRectangle(int position)
    {
        var lineHeight = LineHeight;
        var (x, y) = TextOrigin(lineHeight);
        return new QRect(x + OffsetOf(position), y, 1, lineHeight);
    }

    [Qml]
    public bool IsRightToLeft(int start, int end)
    {
        if (start > end)
            return false;
        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, start, _text.Length);
        return TextLayout.IsRightToLeft(_text[start..end]);
    }

    [Qml]
    public void EnsureVisible(int position) => EnsureCursorVisible();

    private void NotifySelection()
    {
        NotifyChanged("selectionStart");
        NotifyChanged("selectionEnd");
        NotifyChanged("selectedText");
        NotifyChanged("cursorPosition");
        NotifyChanged("cursorRectangle");
        EnsureCursorVisible();
        Update();
    }

    private void ClampSelection()
    {
        _selectionStart = TextClusters.Snap(_text, _selectionStart);
        _selectionEnd = TextClusters.Snap(_text, _selectionEnd);
    }

    private FontSpec Spec => Font.Spec;

    // The height of one line in the font, measured once per font.
    private double LineHeight
    {
        get
        {
            var spec = Spec;
            if (_lineHeight is { } cached && cached.Font == spec)
                return cached.Height;
            using var layout = TextLayout.Build([new TextSpan("Ag", spec)], double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, 1, true);
            LineHeightBuilds++;
            _lineHeight = (spec, layout.Height);
            return layout.Height;
        }
    }

    /// <summary>How many layouts this input has built to measure its line height.</summary>
    internal int LineHeightBuilds { get; private set; }

    private bool Masked => _echoMode is 2 or 3;

    private string Mask(string text) => TextClusters.Repeat(_passwordCharacter, TextClusters.Count(text));

    /// <summary>A UTF-16 index in the text, as an index into <see cref="DisplayTextRaw"/>.</summary>
    private int TextToMask(int textIndex)
    {
        if (!Masked)
            return Math.Clamp(textIndex, 0, _text.Length);
        return TextClusters.CountBefore(_text, textIndex) * _passwordCharacter.Length;
    }

    /// <summary>An index into the mask, as a UTF-16 index in the text.</summary>
    private int MaskToText(int maskIndex)
    {
        if (!Masked)
            return Math.Clamp(maskIndex, 0, _text.Length);
        var unit = Math.Max(1, _passwordCharacter.Length);
        var element = Math.Clamp(maskIndex, 0, int.MaxValue) / unit;
        return TextClusters.IndexOfElement(_text, element);
    }

    private int Insertion => Math.Clamp(TextToMask(_cursorPosition), 0, DisplayTextRaw.Length);

    private string DisplayTextRaw => _echoMode switch
    {
        1 => "",
        2 or 3 => Mask(_text),
        _ => _text,
    };

    /// <summary>Committed echo with the preedit inserted at the caret. Password fields show the preedit as typed.</summary>
    private string VisualText()
    {
        var raw = DisplayTextRaw;
        if (_composition.Length == 0)
            return raw;
        return raw.Insert(Insertion, _composition);
    }

    private int ToVisual(int index)
    {
        var visual = Math.Clamp(TextToMask(index), 0, DisplayTextRaw.Length);
        if (_composition.Length == 0 || visual < Insertion)
            return visual;
        return visual + _composition.Length;
    }

    private int FromVisual(int visual)
    {
        var start = Insertion;
        if (_composition.Length == 0)
            return MaskToText(visual);
        if (visual < start + _composition.Length)
            return TextClusters.Snap(_text, _cursorPosition);
        return MaskToText(visual - _composition.Length);
    }

    private TextLayout? VisualLayout()
    {
        if (!_visualDirty)
            return _visual;
        _visual?.Dispose();
        _visual = null;
        var shown = VisualText();
        if (shown.Length > 0)
        {
            _visual = TextLayout.Build([new TextSpan(shown, Spec)], double.NaN, TextLayout.Wrap.NoWrap, TextLayout.Elide.None, 0, 1, true);
            if (_composition.Length > 0)
                _visual.UnderlineRange(Insertion, _composition.Length);
        }
        _visualDirty = false;
        return _visual;
    }

    private double OffsetOf(int position) => VisualLayout()?.CaretX(ToVisual(position)) ?? 0;

    /// <summary>Where the text's line starts in the item, after padding, scrolling and alignment. Drawing, clicks and the caret all use it.</summary>
    private (double X, double Y) TextOrigin(double lineHeight)
    {
        var available = Width - _leftPadding - _rightPadding - ReservedRight;
        var total = VisualLayout()?.Width ?? 0;
        var x = _leftPadding - _scrollX;
        if (total < available)
        {
            if ((_horizontalAlignment & QtEnums.AlignHCenter) != 0)
                x += (available - total) / 2;
            else if ((_horizontalAlignment & QtEnums.AlignRight) != 0)
                x += available - total;
        }
        var y = _topPadding;
        if ((_verticalAlignment & QtEnums.AlignVCenter) != 0)
            y = (Height - lineHeight) / 2;
        else if ((_verticalAlignment & QtEnums.AlignBottom) != 0)
            y = Height - _bottomPadding - lineHeight;
        return (x, y);
    }

    public void SetComposition(string text)
    {
        text ??= "";
        if (_readOnly)
            return;
        if (_composition.Length == 0 && text.Length > 0)
            DeleteSelection(fromUser: true);
        if (_composition == text)
            return;
        _composition = text;
        UpdateImplicit();
        NotifyChanged("cursorRectangle");
    }

    public void CommitComposition(string text)
    {
        _composition = "";
        UpdateImplicit();
        if (!string.IsNullOrEmpty(text))
            InsertText(text);
        NotifyChanged("cursorRectangle");
    }

    public void CancelComposition()
    {
        if (_composition.Length == 0)
            return;
        _composition = "";
        UpdateImplicit();
        NotifyChanged("cursorRectangle");
    }

    public QRect ImeCaret() => PositionToRectangle(_cursorPosition);

    private void UpdateImplicit()
    {
        _visualDirty = true;
        using var _ = DependencyTracker.Suspend();
        ImplicitWidth = Math.Ceiling(OffsetOf(_text.Length)) + _leftPadding + _rightPadding + 1;
        ImplicitHeight = Math.Ceiling(LineHeight) + _topPadding + _bottomPadding;
        NotifyChanged("contentWidth");
        Update();
    }

    private void EnsureCursorVisible()
    {
        var available = Width - _leftPadding - _rightPadding - ReservedRight;
        if (available <= 0)
            return;
        var x = OffsetOf(_cursorPosition);
        if (x - _scrollX > available)
            _scrollX = x - available + 1;
        else if (x - _scrollX < 0)
            _scrollX = x;
        var total = OffsetOf(_text.Length);
        if (total - _scrollX < available)
            _scrollX = Math.Max(0, total - available + 1);
    }

    protected override void OnActiveFocusChanged(bool focused)
    {
        _cursorVisible = focused;
        NotifyChanged("cursorVisible");
        if (focused)
        {
            RestartBlink();
        }
        else
        {
            _blink?.Dispose();
            _blink = null;
            if (!PersistentSelection)
                Deselect();
            Emit("editingFinished");
        }
        Update();
    }

    private void RestartBlink()
    {
        _blinkOn = true;
        _blinkElapsed = 0;
        _blink?.Dispose();
        _blink = null;
        ScheduleBlink();
    }

    /// <summary>
    /// Whether the caret still blinks. It blinks at the Windows rate until the caret timeout passes without input,
    /// then stays shown, as a Windows edit box does.
    /// </summary>
    private bool Blinking => Theme.Current.CaretBlinkTime != Theme.CaretNoBlink && _blinkElapsed < Theme.Current.CaretTimeout;

    private void ScheduleBlink()
    {
        if (Blinking && !IsDestroyed && ActiveFocus && Engine is not null)
            _blink = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(Theme.Current.CaretBlinkTime), Blink);
    }

    private void Blink()
    {
        _blink = null;
        if (IsDestroyed || !ActiveFocus || Engine is null)
            return;
        try
        {
            _blinkElapsed += Theme.Current.CaretBlinkTime;
            _blinkOn = !_blinkOn || !Blinking;
            Update();
        }
        finally
        {
            ScheduleBlink();
        }
    }

    public void InsertText(string text)
    {
        if (_readOnly || text.Length == 0)
            return;
        // A tab or other control in a paste used to drop the whole string.
        if (text.Any(char.IsControl))
            text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        DeleteSelection(fromUser: true);
        var room = _maximumLength - _text.Length;
        if (room <= 0)
            return;
        if (text.Length > room)
            text = TextClusters.Limit(text, room);
        if (text.Length == 0)
            return;
        var pos = _cursorPosition;
        SetText(_text.Insert(pos, text), fromUser: true);
        _selectionStart = _selectionEnd = pos + text.Length;
        CursorPosition = pos + text.Length;
        NotifySelection();
    }

    private bool DeleteSelection(bool fromUser)
    {
        var s = SelectionStart;
        var e = SelectionEnd;
        if (e <= s)
            return false;
        SetText(_text.Remove(s, e - s), fromUser);
        _selectionStart = _selectionEnd = s;
        CursorPosition = s;
        NotifySelection();
        return true;
    }

    private (int Start, int End) WordAt(int position)
    {
        var s = position;
        while (s > 0 && char.IsLetterOrDigit(_text[s - 1]))
            s--;
        var e = position;
        while (e < _text.Length && char.IsLetterOrDigit(_text[e]))
            e++;
        return (s, e);
    }

    private int PreviousWord(int pos)
    {
        pos = TextClusters.Snap(_text, pos);
        while (pos > 0 && char.IsWhiteSpace(_text[pos - 1]))
            pos = TextClusters.Previous(_text, pos);
        while (pos > 0 && !char.IsWhiteSpace(_text[pos - 1]))
            pos = TextClusters.Previous(_text, pos);
        return pos;
    }

    private int NextWord(int pos)
    {
        pos = TextClusters.Snap(_text, pos);
        while (pos < _text.Length && !char.IsWhiteSpace(_text[pos]))
            pos = TextClusters.Next(_text, pos);
        while (pos < _text.Length && char.IsWhiteSpace(_text[pos]))
            pos = TextClusters.Next(_text, pos);
        return pos;
    }

    private void MoveCursor(int to, bool select)
    {
        to = TextClusters.Snap(_text, to);
        if (select)
        {
            if (_selectionStart == _selectionEnd)
                _selectionStart = _cursorPosition;
            _selectionEnd = to;
        }
        else
        {
            _selectionStart = _selectionEnd = to;
        }
        _cursorPosition = to;
        RestartBlink();
        NotifySelection();
    }

    public bool KeyPress(KeyEvent e)
    {
        var ctrl = (e.Modifiers & QtEnums.ControlModifier) != 0;
        var alt = (e.Modifiers & QtEnums.AltModifier) != 0;
        var shift = (e.Modifiers & QtEnums.ShiftModifier) != 0;
        switch (e.Key)
        {
            case 0x01000012: // Left
                if (!shift && SelectionEnd > SelectionStart)
                    MoveCursor(SelectionStart, false);
                else
                    MoveCursor(ctrl ? PreviousWord(_cursorPosition) : TextClusters.Previous(_text, _cursorPosition), shift);
                return true;
            case 0x01000014: // Right
                if (!shift && SelectionEnd > SelectionStart)
                    MoveCursor(SelectionEnd, false);
                else
                    MoveCursor(ctrl ? NextWord(_cursorPosition) : TextClusters.Next(_text, _cursorPosition), shift);
                return true;
            case 0x01000010: // Home
                MoveCursor(0, shift);
                return true;
            case 0x01000011: // End
                MoveCursor(_text.Length, shift);
                return true;
            case 0x01000003: // Backspace
                if (_readOnly)
                    return true;
                if (!DeleteSelection(true) && _cursorPosition > 0)
                {
                    var at = TextClusters.Snap(_text, _cursorPosition);
                    var from = ctrl ? PreviousWord(at) : TextClusters.Previous(_text, at);
                    SetText(_text.Remove(from, at - from), true);
                    MoveCursor(from, false);
                }
                return true;
            case 0x01000006 when shift && !ctrl: // Shift+Insert
                Paste();
                return true;
            case 0x01000006 when ctrl && !shift: // Ctrl+Insert
                Copy();
                return true;
            case 0x01000007 when shift && !ctrl: // Shift+Delete
                Cut();
                return true;
            case 0x01000007: // Delete
                if (_readOnly)
                    return true;
                if (!DeleteSelection(true) && _cursorPosition < _text.Length)
                {
                    var at = TextClusters.Snap(_text, _cursorPosition);
                    var to = ctrl ? NextWord(at) : TextClusters.Next(_text, at);
                    SetText(_text.Remove(at, to - at), true);
                    MoveCursor(at, false);
                }
                return true;
            case 0x01000004 or 0x01000005: // Return, Enter
                Emit("accepted");
                Emit("editingFinished");
                return true;
        }
        // AltGr arrives as Ctrl+Alt. Those keys are characters, not shortcuts.
        if (ctrl && !alt)
        {
            switch (e.Key)
            {
                case 'A':
                    SelectAll();
                    return true;
                case 'C':
                    Copy();
                    return true;
                case 'X':
                    Cut();
                    return true;
                case 'V':
                    Paste();
                    return true;
                case 'Z':
                    Undo();
                    return true;
            }
        }
        return false;
    }

    public bool KeyRelease(KeyEvent e) => false;

    public bool AcceptsButton(int button) => Enabled && button == QtEnums.LeftButton;

    public virtual bool PointerPress(double x, double y, int button, int buttons, int modifiers)
    {
        if (ActiveFocusOnPress)
            ForceActiveFocus();
        var pos = PositionAt(x, y);
        _dragAnchor = pos;
        MoveCursor(pos, (modifiers & QtEnums.ShiftModifier) != 0);
        return true;
    }

    public virtual void PointerMove(double x, double y, int buttons, int modifiers)
    {
        if (_dragAnchor < 0 || !_selectByMouse)
            return;
        var pos = PositionAt(x, y);
        _selectionStart = _dragAnchor;
        _selectionEnd = pos;
        _cursorPosition = pos;
        NotifySelection();
    }

    public virtual void PointerRelease(double x, double y, int button, int buttons, int modifiers) => _dragAnchor = -1;

    public bool PointerDoubleClick(double x, double y, int button, int buttons, int modifiers)
    {
        _cursorPosition = PositionAt(x, y);
        SelectWord();
        return true;
    }

    public void PointerCancel() => _dragAnchor = -1;

    public override void Paint(SKCanvas canvas, PaintContext context)
    {
        var available = Width - _leftPadding - _rightPadding - ReservedRight;
        var lineHeight = LineHeight;
        var (x, y) = TextOrigin(lineHeight);
        var layout = VisualLayout();
        var total = layout?.Width ?? 0;
        canvas.Save();
        canvas.ClipRect(SKRect.Create((float)_leftPadding, 0, (float)Math.Max(0, available + 1), (float)Height));
        canvas.Translate((float)x, (float)y);
        void DrawText(QColor color) => layout?.Draw(canvas, Math.Max(total, 1), QtEnums.AlignLeft, color, 0, Antialiasing, paint: context);
        if (SelectionEnd > SelectionStart && (ActiveFocus || PersistentSelection))
        {
            var sx = OffsetOf(SelectionStart);
            var ex = OffsetOf(SelectionEnd);
            var selection = SKRect.Create((float)sx, 0, (float)(ex - sx), (float)lineHeight);
            using var sel = new SKPaint { Color = (_selectionColor ?? Theme.Current.AccentFillColorDefault).ToSk() };
            canvas.DrawRect(selection, sel);
            // The text is drawn twice: in its color outside the selection, and in selectedTextColor inside it.
            canvas.Save();
            canvas.ClipRect(selection, SKClipOperation.Difference);
            DrawText(PaintColor);
            canvas.Restore();
            canvas.Save();
            canvas.ClipRect(selection);
            DrawText(_selectedTextColor ?? Theme.Current.TextOnAccentFillColorPrimary);
            canvas.Restore();
        }
        else
        {
            DrawText(PaintColor);
        }
        if (_cursorVisible && ActiveFocus && _blinkOn && !_readOnly)
        {
            var cx = (float)Math.Round(OffsetOf(_cursorPosition));
            var width = (float)Math.Max(1, Theme.Current.CaretWidth);
            using var caret = new SKPaint { Color = PaintColor.ToSk(), IsAntialias = false };
            canvas.DrawRect(SKRect.Create(cx, 1, width, (float)lineHeight - 2), caret);
        }
        canvas.Restore();
    }

    protected override void OnDestroy()
    {
        _visual?.Dispose();
        _visual = null;
        _blink?.Dispose();
        Theme.Changed -= OnTheme;
        base.OnDestroy();
    }
}

/// <summary>QtQuick's <c>TextEdit</c>. Edits like TextInput; shows its text wrapped.</summary>
public sealed class TextEdit : TextInput
{
    [Qml] public int TextFormat { get; set; }

    [Qml] public double LineCount => 1;
}

/// <summary>The system clipboard, for copy and paste.</summary>
public interface IClipboard
{
    string? GetText();

    void SetText(string text);

    /// <summary>The clipboard text changed outside this call, for <c>Shell.clipboardText</c>.</summary>
    event Action? Changed;
}
