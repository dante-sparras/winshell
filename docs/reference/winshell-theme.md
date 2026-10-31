# Winshell.Theme

`import Winshell.Theme`. The module has the `Theme` singleton: the Windows 11 theme as WinUI 3 has it, so a shell can look like the rest of Windows. It holds light or dark, the accent color, whether a contrast theme is on, and the tokens made from them: colors, the type ramp, corner radii, animation timings and shadows. They follow the user's Windows settings as they change.

The default shell's Personalization page sets `colorScheme` and `accentColor`; see [Winshell Settings](../settings.md).

## Theme

A singleton, reached as `Theme`. Every color token is a read-only `color` property with its WinUI 3 theme resource name, first letter lowered, such as `Theme.textFillColorPrimary`; see [Color tokens](#color-tokens) below. `colorScheme` and `accentColor` are the shell's own choice for Winshell's windows, on top of what Windows reports for other programs. A contrast theme still wins over both.

| Member | Type | Description |
| --- | --- | --- |
| `dark` | bool, read-only | Whether the theme in use is dark, after `colorScheme`. |
| `colorScheme` | string | `"system"` follows Windows mode, as the taskbar and Start do, for every Winshell window, Settings included. `"light"` and `"dark"` are the shell's own choice for Winshell's windows, including their title-bar frame. Other programs keep their own mode. Default `"system"`. Another value logs a warning in the `theme` category and changes nothing. |
| `accentColor` | color | The shell's own accent, from which the accent tokens and their shades are made. Default transparent, which follows the accent picked in Windows. |
| `highContrast` | bool, read-only | Whether a contrast theme is on. Every color token is then one of the eight system colors, and the shadows are transparent. |
| `animationsEnabled` | bool, read-only | Whether Windows animation effects are on. Skip motion when this is false. A contrast theme does not turn it off. |
| `autoHideScrollBars` | bool, read-only | False when "Always show scrollbars" is on in Windows' Accessibility settings. Scroll bars then stay wide, with their track and arrows. |
| `fontFamily` | string, read-only | The Windows 11 text font, `"Segoe UI Variable"`. |
| `iconFontFamily` | string, read-only | The Windows 11 icon font, `"Segoe Fluent Icons"`. |
| `typography` | [Typography](#typography), read-only | The Windows 11 type ramp, as fonts to assign whole: `font: Theme.typography.subtitle`. |
| `controlCornerRadius` | real, read-only | The corner radius of controls such as buttons: `4`. |
| `overlayCornerRadius` | real, read-only | The corner radius of flyouts, menus and dialogs: `8`. |
| `controlFasterAnimationDuration` | int, read-only | `83` milliseconds. |
| `controlFastAnimationDuration` | int, read-only | `167` milliseconds. |
| `controlNormalAnimationDuration` | int, read-only | `250` milliseconds. |
| `controlFastOutSlowInKeySpline` | list of real, read-only | The WinUI fast-out, slow-in curve as `easing.bezierCurve` takes it: two control points, then the end point, `[0, 0, 0, 1, 1, 1]`. |
| `cardShadow` | [Shadow](#shadows), read-only | The shadow of a card: blur `4`, offset `2`. |
| `tooltipShadow` | [Shadow](#shadows), read-only | The shadow of a tooltip: blur `8`, offset `4`. |
| `flyoutShadow` | [Shadow](#shadows), read-only | The shadow of a flyout or menu: blur `16`, offset `8`. |
| `dialogShadow` | [Shadow](#shadows), read-only | The shadow of a dialog: blur `64`, offset `32`. |

`colorScheme` and `accentColor` belong to the shell generation that set them. After a reload, the theme follows Windows until the new generation sets them again.

### Color tokens

The color tokens have the values of the Windows App SDK 1.8 theme resources, for light and for dark. In a contrast theme each one is a system color.

- Accent: `systemAccentColor`, `systemAccentColorLight1` to `systemAccentColorLight3`, `systemAccentColorDark1` to `systemAccentColorDark3`, `accentFillColorDefault`, `accentFillColorSecondary`, `accentFillColorTertiary`, `accentFillColorDisabled`, `accentFillColorSelectedTextBackground`, `accentTextFillColorPrimary`, `accentTextFillColorSecondary`, `accentTextFillColorTertiary`, `accentTextFillColorDisabled`.
- Text: `textFillColorPrimary`, `textFillColorSecondary`, `textFillColorTertiary`, `textFillColorDisabled`, `textFillColorInverse`, `textOnAccentFillColorPrimary`, `textOnAccentFillColorSecondary`, `textOnAccentFillColorDisabled`, `textOnAccentFillColorSelectedText`.
- Control fills: `controlFillColorDefault`, `controlFillColorSecondary`, `controlFillColorTertiary`, `controlFillColorDisabled`, `controlFillColorTransparent`, `controlFillColorInputActive`, `controlStrongFillColorDefault`, `controlStrongFillColorDisabled`, `controlSolidFillColorDefault`, `subtleFillColorTransparent`, `subtleFillColorSecondary`, `subtleFillColorTertiary`, `subtleFillColorDisabled`, `controlAltFillColorTransparent`, `controlAltFillColorSecondary`, `controlAltFillColorTertiary`, `controlAltFillColorQuarternary`, `controlAltFillColorDisabled`, `controlOnImageFillColorDefault`, `controlOnImageFillColorSecondary`, `controlOnImageFillColorTertiary`, `controlOnImageFillColorDisabled`.
- Strokes: `controlStrokeColorDefault`, `controlStrokeColorSecondary`, `controlStrokeColorOnAccentDefault`, `controlStrokeColorOnAccentSecondary`, `controlStrokeColorOnAccentTertiary`, `controlStrokeColorOnAccentDisabled`, `controlStrokeColorForStrongFillWhenOnImage`, `controlStrongStrokeColorDefault`, `controlStrongStrokeColorDisabled`, `cardStrokeColorDefault`, `cardStrokeColorDefaultSolid`, `surfaceStrokeColorDefault`, `surfaceStrokeColorFlyout`, `surfaceStrokeColorInverse`, `dividerStrokeColorDefault`, `focusStrokeColorOuter`, `focusStrokeColorInner`.
- Backgrounds: `cardBackgroundFillColorDefault`, `cardBackgroundFillColorSecondary`, `smokeFillColorDefault`, `layerFillColorDefault`, `layerFillColorAlt`, `layerOnAcrylicFillColorDefault`, `layerOnAccentAcrylicFillColorDefault`, `layerOnMicaBaseAltFillColorDefault`, `layerOnMicaBaseAltFillColorSecondary`, `layerOnMicaBaseAltFillColorTertiary`, `layerOnMicaBaseAltFillColorTransparent`, `solidBackgroundFillColorBase`, `solidBackgroundFillColorBaseAlt`, `solidBackgroundFillColorSecondary`, `solidBackgroundFillColorTertiary`, `solidBackgroundFillColorQuarternary`, `solidBackgroundFillColorTransparent`, `acrylicBackgroundFillColorDefault`, `acrylicBackgroundFillColorBase`.
- Status: `systemFillColorAttention`, `systemFillColorSuccess`, `systemFillColorCaution`, `systemFillColorCritical`, `systemFillColorNeutral`, `systemFillColorSolidNeutral`, `systemFillColorAttentionBackground`, `systemFillColorSuccessBackground`, `systemFillColorCautionBackground`, `systemFillColorCriticalBackground`, `systemFillColorNeutralBackground`, `systemFillColorSolidAttentionBackground`, `systemFillColorSolidNeutralBackground`.
- System colors, which a contrast theme is made of: `systemColorWindowColor`, `systemColorWindowTextColor`, `systemColorHighlightColor`, `systemColorHighlightTextColor`, `systemColorHotlightColor`, `systemColorGrayTextColor`, `systemColorButtonFaceColor`, `systemColorButtonTextColor`.

The accent shades follow the accent picked in Windows. With `accentColor` set, they keep its hue and saturation and move its lightness as far as Windows' shades of the default blue do. The acrylic colors are the colors WinUI shows without the blur; a window gets the real material from `WindowEffects.backdrop`.

### Typography

`Theme.typography` has one font per step of the Windows 11 type ramp, all in Segoe UI Variable, with sizes in pixels: `caption` (12, regular), `body` (14, regular), `bodyStrong` (14, semibold), `bodyLarge` (18, regular), `subtitle` (20, semibold), `title` (28, semibold), `titleLarge` (40, semibold) and `display` (68, semibold).

### Shadows

A shadow has `blur`, `verticalOffset` (how far it falls below the item, in pixels) and `color`: black, a little darker in dark mode, and transparent in a contrast theme.

```qml
import QtQuick
import Winshell
import Winshell.Theme

FloatingWindow {
    implicitWidth: 320
    implicitHeight: 120
    color: "transparent"

    Rectangle {
        anchors.fill: parent
        radius: Theme.overlayCornerRadius
        color: Theme.solidBackgroundFillColorBase
        border.width: 1
        border.color: Theme.surfaceStrokeColorFlyout

        Text {
            anchors.centerIn: parent
            text: Theme.dark ? "Dark mode" : "Light mode"
            color: Theme.textFillColorPrimary
            font: Theme.typography.subtitle
        }
    }
}
```
