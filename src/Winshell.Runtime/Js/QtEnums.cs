namespace Winshell.Runtime;

/// <summary>The members of the <c>Qt</c> namespace that scripts use as constants.</summary>
public static class QtEnums
{
    public const int AlignLeft = 0x0001;
    public const int AlignRight = 0x0002;
    public const int AlignHCenter = 0x0004;
    public const int AlignJustify = 0x0008;
    public const int AlignTop = 0x0020;
    public const int AlignBottom = 0x0040;
    public const int AlignVCenter = 0x0080;
    public const int AlignBaseline = 0x0100;
    public const int AlignCenter = AlignHCenter | AlignVCenter;

    public const int NoButton = 0;
    public const int LeftButton = 1;
    public const int RightButton = 2;
    public const int MiddleButton = 4;
    public const int BackButton = 8;
    public const int ForwardButton = 16;
    public const int AllButtons = 0x07ffffff;

    public const int NoModifier = 0;
    public const int ShiftModifier = 0x02000000;
    public const int ControlModifier = 0x04000000;
    public const int AltModifier = 0x08000000;
    public const int MetaModifier = 0x10000000;
    public const int KeypadModifier = 0x20000000;

    public static IEnumerable<KeyValuePair<string, int>> All
    {
        get
        {
            var list = new List<KeyValuePair<string, int>>
            {
                new("AlignLeft", AlignLeft), new("AlignRight", AlignRight), new("AlignHCenter", AlignHCenter),
                new("AlignJustify", AlignJustify), new("AlignTop", AlignTop), new("AlignBottom", AlignBottom),
                new("AlignVCenter", AlignVCenter), new("AlignBaseline", AlignBaseline), new("AlignCenter", AlignCenter),
                new("AlignAbsolute", 0x0010), new("AlignLeading", AlignLeft), new("AlignTrailing", AlignRight),
                new("NoButton", NoButton), new("LeftButton", LeftButton), new("RightButton", RightButton),
                new("MiddleButton", MiddleButton), new("BackButton", BackButton), new("ForwardButton", ForwardButton),
                new("AllButtons", AllButtons),
                new("NoModifier", NoModifier), new("ShiftModifier", ShiftModifier), new("ControlModifier", ControlModifier),
                new("AltModifier", AltModifier), new("MetaModifier", MetaModifier), new("KeypadModifier", KeypadModifier),
                new("Horizontal", 1), new("Vertical", 2),
                new("LeftToRight", 0), new("RightToLeft", 1), new("LayoutDirectionAuto", 2),
                new("Unchecked", 0), new("PartiallyChecked", 1), new("Checked", 2),
                new("ElideLeft", 0), new("ElideRight", 1), new("ElideMiddle", 2), new("ElideNone", 3),
                new("PlainText", 0), new("RichText", 1), new("AutoText", 2), new("MarkdownText", 3), new("StyledText", 4),
                new("TextDate", 0), new("ISODate", 1), new("RFC2822Date", 8), new("ISODateWithMs", 9),
                new("LocalDate", 2), new("DefaultLocaleShortDate", 6), new("DefaultLocaleLongDate", 7),
                new("NoFocus", 0), new("TabFocus", 1), new("ClickFocus", 2), new("StrongFocus", 11), new("WheelFocus", 15),
                new("IgnoreAspectRatio", 0), new("KeepAspectRatio", 1), new("KeepAspectRatioByExpanding", 2),
                new("FastTransformation", 0), new("SmoothTransformation", 1),
                new("ScrollBarAsNeeded", 0), new("ScrollBarAlwaysOff", 1), new("ScrollBarAlwaysOn", 2),
                new("NoPen", 0), new("SolidLine", 1), new("DashLine", 2), new("DotLine", 3),
                new("RoundCap", 0x20), new("FlatCap", 0), new("SquareCap", 0x10),
                new("MiterJoin", 0), new("BevelJoin", 0x40), new("RoundJoin", 0x80),
                new("ImhNone", 0), new("ImhHiddenText", 1), new("ImhSensitiveData", 2), new("ImhNoPredictiveText", 0x40),
                new("ImhDigitsOnly", 0x10000), new("ImhFormattedNumbersOnly", 0x20000), new("ImhEmailCharactersOnly", 0x200000),
                new("UpArrow", 0), new("DownArrow", 1), new("LeftArrow", 2), new("RightArrow", 3),
                new("TopEdge", 1), new("LeftEdge", 2), new("RightEdge", 4), new("BottomEdge", 8),
                new("TopLeftCorner", 0), new("TopRightCorner", 1), new("BottomLeftCorner", 2), new("BottomRightCorner", 3),
                new("PortraitOrientation", 1), new("LandscapeOrientation", 2), new("PrimaryOrientation", 0),
                new("WindowNoState", 0), new("WindowMinimized", 1), new("WindowMaximized", 2), new("WindowFullScreen", 4), new("WindowActive", 8),
                new("NonModal", 0), new("WindowModal", 1), new("ApplicationModal", 2),
                new("Window", 1), new("Dialog", 3), new("Popup", 9), new("Tool", 11), new("ToolTip", 13), new("SplashScreen", 15),
                new("FramelessWindowHint", 0x800), new("WindowStaysOnTopHint", 0x40000), new("WindowStaysOnBottomHint", 0x4000000),
                new("WindowDoesNotAcceptFocus", 0x200000), new("WindowTransparentForInput", 0x80000),
                new("ApplicationActive", 4), new("ApplicationInactive", 2), new("ApplicationSuspended", 0), new("ApplicationHidden", 1),
                new("PinchGesture", 4), new("TapGesture", 1),
                new("MouseEventNotSynthesized", 0), new("MouseEventSynthesizedBySystem", 1),
                new("RepeatTile", 1), new("StretchTile", 0), new("RoundTile", 2),
                new("ExactHit", 0), new("FuzzyHit", 1),
                new("ColorScheme", 0),
            };
            string[] cursors =
            [
                "ArrowCursor", "UpArrowCursor", "CrossCursor", "WaitCursor", "IBeamCursor", "SizeVerCursor", "SizeHorCursor",
                "SizeBDiagCursor", "SizeFDiagCursor", "SizeAllCursor", "BlankCursor", "SplitVCursor", "SplitHCursor",
                "PointingHandCursor", "ForbiddenCursor", "WhatsThisCursor", "BusyCursor", "OpenHandCursor", "ClosedHandCursor",
                "DragCopyCursor", "DragMoveCursor", "DragLinkCursor",
            ];
            for (var i = 0; i < cursors.Length; i++)
                list.Add(new(cursors[i], i));
            list.Add(new("BitmapCursor", 24));
            list.Add(new("CustomCursor", 25));
            foreach (var (name, code) in Keys)
                list.Add(new("Key_" + name, code));
            return list;
        }
    }

