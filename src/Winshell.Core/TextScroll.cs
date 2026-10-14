namespace Winshell.Core;

/// <summary>
/// How a fixed-width line of text moves. The line sits still when it fits in the box.
/// When it does not, one loop travels the line and a gap, then a second copy of the line is where the first began.
/// </summary>
/// <param name="Runs">True when the line is wider than its box and scrolls.</param>
/// <param name="Travel">The distance of one loop, in device-independent pixels. Zero when <see cref="Runs"/> is false.</param>
/// <param name="Duration">How long one loop takes. Zero when <see cref="Runs"/> is false.</param>
public readonly record struct TextScroll(bool Runs, double Travel, TimeSpan Duration)
{
    /// <summary>A line that fits in its box, so it sits still.</summary>
    public static TextScroll Still => default;

    /// <summary>How fast a scrolling line moves, in device-independent pixels per second. A longer line takes longer.</summary>
    public const double Speed = 36;

    /// <summary>The space between a scrolling line and the copy that follows it, in device-independent pixels.</summary>
    public const double Gap = 40;

    /// <summary>
    /// The loop for a line <paramref name="textWidth"/> wide drawn in a box <paramref name="boxWidth"/> wide.
    /// One loop travels the line and <paramref name="gap"/> at <paramref name="speed"/>, so the speed stays the same however long the line is.
    /// </summary>
    /// <param name="textWidth">The measured width of the line.</param>
    /// <param name="boxWidth">The width the line is drawn in.</param>
    /// <param name="gap">The space between the line and the copy that follows it. A negative gap is no gap.</param>
    /// <param name="speed">How fast the line moves, in device-independent pixels per second.</param>
    /// <returns><see cref="Still"/> when the line fits, or when a width or the speed is not usable.</returns>
    public static TextScroll For(double textWidth, double boxWidth, double gap, double speed)
    {
        if (!double.IsFinite(textWidth)
            || !double.IsFinite(boxWidth)
            || !double.IsFinite(speed)
            || !double.IsFinite(gap)
            || textWidth <= boxWidth
            || boxWidth <= 0
            || speed <= 0)
        {
            return Still;
        }

        var travel = textWidth + Math.Max(0, gap);
        return new TextScroll(true, travel, TimeSpan.FromSeconds(travel / speed));
    }
}
