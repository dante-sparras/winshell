namespace Winshell.Input;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Win = 1,
    Ctrl = 2,
    Alt = 4,
    Shift = 8,
}

internal enum WinKeySide
{
    None,
    Either,
    Left,
    Right,
}

/// <summary>Virtual keys a hotkey chord can name.</summary>
internal static class VirtualKeys
{
    public const int LeftWin = 0x5B;
    public const int RightWin = 0x5C;

    public static bool IsModifier(int vk) => vk switch
    {
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 => true,
        _ => false,
    };
}

/// <summary>One chord, such as <c>Ctrl+Shift+K</c> or the Windows key by itself.</summary>
internal sealed class HotkeyChord
{
    private HotkeyChord(HotkeyModifiers modifiers, int virtualKey, WinKeySide side, string text)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Side = side;
        Text = text;
    }

    public HotkeyModifiers Modifiers { get; }

    /// <summary>The key, or 0 when the chord is the Windows key by itself.</summary>
    public int VirtualKey { get; }

    public WinKeySide Side { get; }

    public string Text { get; }

    public bool IsWinOnly => VirtualKey == 0;

    public OsChord OsKey => new(Modifiers, VirtualKey);

    public uint Win32Modifiers
    {
        get
        {
            uint mods = 0;
            if ((Modifiers & HotkeyModifiers.Alt) != 0)
                mods |= 0x0001;
            if ((Modifiers & HotkeyModifiers.Ctrl) != 0)
                mods |= 0x0002;
            if ((Modifiers & HotkeyModifiers.Shift) != 0)
                mods |= 0x0004;
            if ((Modifiers & HotkeyModifiers.Win) != 0)
                mods |= 0x0008;
            return mods;
        }
    }

    public static HotkeyChord WindowsKey(WinKeySide side) => new(
        HotkeyModifiers.Win,
        0,
        side,
        side switch
        {
            WinKeySide.Left => "Left Win",
            WinKeySide.Right => "Right Win",
            _ => "Win",
        });

    public static HotkeyChord Key(HotkeyModifiers modifiers, int virtualKey, string keyText)
    {
        var names = new List<string>(5);
        if ((modifiers & HotkeyModifiers.Win) != 0)
            names.Add("Win");
        if ((modifiers & HotkeyModifiers.Ctrl) != 0)
            names.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0)
            names.Add("Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0)
            names.Add("Shift");
        names.Add(keyText);
        return new HotkeyChord(modifiers, virtualKey, WinKeySide.None, string.Join('+', names));
    }
}

/// <summary>The <c>RegisterHotKey</c> identity of a chord: its modifiers and virtual key.</summary>
internal readonly record struct OsChord(HotkeyModifiers Modifiers, int VirtualKey);

/// <summary>One or more chords, separated by commas in the text the configuration wrote.</summary>
internal sealed class HotkeySequence
{
    private HotkeySequence(IReadOnlyList<HotkeyChord> chords, string text)
    {
        Chords = chords;
        Text = text;
    }

    public IReadOnlyList<HotkeyChord> Chords { get; }

    /// <summary>The sequence in Windows names, such as <c>Win+Ctrl+K, C</c>.</summary>
    public string Text { get; }

    /// <summary>
    /// Reads a key sequence. An empty string is idle: <paramref name="error"/> is null.
    /// Anything else that is not a sequence sets <paramref name="error"/>.
    /// </summary>
    public static bool TryParse(string? text, out HotkeySequence? sequence, out string? error)
    {
        sequence = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var pieces = text.Split(',');
        if (pieces.Length > 8)
        {
            error = "A key sequence can have at most 8 chords.";
            return false;
        }
        var chords = new List<HotkeyChord>(pieces.Length);
        foreach (var piece in pieces)
        {
            if (!TryChord(piece.Trim(), out var chord, out error))
                return false;
            chords.Add(chord!);
        }
        sequence = new HotkeySequence(chords, string.Join(", ", chords.Select(c => c.Text)));
        return true;
    }

