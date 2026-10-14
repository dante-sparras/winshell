namespace Winshell.Core;

/// <summary>
/// Where a strip wider than its window sits, such as the forecast's hours: how far it has moved while the pointer drags it
/// or the wheel turns over it. An offset is how much of the strip is off the window's left edge.
/// </summary>
public static class StripPan
{
    /// <summary>One notch of a mouse wheel, as Windows reports it.</summary>
    public const int Notch = 120;

    /// <summary>The offset while the pointer drags the strip: the strip follows the pointer and stops at either end.</summary>
    /// <param name="startOffset">The offset when the pointer went down.</param>
    /// <param name="pressedAt">Where the pointer went down, across the window.</param>
    /// <param name="pointerAt">Where the pointer is now, on the same axis.</param>
    /// <param name="extent">The strip's width.</param>
    /// <param name="viewport">The window's width.</param>
    /// <returns>The offset now.</returns>
    public static double Dragged(double startOffset, double pressedAt, double pointerAt, double extent, double viewport) =>
        Clamp(startOffset - (pointerAt - pressedAt), extent, viewport);

    /// <summary>
    /// The offset after the wheel turned: <paramref name="step"/> for each notch. A wheel turned toward the person moves on,
    /// as it scrolls down a page, and so does a wheel tilted to the right.
    /// </summary>
    /// <param name="offset">The offset before, or where the strip is already headed.</param>
    /// <param name="delta">The wheel's delta. A touchpad sends parts of a <see cref="Notch"/>.</param>
    /// <param name="horizontal">True for a tilted wheel or a sideways swipe.</param>
    /// <param name="step">How far one notch moves the strip.</param>
    /// <param name="extent">The strip's width.</param>
    /// <param name="viewport">The window's width.</param>
    /// <returns>The offset to move to.</returns>
    public static double Wheeled(double offset, int delta, bool horizontal, double step, double extent, double viewport) =>
        Clamp(offset + ((horizontal ? delta : -delta) * step / Notch), extent, viewport);

    private static double Clamp(double offset, double extent, double viewport) =>
        Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
}
