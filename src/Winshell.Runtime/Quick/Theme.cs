namespace Winshell.Runtime.Quick;

/// <summary>The accent color the user picked in Windows, with the lighter and darker shades Windows derives from it.</summary>
public sealed record AccentColors(QColor Accent, QColor Light1, QColor Light2, QColor Light3, QColor Dark1, QColor Dark2, QColor Dark3)
{
    /// <summary>The Windows default blue.</summary>
    public static readonly AccentColors Default = new(
        QColor.FromArgb(0xFF0078D4),
        QColor.FromArgb(0xFF0093F9),
        QColor.FromArgb(0xFF60CCFF),
        QColor.FromArgb(0xFF98ECFF),
        QColor.FromArgb(0xFF005FB8),
        QColor.FromArgb(0xFF004480),
        QColor.FromArgb(0xFF002642));

    // How far each shade moves the lightness toward white or black, from the Windows default blue and its shades.
    private static readonly double[] Lighter = [0.123, 0.466, 0.654];
    private static readonly double[] Darker = [0.132, 0.397, 0.69];

    /// <summary>
    /// The shades of an accent the user picked in Winshell rather than in Windows. Windows does not say how it makes
    /// them, so each keeps the hue and saturation and moves the lightness as far as the default blue's shades do.
    /// </summary>
    public static AccentColors From(QColor accent)
    {
        accent = accent with { A = 1 };
        var (h, s, l) = accent.ToHsl();
        QColor Light(int i) => QColor.FromHsl(h, s, l + (1 - l) * Lighter[i]);
        QColor Dark(int i) => QColor.FromHsl(h, s, l * (1 - Darker[i]));
        return new(accent, Light(0), Light(1), Light(2), Dark(0), Dark(1), Dark(2));
    }
}

/// <summary>
/// The shell's own choice of light or dark and of the accent, applied on top of what Windows reports. Null follows
/// Windows.
/// </summary>
public sealed record ThemeChoice(bool? Dark = null, QColor? Accent = null)
{
    public static readonly ThemeChoice None = new();
}

/// <summary>
/// The eight system colors. A contrast theme is these colors and nothing else: while one is on, WinUI maps every
/// Fluent token to one of them.
/// </summary>
public sealed record SystemColors(QColor Window, QColor WindowText, QColor Highlight, QColor HighlightText, QColor Hotlight, QColor GrayText, QColor ButtonFace, QColor ButtonText)
{
    /// <summary>The colors Windows reports when no contrast theme is on.</summary>
    public static readonly SystemColors Default = new(
        QColor.FromArgb(0xFFFFFFFF),
        QColor.FromArgb(0xFF000000),
        QColor.FromArgb(0xFF0078D7),
        QColor.FromArgb(0xFFFFFFFF),
        QColor.FromArgb(0xFF0066CC),
        QColor.FromArgb(0xFF6D6D6D),
        QColor.FromArgb(0xFFF0F0F0),
        QColor.FromArgb(0xFF000000));
}

/// <summary>A drop shadow: how far it is blurred, how far it falls below the item, and its color.</summary>
public readonly record struct Elevation(float Blur, float OffsetY, QColor Color);

