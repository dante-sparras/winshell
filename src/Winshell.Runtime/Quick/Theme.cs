namespace Winshell.Runtime.Quick;

/// <summary>
/// The Windows 11 theme: light or dark, and the accent color, with the Fluent design tokens built from them.
/// The platform updates it when the user changes their Windows settings.
/// </summary>
public sealed record Theme(bool IsDark, QColor Accent, QColor AccentLight1, QColor AccentLight2, QColor AccentLight3, QColor AccentDark1, QColor AccentDark2, QColor AccentDark3)
{
    private static Theme _current = Default(false);

    public static event Action? Changed;

    public static Theme Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;
            _current = value;
            Changed?.Invoke();
        }
    }

    public static Theme Default(bool dark) => new(
        dark,
        QColor.Parse("#0078d4"),
        QColor.Parse("#0093f9"),
        QColor.Parse("#60ccff"),
        QColor.Parse("#98ecff"),
        QColor.Parse("#005fb8"),
        QColor.Parse("#004480"),
        QColor.Parse("#002642"));

    private static QColor C(string s) => QColor.Parse(s);

    // Text
    public QColor TextColor => IsDark ? C("#ffffff") : C("#e4000000");

    public QColor TextSecondary => IsDark ? C("#c5ffffff") : C("#9e000000");

    public QColor TextTertiary => IsDark ? C("#87ffffff") : C("#72000000");

    public QColor TextDisabled => IsDark ? C("#5dffffff") : C("#5c000000");

    public QColor TextOnAccent => IsDark ? C("#000000") : C("#ffffff");

    public QColor TextOnAccentSecondary => IsDark ? C("#80000000") : C("#b3ffffff");

    public QColor TextOnAccentDisabled => IsDark ? C("#87ffffff") : C("#ffffff");

    /// <summary>Accent-colored text, such as a link.</summary>
    public QColor AccentText => IsDark ? AccentLight2 : AccentDark2;

    // Fills
    public QColor ControlFill => IsDark ? C("#0fffffff") : C("#b3ffffff");

    public QColor ControlFillSecondary => IsDark ? C("#15ffffff") : C("#80f9f9f9");

    public QColor ControlFillTertiary => IsDark ? C("#08ffffff") : C("#4df9f9f9");

    public QColor ControlFillDisabled => IsDark ? C("#0bffffff") : C("#4df9f9f9");

    public QColor ControlFillInputActive => IsDark ? C("#b31e1e1e") : C("#ffffff");

    public QColor ControlStrongFill => IsDark ? C("#8bffffff") : C("#72000000");

    public QColor ControlStrongFillDisabled => IsDark ? C("#3fffffff") : C("#51000000");

    public QColor ControlAltFillSecondary => IsDark ? C("#19000000") : C("#06000000");

    public QColor ControlAltFillTertiary => IsDark ? C("#0bffffff") : C("#0f000000");

    public QColor SubtleFillSecondary => IsDark ? C("#0fffffff") : C("#09000000");

    public QColor SubtleFillTertiary => IsDark ? C("#0affffff") : C("#06000000");

    public QColor AccentFill => IsDark ? AccentLight2 : AccentDark1;

    public QColor AccentFillSecondary => AccentFill.WithAlpha(0.9);

    public QColor AccentFillTertiary => AccentFill.WithAlpha(0.8);

    public QColor AccentFillDisabled => IsDark ? C("#28ffffff") : C("#37000000");

    public QColor ControlSolidFill => IsDark ? C("#454545") : C("#ffffff");

    // Strokes
    public QColor ControlStroke => IsDark ? C("#12ffffff") : C("#0f000000");

    public QColor ControlStrokeSecondary => IsDark ? C("#18ffffff") : C("#29000000");

    public QColor ControlStrongStroke => IsDark ? C("#8bffffff") : C("#72000000");

    public QColor ControlStrongStrokeDisabled => IsDark ? C("#28ffffff") : C("#37000000");

    public QColor AccentStrokeSecondary => IsDark ? C("#23000000") : C("#66000000");

    public QColor CardStroke => IsDark ? C("#19000000") : C("#0f000000");

    public QColor SurfaceStroke => IsDark ? C("#66757575") : C("#66757575");

    public QColor DividerStroke => IsDark ? C("#15ffffff") : C("#14000000");

    public QColor FocusStrokeOuter => IsDark ? C("#ffffff") : C("#e4000000");

    public QColor FocusStrokeInner => IsDark ? C("#b3000000") : C("#b3ffffff");

    // Backgrounds
    public QColor CardBackground => IsDark ? C("#0dffffff") : C("#b3ffffff");

    public QColor CardBackgroundSecondary => IsDark ? C("#08ffffff") : C("#80f6f6f6");

    public QColor LayerFill => IsDark ? C("#4c3a3a3a") : C("#80ffffff");

    public QColor SolidBackground => IsDark ? C("#202020") : C("#f3f3f3");

    public QColor SolidBackgroundSecondary => IsDark ? C("#1c1c1c") : C("#eeeeee");

    public QColor SolidBackgroundTertiary => IsDark ? C("#282828") : C("#f9f9f9");

    public QColor FlyoutBackground => IsDark ? C("#2c2c2c") : C("#fcfcfc");

    /// <summary>The tint of an Acrylic surface where the system draws none.</summary>
    public QColor AcrylicFallback => IsDark ? C("#2c2c2c") : C("#f9f9f9");

    public QColor SystemCritical => IsDark ? C("#ff99a4") : C("#c42b1c");

    public QColor SystemSuccess => IsDark ? C("#6ccb5f") : C("#0f7b0f");

    public QColor SystemCaution => IsDark ? C("#fce100") : C("#9d5d00");
}
