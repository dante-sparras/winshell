namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The taskbar's app strip. Buttons that do not fit scroll sideways, and a focused button is scrolled fully into view.
/// </summary>
public static class TaskStrip
{
    /// <summary>One app button, in logical pixels.</summary>
    public const double Button = 40;

    /// <summary>The gap between buttons, and between Search and the first one.</summary>
    public const double Spacing = 4;

    /// <summary>The room kept between the apps and the tray.</summary>
    public const double Gap = 12;

    /// <summary>Start, Search and the gaps before the first app.</summary>
    public const double Chrome = Button + Spacing + Button + Spacing;

    /// <summary>How far one wheel notch scrolls. Three of Windows' default lines, 16 pixels each.</summary>
    public const double WheelNotch = 48;

    /// <summary>How wide the buttons are side by side, before the strip limits them.</summary>
    public static double NaturalWidth(int count)
    {
        if (count <= 0)
            return 0;
        return count * Button + (count - 1) * Spacing;
    }

    /// <summary>The left of the button at <paramref name="index"/> in the strip's content.</summary>
    public static double ButtonX(int index) => index * (Button + Spacing);

    /// <summary>
    /// The scroll offset that puts this button fully in view. A button already in view is left where it is.
    /// </summary>
    public static double Reveal(double offset, double viewport, double buttonX, double buttonWidth)
    {
        if (buttonX < offset)
            return buttonX;
        if (buttonX + buttonWidth > offset + viewport)
            return buttonX + buttonWidth - viewport;
        return offset;
    }

    /// <summary>
    /// Where the app group sits, and how wide the strip is, in the bar's content. The strip stops before the tray.
    /// A centered group slides left rather than run under it.
    /// </summary>
    public static (double X, double Strip) Place(double contentWidth, double trayWidth, double naturalStrip, bool centered)
    {
        var trayLeft = Math.Max(0, contentWidth - trayWidth);
        var room = Math.Max(0, trayLeft - Gap - Chrome);
        var strip = Math.Min(Math.Max(0, naturalStrip), room);
        var group = Chrome + strip;
        var centeredAt = Math.Round((contentWidth - group) / 2.0);
        var limit = Math.Max(0, trayLeft - group - Gap);
        var x = centered ? Math.Max(0, Math.Min(centeredAt, limit)) : 0;
        return (x, strip);
    }

    /// <summary>
    /// The offset after one wheel. A downward notch, which is negative, moves toward the later apps. The offset stays
    /// inside the content.
    /// </summary>
    public static double Wheel(double offset, double content, double viewport, int angleDelta)
    {
        var max = Math.Max(0, content - Math.Max(0, viewport));
        return Math.Clamp(offset + -angleDelta / 120.0 * WheelNotch, 0, max);
    }
}