    private static bool TryChord(string raw, out HotkeyChord? chord, out string? error)
    {
        chord = null;
        error = null;
        if (raw.Length == 0)
        {
            error = "A key sequence has an empty chord. For example, Ctrl+K, C.";
            return false;
        }
        var parts = raw.Split('+');
        if (parts.Length > 5 || parts.Any(p => string.IsNullOrWhiteSpace(p)))
        {
            error = Fail(raw, "A chord has an empty key. Write the comma key as Comma, as in Ctrl+Comma.");
            return false;
        }
        HotkeyModifiers mods = 0;
        KeyName? key = null;
        foreach (var piece in parts)
        {
            var part = piece.Trim();
            var norm = Norm(part);
            if (LinuxModifiers.Contains(norm))
            {
                error = Fail(raw, $"\"{part}\" is not a Windows key name. Use Win, Ctrl, Alt or Shift.");
                return false;
            }
            if (RejectedSide(norm, out var sideError))
            {
                error = Fail(raw, sideError);
                return false;
            }
            if (WinSide(norm) is { } side)
            {
                if (parts.Length != 1)
                {
                    error = Fail(raw, side == WinKeySide.Left
                        ? "Windows hotkeys cannot tell Left Win from Win. Use Win+K, or Left Win by itself."
                        : "Windows hotkeys cannot tell Right Win from Win. Use Win+K, or Right Win by itself.");
                    return false;
                }
                chord = HotkeyChord.WindowsKey(side);
                return true;
            }
            if (norm is "win" or "windows")
            {
                if (!AddMod(ref mods, HotkeyModifiers.Win, raw, "Win", out error))
                    return false;
                continue;
            }
            if (norm is "ctrl" or "control")
            {
                if (!AddMod(ref mods, HotkeyModifiers.Ctrl, raw, "Ctrl", out error))
                    return false;
                continue;
            }
            if (norm == "alt")
            {
                if (!AddMod(ref mods, HotkeyModifiers.Alt, raw, "Alt", out error))
                    return false;
                continue;
            }
            if (norm == "shift")
            {
                if (!AddMod(ref mods, HotkeyModifiers.Shift, raw, "Shift", out error))
                    return false;
                continue;
            }
            if (!Keys.TryGetValue(norm, out var found))
            {
                error = Fail(raw, $"Unknown key \"{part}\". Use Win, Ctrl, Alt and Shift, as in Ctrl+Shift+K or Win.");
                return false;
            }
            if (key is not null)
            {
                error = Fail(raw, "A chord can only have one key.");
                return false;
            }
            key = found;
        }
        if (key is not { } named)
        {
            if (mods == HotkeyModifiers.Win)
            {
                chord = HotkeyChord.WindowsKey(WinKeySide.Either);
                return true;
            }
            error = Fail(raw, "A chord needs a key, unless it is the Windows key by itself. For example, Ctrl+Shift+K or Win.");
            return false;
        }
        chord = HotkeyChord.Key(mods, named.VirtualKey, named.Canonical);
        return true;
    }

    private static bool AddMod(ref HotkeyModifiers mods, HotkeyModifiers flag, string raw, string name, out string? error)
    {
        if ((mods & flag) != 0)
        {
            error = Fail(raw, $"{name} is repeated.");
            return false;
        }
        mods |= flag;
        error = null;
        return true;
    }

    private static string Fail(string raw, string detail) => $"Could not read \"{raw}\" as a Windows key chord. {detail}";

    private static string Norm(string part)
    {
        var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }

    private static WinKeySide? WinSide(string norm) => norm switch
    {
        "left win" or "left windows" or "lwin" => WinKeySide.Left,
        "right win" or "right windows" or "rwin" => WinKeySide.Right,
        _ => null,
    };