    public static IEnumerable<(string Name, int Code)> Keys
    {
        get
        {
            for (var c = 'A'; c <= 'Z'; c++)
                yield return (c.ToString(), c);
            for (var c = '0'; c <= '9'; c++)
                yield return (c.ToString(), c);
            for (var f = 1; f <= 35; f++)
                yield return ("F" + f, 0x01000030 + f - 1);
            yield return ("Escape", 0x01000000);
            yield return ("Tab", 0x01000001);
            yield return ("Backtab", 0x01000002);
            yield return ("Backspace", 0x01000003);
            yield return ("Return", 0x01000004);
            yield return ("Enter", 0x01000005);
            yield return ("Insert", 0x01000006);
            yield return ("Delete", 0x01000007);
            yield return ("Pause", 0x01000008);
            yield return ("Print", 0x01000009);
            yield return ("SysReq", 0x0100000a);
            yield return ("Clear", 0x0100000b);
            yield return ("Home", 0x01000010);
            yield return ("End", 0x01000011);
            yield return ("Left", 0x01000012);
            yield return ("Up", 0x01000013);
            yield return ("Right", 0x01000014);
            yield return ("Down", 0x01000015);
            yield return ("PageUp", 0x01000016);
            yield return ("PageDown", 0x01000017);
            yield return ("Shift", 0x01000020);
            yield return ("Control", 0x01000021);
            yield return ("Meta", 0x01000022);
            yield return ("Alt", 0x01000023);
            yield return ("CapsLock", 0x01000024);
            yield return ("NumLock", 0x01000025);
            yield return ("ScrollLock", 0x01000026);
            yield return ("Super_L", 0x01000053);
            yield return ("Super_R", 0x01000054);
            yield return ("Menu", 0x01000055);
            yield return ("Space", 0x20);
            yield return ("Exclam", 0x21);
            yield return ("QuoteDbl", 0x22);
            yield return ("NumberSign", 0x23);
            yield return ("Dollar", 0x24);
            yield return ("Percent", 0x25);
            yield return ("Ampersand", 0x26);
            yield return ("Apostrophe", 0x27);
            yield return ("ParenLeft", 0x28);
            yield return ("ParenRight", 0x29);
            yield return ("Asterisk", 0x2a);
            yield return ("Plus", 0x2b);
            yield return ("Comma", 0x2c);
            yield return ("Minus", 0x2d);
            yield return ("Period", 0x2e);
            yield return ("Slash", 0x2f);
            yield return ("Colon", 0x3a);
            yield return ("Semicolon", 0x3b);
            yield return ("Less", 0x3c);
            yield return ("Equal", 0x3d);
            yield return ("Greater", 0x3e);
            yield return ("Question", 0x3f);
            yield return ("At", 0x40);
            yield return ("BracketLeft", 0x5b);
            yield return ("Backslash", 0x5c);
            yield return ("BracketRight", 0x5d);
            yield return ("AsciiCircum", 0x5e);
            yield return ("Underscore", 0x5f);
            yield return ("QuoteLeft", 0x60);
            yield return ("BraceLeft", 0x7b);
            yield return ("Bar", 0x7c);
            yield return ("BraceRight", 0x7d);
            yield return ("AsciiTilde", 0x7e);
            yield return ("VolumeDown", 0x01000070);
            yield return ("VolumeMute", 0x01000071);
            yield return ("VolumeUp", 0x01000072);
            yield return ("MediaPlay", 0x01000080);
            yield return ("MediaStop", 0x01000081);
            yield return ("MediaPrevious", 0x01000082);
            yield return ("MediaNext", 0x01000083);
            yield return ("MediaTogglePlayPause", 0x1000085);
            yield return ("unknown", 0x01ffffff);
        }
    }
}

/// <summary>What the platform does for scripts: open a URL, quit.</summary>
public interface IPlatformServices
{
    void OpenUrl(string url);

    void Quit(int code);
}