/// <summary>
/// The Windows 11 theme: light or dark, whether a contrast theme is on, the accent and the system colors, with the
/// Fluent design tokens made from them. A token has the name of its WinUI 3 theme resource and the values of the
/// Windows App SDK 1.8 <c>generic.xaml</c>. Animation effects follow Windows, and a contrast theme does not turn
/// them off. <see cref="WheelScrollLines"/> is how many lines a wheel notch scrolls, or <see cref="WheelPageScroll"/>
/// for a page, as the Windows mouse settings say. <see cref="DragThreshold"/> is how far a press moves before it drags,
/// and a second tap within <see cref="DoubleClickTime"/> milliseconds and inside a <see cref="DoubleClickWidth"/> by
/// <see cref="DoubleClickHeight"/> rectangle centered on the first is a double tap, in logical pixels.
/// The text caret blinks every <see cref="CaretBlinkTime"/> milliseconds,
/// or not at all when it is <see cref="CaretNoBlink"/>, stops blinking after <see cref="CaretTimeout"/> milliseconds
/// without input, and is <see cref="CaretWidth"/> logical pixels wide, as the Windows keyboard and text cursor settings say.
/// The platform updates the theme when the user changes their Windows settings, and a shell can choose
/// light or dark and its own accent on top (<see cref="Choose"/>).
/// </summary>
public sealed record Theme(
    bool IsDark,
    AccentColors Accents,
    SystemColors System,
    bool IsHighContrast = false,
    bool AnimationsEnabled = true,
    uint WheelScrollLines = 3,
    uint CaretBlinkTime = 530,
    uint CaretTimeout = 5000,
    double CaretWidth = 1,
    double DragThreshold = 4,
    uint DoubleClickTime = 500,
    double DoubleClickWidth = 4,
    double DoubleClickHeight = 4)
{
    /// <summary>The <see cref="WheelScrollLines"/> that scrolls a page a notch, Win32's <c>WHEEL_PAGESCROLL</c>.</summary>
    public const uint WheelPageScroll = uint.MaxValue;

    /// <summary>The <see cref="CaretBlinkTime"/> of a caret that does not blink, Win32's <c>INFINITE</c>.</summary>
    public const uint CaretNoBlink = uint.MaxValue;

    private static Theme _reported = Default(false);
    private static Theme _current = _reported;
    private static ThemeChoice _choice = ThemeChoice.None;
    private static object? _chooser;

    /// <summary>Raised when <see cref="Current"/> or <see cref="Choice"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>How many listeners <see cref="Changed"/> has. A destroyed palette must not leave any behind.</summary>
    internal static int ListenerCount => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>
    /// The theme to draw with: what Windows reports, with the shell's <see cref="Choice"/> on top. Setting it
    /// replaces what Windows reports, and the choice still applies.
    /// </summary>
    public static Theme Current
    {
        get => _current;
        set
        {
            _reported = value;
            Update(false);
        }
    }

    /// <summary>What Windows reports, without the shell's choice.</summary>
    public static Theme Reported => _reported;

    public static ThemeChoice Choice => _choice;

    /// <summary>
    /// Makes this the shell's choice. Only the owner of the last choice can take it back, so the generation a reload
    /// replaces cannot undo the choice of the one that replaced it.
    /// </summary>
    public static void Choose(object owner, ThemeChoice choice)
    {
        _chooser = owner;
        var changed = _choice != choice;
        _choice = choice;
        Update(changed);
    }

    /// <summary>Follows Windows again, when <paramref name="owner"/> made the choice in effect.</summary>
    public static void Release(object owner)
    {
        if (!ReferenceEquals(_chooser, owner))
            return;
        _chooser = null;
        var changed = _choice != ThemeChoice.None;
        _choice = ThemeChoice.None;
        Update(changed);
    }

    private static void Update(bool choiceChanged)
    {
        var next = _reported with
        {
            IsDark = _choice.Dark ?? _reported.IsDark,
            Accents = _choice.Accent is { } accent ? AccentColors.From(accent) : _reported.Accents,
        };
        if (_current == next && !choiceChanged)
            return;
        _current = next;
        Changed?.Invoke();
    }

    public static Theme Default(bool dark) => new(dark, AccentColors.Default, SystemColors.Default);

    /// <summary>A token's dark or light value, or its system color in a contrast theme.</summary>
    private QColor Pick(uint dark, uint light, QColor highContrast) =>
        IsHighContrast ? highContrast : QColor.FromArgb(IsDark ? dark : light);

    private QColor PickAccent(QColor dark, QColor light, QColor highContrast, double opacity = 1) =>
        IsHighContrast ? highContrast : (IsDark ? dark : light).WithAlpha(opacity);

    // The accent and the system colors

    public QColor SystemAccentColor => Accents.Accent;

    public QColor SystemAccentColorLight1 => Accents.Light1;

    public QColor SystemAccentColorLight2 => Accents.Light2;

    public QColor SystemAccentColorLight3 => Accents.Light3;

    public QColor SystemAccentColorDark1 => Accents.Dark1;

    public QColor SystemAccentColorDark2 => Accents.Dark2;

    public QColor SystemAccentColorDark3 => Accents.Dark3;

    public QColor SystemColorWindowColor => System.Window;

    public QColor SystemColorWindowTextColor => System.WindowText;

    public QColor SystemColorHighlightColor => System.Highlight;

    public QColor SystemColorHighlightTextColor => System.HighlightText;

    public QColor SystemColorHotlightColor => System.Hotlight;

    public QColor SystemColorGrayTextColor => System.GrayText;

    public QColor SystemColorButtonFaceColor => System.ButtonFace;

    public QColor SystemColorButtonTextColor => System.ButtonText;

    // Colors made from the accent

    public QColor AccentTextFillColorPrimary => PickAccent(Accents.Light3, Accents.Dark2, System.WindowText);

    public QColor AccentTextFillColorSecondary => PickAccent(Accents.Light3, Accents.Dark3, System.WindowText);

    public QColor AccentTextFillColorTertiary => PickAccent(Accents.Light2, Accents.Dark1, System.WindowText);

    public QColor AccentFillColorSelectedTextBackground => PickAccent(Accents.Accent, Accents.Accent, System.Window);

    public QColor AccentFillColorDefault => PickAccent(Accents.Light2, Accents.Dark1, System.Window);

    public QColor AccentFillColorSecondary => PickAccent(Accents.Light2, Accents.Dark1, System.Window, 0.9);

    public QColor AccentFillColorTertiary => PickAccent(Accents.Light2, Accents.Dark1, System.Window, 0.8);

    public QColor SystemFillColorAttention => PickAccent(Accents.Light2, Accents.Accent, System.WindowText);

    // The fixed colors

    public QColor TextFillColorPrimary => Pick(0xFFFFFFFF, 0xE4000000, System.WindowText);

    public QColor TextFillColorSecondary => Pick(0xC5FFFFFF, 0x9E000000, System.WindowText);

    public QColor TextFillColorTertiary => Pick(0x87FFFFFF, 0x72000000, System.WindowText);

    public QColor TextFillColorDisabled => Pick(0x5DFFFFFF, 0x5C000000, System.GrayText);

    public QColor TextFillColorInverse => Pick(0xE4000000, 0xFFFFFFFF, System.WindowText);

    public QColor AccentTextFillColorDisabled => Pick(0x5DFFFFFF, 0x5C000000, System.GrayText);

    public QColor TextOnAccentFillColorSelectedText => Pick(0xFFFFFFFF, 0xFFFFFFFF, System.WindowText);

    public QColor TextOnAccentFillColorPrimary => Pick(0xFF000000, 0xFFFFFFFF, System.WindowText);

    public QColor TextOnAccentFillColorSecondary => Pick(0x80000000, 0xB3FFFFFF, System.WindowText);

    public QColor TextOnAccentFillColorDisabled => Pick(0x87FFFFFF, 0xFFFFFFFF, System.GrayText);

    public QColor ControlFillColorDefault => Pick(0x0FFFFFFF, 0xB3FFFFFF, System.ButtonFace);

    public QColor ControlFillColorSecondary => Pick(0x15FFFFFF, 0x80F9F9F9, System.ButtonFace);

    public QColor ControlFillColorTertiary => Pick(0x08FFFFFF, 0x4DF9F9F9, System.ButtonFace);

    public QColor ControlFillColorDisabled => Pick(0x0BFFFFFF, 0x4DF9F9F9, System.ButtonFace);

    public QColor ControlFillColorTransparent => Pick(0x00FFFFFF, 0x00FFFFFF, QColor.Transparent);

    public QColor ControlFillColorInputActive => Pick(0xB31E1E1E, 0xFFFFFFFF, System.ButtonFace);

    public QColor ControlStrongFillColorDefault => Pick(0x8BFFFFFF, 0x72000000, System.ButtonFace);

    public QColor ControlStrongFillColorDisabled => Pick(0x3FFFFFFF, 0x51000000, System.ButtonFace);

    public QColor ControlSolidFillColorDefault => Pick(0xFF454545, 0xFFFFFFFF, System.ButtonFace);

    public QColor SubtleFillColorTransparent => Pick(0x00FFFFFF, 0x00FFFFFF, QColor.Transparent);

    public QColor SubtleFillColorSecondary => Pick(0x0FFFFFFF, 0x09000000, System.ButtonFace);

    public QColor SubtleFillColorTertiary => Pick(0x0AFFFFFF, 0x06000000, System.ButtonFace);

    public QColor SubtleFillColorDisabled => Pick(0x00FFFFFF, 0x00FFFFFF, System.ButtonFace);

    public QColor ControlAltFillColorTransparent => Pick(0x00FFFFFF, 0x00FFFFFF, QColor.Transparent);

    public QColor ControlAltFillColorSecondary => Pick(0x19000000, 0x06000000, System.ButtonFace);

    public QColor ControlAltFillColorTertiary => Pick(0x0BFFFFFF, 0x0F000000, System.ButtonFace);

    public QColor ControlAltFillColorQuarternary => Pick(0x12FFFFFF, 0x18000000, System.ButtonFace);

    public QColor ControlAltFillColorDisabled => Pick(0x00FFFFFF, 0x00FFFFFF, System.ButtonFace);

    public QColor ControlOnImageFillColorDefault => Pick(0xB31C1C1C, 0xC9FFFFFF, System.ButtonFace);

    public QColor ControlOnImageFillColorSecondary => Pick(0xFF1A1A1A, 0xFFF3F3F3, System.ButtonFace);

    public QColor ControlOnImageFillColorTertiary => Pick(0xFF131313, 0xFFEBEBEB, System.ButtonFace);

    public QColor ControlOnImageFillColorDisabled => Pick(0xFF1E1E1E, 0x00FFFFFF, System.ButtonFace);

    public QColor AccentFillColorDisabled => Pick(0x28FFFFFF, 0x37000000, System.Window);

    public QColor ControlStrokeColorDefault => Pick(0x12FFFFFF, 0x0F000000, System.ButtonText);

    public QColor ControlStrokeColorSecondary => Pick(0x18FFFFFF, 0x29000000, System.ButtonText);

    public QColor ControlStrokeColorOnAccentDefault => Pick(0x14FFFFFF, 0x14FFFFFF, System.ButtonText);

    public QColor ControlStrokeColorOnAccentSecondary => Pick(0x23000000, 0x66000000, System.ButtonText);

    public QColor ControlStrokeColorOnAccentTertiary => Pick(0x37000000, 0x37000000, System.ButtonText);

    public QColor ControlStrokeColorOnAccentDisabled => Pick(0x33000000, 0x0F000000, System.GrayText);

    public QColor ControlStrokeColorForStrongFillWhenOnImage => Pick(0x6B000000, 0x59FFFFFF, System.ButtonText);

    public QColor CardStrokeColorDefault => Pick(0x19000000, 0x0F000000, System.WindowText);

    public QColor CardStrokeColorDefaultSolid => Pick(0xFF1C1C1C, 0xFFEBEBEB, System.WindowText);

    public QColor ControlStrongStrokeColorDefault => Pick(0x8BFFFFFF, 0x72000000, System.ButtonText);

    public QColor ControlStrongStrokeColorDisabled => Pick(0x28FFFFFF, 0x37000000, System.ButtonText);

    public QColor SurfaceStrokeColorDefault => Pick(0x66757575, 0x66757575, System.WindowText);

    public QColor SurfaceStrokeColorFlyout => Pick(0x33000000, 0x0F000000, System.WindowText);

    public QColor SurfaceStrokeColorInverse => Pick(0x0F000000, 0x15FFFFFF, System.WindowText);

    public QColor DividerStrokeColorDefault => Pick(0x15FFFFFF, 0x0F000000, System.WindowText);

    public QColor FocusStrokeColorOuter => Pick(0xFFFFFFFF, 0xE4000000, System.WindowText);

    public QColor FocusStrokeColorInner => Pick(0xB3000000, 0xB3FFFFFF, System.Window);

    public QColor CardBackgroundFillColorDefault => Pick(0x0DFFFFFF, 0xB3FFFFFF, System.Window);

    public QColor CardBackgroundFillColorSecondary => Pick(0x08FFFFFF, 0x80F6F6F6, System.Window);

    public QColor SmokeFillColorDefault => Pick(0x4D000000, 0x4D000000, System.Window);

    public QColor LayerFillColorDefault => Pick(0x4C3A3A3A, 0x80FFFFFF, System.Window);

    public QColor LayerFillColorAlt => Pick(0x0DFFFFFF, 0xFFFFFFFF, System.Window);

    public QColor LayerOnAcrylicFillColorDefault => Pick(0x09FFFFFF, 0x40FFFFFF, System.Window);

    public QColor LayerOnAccentAcrylicFillColorDefault => Pick(0x09FFFFFF, 0x40FFFFFF, System.Window);

    public QColor LayerOnMicaBaseAltFillColorDefault => Pick(0x733A3A3A, 0xB3FFFFFF, System.Window);

    public QColor LayerOnMicaBaseAltFillColorSecondary => Pick(0x0FFFFFFF, 0x0A000000, System.Window);

    public QColor LayerOnMicaBaseAltFillColorTertiary => Pick(0xFF2C2C2C, 0xFFF9F9F9, System.Window);

    public QColor LayerOnMicaBaseAltFillColorTransparent => Pick(0x00FFFFFF, 0x00000000, System.Window);

    public QColor SolidBackgroundFillColorBase => Pick(0xFF202020, 0xFFF3F3F3, System.Window);

    public QColor SolidBackgroundFillColorSecondary => Pick(0xFF1C1C1C, 0xFFEEEEEE, System.Window);

    public QColor SolidBackgroundFillColorTertiary => Pick(0xFF282828, 0xFFF9F9F9, System.Window);

    public QColor SolidBackgroundFillColorQuarternary => Pick(0xFF2C2C2C, 0xFFFFFFFF, System.Window);

    public QColor SolidBackgroundFillColorTransparent => Pick(0x00202020, 0x00F3F3F3, System.Window.WithAlpha(0));

    public QColor SolidBackgroundFillColorBaseAlt => Pick(0xFF0A0A0A, 0xFFDADADA, System.Window);

    public QColor SystemFillColorSuccess => Pick(0xFF6CCB5F, 0xFF0F7B0F, System.WindowText);

    public QColor SystemFillColorCaution => Pick(0xFFFCE100, 0xFF9D5D00, System.WindowText);

    public QColor SystemFillColorCritical => Pick(0xFFFF99A4, 0xFFC42B1C, System.WindowText);

    public QColor SystemFillColorNeutral => Pick(0x8BFFFFFF, 0x72000000, System.WindowText);

    public QColor SystemFillColorSolidNeutral => Pick(0xFF9D9D9D, 0xFF8A8A8A, System.WindowText);

    public QColor SystemFillColorAttentionBackground => Pick(0x08FFFFFF, 0x80F6F6F6, System.Window);

    public QColor SystemFillColorSuccessBackground => Pick(0xFF393D1B, 0xFFDFF6DD, System.Window);

    public QColor SystemFillColorCautionBackground => Pick(0xFF433519, 0xFFFFF4CE, System.Window);

    public QColor SystemFillColorCriticalBackground => Pick(0xFF442726, 0xFFFDE7E9, System.Window);

    public QColor SystemFillColorNeutralBackground => Pick(0x08FFFFFF, 0x06000000, System.Window);

    public QColor SystemFillColorSolidAttentionBackground => Pick(0xFF2E2E2E, 0xFFF7F7F7, System.Window);

    public QColor SystemFillColorSolidNeutralBackground => Pick(0xFF2E2E2E, 0xFFF3F3F3, System.Window);

    // Acrylic. The scene cannot blur what is behind an item, so these are the colors WinUI shows without the blur.
    // A popup window gets the real material from the DWM.

    public QColor AcrylicBackgroundFillColorDefault => Pick(0xFF2C2C2C, 0xFFF9F9F9, System.Window);

    public QColor AcrylicBackgroundFillColorBase => Pick(0xFF1C1C1C, 0xFFEEEEEE, System.Window);

    // Elevation, from the shadows of the Windows 11 design kit. WinUI draws them with ThemeShadow, which has no
    // theme resource. A contrast theme draws no shadows.

    public Elevation CardShadow => Shadow(4, 2, 0.04, 0.13);

    public Elevation TooltipShadow => Shadow(8, 4, 0.14, 0.26);

    public Elevation FlyoutShadow => Shadow(16, 8, 0.14, 0.26);

    public Elevation DialogShadow => Shadow(64, 32, 0.19, 0.37);

    private Elevation Shadow(float blur, float offsetY, double light, double dark) =>
        new(blur, offsetY, IsHighContrast ? QColor.Transparent : QColor.Black.WithAlpha(IsDark ? dark : light));
}