    private static bool RejectedSide(string norm, out string message)
    {
        message = norm switch
        {
            "left ctrl" or "right ctrl" or "left control" or "right control" or "lctrl" or "rctrl" =>
                "Windows hotkeys cannot tell Left Ctrl from Ctrl. Use Ctrl.",
            "left alt" or "right alt" or "lalt" or "ralt" =>
                "Windows hotkeys cannot tell Left Alt from Alt. Use Alt.",
            "left shift" or "right shift" or "lshift" or "rshift" =>
                "Windows hotkeys cannot tell Left Shift from Shift. Use Shift.",
            _ => "",
        };
        return message.Length > 0;
    }

    private static readonly HashSet<string> LinuxModifiers = new(StringComparer.Ordinal)
    {
        "meta", "super", "mod4", "primary", "hyper",
    };

    private readonly record struct KeyName(int VirtualKey, string Canonical);

    private static readonly Dictionary<string, KeyName> Keys = BuildKeys();

    private static Dictionary<string, KeyName> BuildKeys()
    {
        var keys = new Dictionary<string, KeyName>(StringComparer.Ordinal);
        void Add(int vk, string canonical, params string[] aliases)
        {
            var key = new KeyName(vk, canonical);
            keys[Norm(canonical)] = key;
            foreach (var alias in aliases)
                keys[Norm(alias)] = key;
        }
        for (var letter = 'A'; letter <= 'Z'; letter++)
            Add(letter, letter.ToString());
        for (var digit = '0'; digit <= '9'; digit++)
            Add(digit, digit.ToString());
        for (var function = 1; function <= 24; function++)
            Add(0x6F + function, "F" + function.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(0x08, "Backspace", "Back");
        Add(0x09, "Tab");
        Add(0x0D, "Enter", "Return");
        Add(0x1B, "Esc", "Escape");
        Add(0x20, "Space", "Spacebar");
        Add(0x21, "Page Up", "PageUp", "PgUp");
        Add(0x22, "Page Down", "PageDown", "PgDn");
        Add(0x23, "End");
        Add(0x24, "Home");
        Add(0x25, "Left", "Left Arrow", "Arrow Left");
        Add(0x26, "Up", "Up Arrow", "Arrow Up");
        Add(0x27, "Right", "Right Arrow", "Arrow Right");
        Add(0x28, "Down", "Down Arrow", "Arrow Down");
        Add(0x2C, "Print Screen", "PrintScreen", "PrtSc", "Snapshot");
        Add(0x2D, "Insert", "Ins");
        Add(0x2E, "Delete", "Del");
        Add(0x13, "Pause", "Break");
        Add(0x14, "Caps Lock", "CapsLock");
        Add(0x90, "Num Lock", "NumLock");
        Add(0x91, "Scroll Lock", "ScrollLock");
        Add(0x5D, "Apps", "Menu");
        Add(0xAD, "Volume Mute", "VolumeMute");
        Add(0xAE, "Volume Down", "VolumeDown");
        Add(0xAF, "Volume Up", "VolumeUp");
        Add(0xB0, "Media Next", "MediaNext");
        Add(0xB1, "Media Previous", "MediaPrevious");
        Add(0xB2, "Media Stop", "MediaStop");
        Add(0xB3, "Media Play Pause", "MediaPlayPause");
        for (var pad = 0; pad <= 9; pad++)
            Add(0x60 + pad, "Numpad " + pad.ToString(System.Globalization.CultureInfo.InvariantCulture), "Numpad" + pad.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(0x6A, "Numpad Multiply", "Numpad Star");
        Add(0x6B, "Numpad Add");
        Add(0x6D, "Numpad Subtract");
        Add(0x6E, "Numpad Decimal");
        Add(0x6F, "Numpad Divide");
        Add(0xBA, "Semicolon");
        Add(0xBB, "Equals", "Plus");
        Add(0xBC, "Comma");
        Add(0xBD, "Minus");
        Add(0xBE, "Period");
        Add(0xBF, "Slash");
        Add(0xC0, "Backtick");
        Add(0xDB, "Left Bracket");
        Add(0xDC, "Backslash");
        Add(0xDD, "Right Bracket");
        Add(0xDE, "Quote");
        return keys;
    }
}
