namespace Winshell.Runtime.Quick.Controls;

/// <summary>
/// The colors WinUI 3 gives each part of a control, by state, under the names of its control resources. In light
/// and dark they are tokens. In a contrast theme WinUI names a system color for each, which is not always the one
/// the token maps to, so a contrast theme has its own row here.
/// </summary>
public static class FluentResources
{
    private static StateColors Contrast(Theme t, Func<SystemColors, QColor> rest, Func<SystemColors, QColor> hover, Func<SystemColors, QColor> pressed, Func<SystemColors, QColor> disabled) =>
        new(rest(t.System), hover(t.System), pressed(t.System), disabled(t.System));

    // Button

    public static StateColors ButtonBackground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonFace, s => s.HighlightText, s => s.HighlightText, s => s.ButtonFace)
        : new(t.ControlFillColorDefault, t.ControlFillColorSecondary, t.ControlFillColorTertiary, t.ControlFillColorDisabled);

    public static StateColors ButtonForeground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.TextFillColorPrimary, t.TextFillColorPrimary, t.TextFillColorSecondary, t.TextFillColorDisabled);

    /// <summary>The border in a contrast theme. In light and dark it is the elevation border, plain when pressed or disabled.</summary>
    public static StateColors ButtonBorderBrush(Theme t) =>
        Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.HighlightText, s => s.GrayText);

    public static StateColors AccentButtonBackground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.ButtonText, s => s.ButtonFace, s => s.ButtonFace)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    public static StateColors AccentButtonForeground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.HighlightText, s => s.ButtonFace, s => s.Highlight, s => s.GrayText)
        : new(t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorSecondary, t.TextOnAccentFillColorDisabled);

    public static StateColors AccentButtonBorderBrush(Theme t) =>
        Contrast(t, s => s.Highlight, s => s.Highlight, s => s.Highlight, s => s.GrayText);

    /// <summary>The text of a check box, radio button or toggle switch: it does not dim when pressed.</summary>
    public static StateColors ToggleForeground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.ButtonText, s => s.ButtonText, s => s.GrayText)
        : new(t.TextFillColorPrimary, t.TextFillColorPrimary, t.TextFillColorPrimary, t.TextFillColorDisabled);

    // Check box

    public static StateColors CheckBoxCheckBackgroundFillUnchecked(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonFace, s => s.HighlightText, s => s.Highlight, s => s.Window)
        : new(t.ControlAltFillColorSecondary, t.ControlAltFillColorTertiary, t.ControlAltFillColorQuarternary, t.ControlAltFillColorDisabled);

    public static StateColors CheckBoxCheckBackgroundStrokeUnchecked(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDisabled, t.ControlStrongStrokeColorDisabled);

    public static StateColors CheckBoxCheckBackgroundFillChecked(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.ButtonText, s => s.ButtonFace, s => s.GrayText)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    /// <summary>The checked box's border, which only a contrast theme draws.</summary>
    public static StateColors CheckBoxCheckBackgroundStrokeChecked(Theme t) =>
        Contrast(t, s => s.Highlight, s => s.ButtonText, s => s.ButtonFace, s => s.GrayText);

    public static StateColors CheckBoxCheckGlyphForegroundChecked(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.HighlightText, s => s.ButtonFace, s => s.ButtonText, s => s.Window)
        : new(t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorSecondary, t.TextOnAccentFillColorDisabled);

    public static StateColors CheckBoxCheckBackgroundFillIndeterminate(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.HighlightText, s => s.Highlight, s => s.GrayText)
        : CheckBoxCheckBackgroundFillChecked(t);

    public static StateColors CheckBoxCheckBackgroundStrokeIndeterminate(Theme t) =>
        Contrast(t, s => s.Highlight, s => s.Highlight, s => s.Highlight, s => s.GrayText);

    public static StateColors CheckBoxCheckGlyphForegroundIndeterminate(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.HighlightText, s => s.Highlight, s => s.HighlightText, s => s.Window)
        : CheckBoxCheckGlyphForegroundChecked(t);

    // Radio button

    public static StateColors RadioButtonOuterEllipseFill(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonFace, s => s.HighlightText, s => s.HighlightText, s => s.Window)
        : new(t.ControlAltFillColorSecondary, t.ControlAltFillColorTertiary, t.ControlAltFillColorQuarternary, t.ControlAltFillColorDisabled);

    public static StateColors RadioButtonOuterEllipseStroke(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.HighlightText, s => s.GrayText)
        : new(t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDisabled, t.ControlStrongStrokeColorDisabled);

    public static StateColors RadioButtonOuterEllipseCheckedFill(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.HighlightText, s => s.ButtonFace, s => s.ButtonFace, s => s.Window)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    public static StateColors RadioButtonOuterEllipseCheckedStroke(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.ButtonText, s => s.ButtonFace, s => s.GrayText)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    public static StateColors RadioButtonCheckGlyphFill(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.ButtonText, s => s.ButtonText, s => s.GrayText)
        : new(t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary);

    // Toggle switch

    public static StateColors ToggleSwitchFillOff(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonFace, s => s.HighlightText, s => s.HighlightText, s => s.Window)
        : new(t.ControlAltFillColorSecondary, t.ControlAltFillColorTertiary, t.ControlAltFillColorQuarternary, t.ControlAltFillColorDisabled);

    public static StateColors ToggleSwitchStrokeOff(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDefault, t.ControlStrongStrokeColorDisabled);

    public static StateColors ToggleSwitchKnobFillOff(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.TextFillColorSecondary, t.TextFillColorSecondary, t.TextFillColorSecondary, t.TextFillColorDisabled);

    public static StateColors ToggleSwitchFillOn(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.ButtonFace, s => s.ButtonFace, s => s.Window)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    /// <summary>The on track's border, which only a contrast theme draws.</summary>
    public static StateColors ToggleSwitchStrokeOn(Theme t) =>
        Contrast(t, s => s.Highlight, s => s.ButtonText, s => s.ButtonText, s => s.GrayText);

    public static StateColors ToggleSwitchKnobFillOn(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.HighlightText, s => s.ButtonText, s => s.ButtonText, s => s.GrayText)
        : new(t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorPrimary, t.TextOnAccentFillColorDisabled);

    // Slider

    public static StateColors SliderTrackFill(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.ButtonText, s => s.ButtonText, s => s.GrayText)
        : new(t.ControlStrongFillColorDefault, t.ControlStrongFillColorDefault, t.ControlStrongFillColorDefault, t.ControlStrongFillColorDisabled);

    public static StateColors SliderTrackValueFill(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.Highlight, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    public static StateColors SliderThumbBackground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Hotlight, s => s.GrayText)
        : new(t.AccentFillColorDefault, t.AccentFillColorSecondary, t.AccentFillColorTertiary, t.AccentFillColorDisabled);

    public static QColor SliderOuterThumbBackground(Theme t) => t.IsHighContrast ? t.SystemColorButtonTextColor : t.ControlSolidFillColorDefault;

    // Progress

    public static QColor ProgressBarForeground(Theme t) => t.IsHighContrast ? t.SystemColorHighlightColor : t.AccentFillColorDefault;

    /// <summary>The one pixel track. A contrast theme draws it as WinUI's border, in the window text color.</summary>
    public static QColor ProgressBarBackground(Theme t) => t.IsHighContrast ? t.SystemColorWindowTextColor : t.ControlStrongStrokeColorDefault;

    // Scroll bar

    public static StateColors ScrollBarThumbFill(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.ControlStrongFillColorDefault, t.ControlStrongFillColorDefault, t.ControlStrongFillColorDefault, t.ControlStrongFillColorDisabled);

    public static StateColors ScrollBarButtonArrowForeground(Theme t) => t.IsHighContrast
        ? Contrast(t, s => s.ButtonText, s => s.Highlight, s => s.Highlight, s => s.GrayText)
        : new(t.ControlStrongFillColorDefault, t.TextFillColorSecondary, t.TextFillColorSecondary, t.ControlStrongFillColorDisabled);

    /// <summary>The track of an expanded scroll bar: WinUI's in-app acrylic, here its fallback color.</summary>
    public static QColor ScrollBarTrackFill(Theme t) => t.IsHighContrast ? t.SystemColorWindowColor : t.AcrylicBackgroundFillColorDefault;
}
