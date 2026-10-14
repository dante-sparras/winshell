namespace Winshell.Core;

/// <summary>
/// Where the shell's own toast sits, in physical pixels, and how it slides.
/// A Windows notification window is placed by the product that parks it, not here.
/// </summary>
public static class ToastPlace
{
    /// <summary>The tallest a toast is drawn, in device-independent pixels. A card that needs more is cut off there.</summary>
    public const int TallestToast = 480;

    /// <summary>
    /// Where a toast sits: <paramref name="gap"/> in from the screen's right edge and from the bar.
    /// Under a top bar the stack runs down from the bar. Otherwise it runs up from the bottom, above a bottom bar,
    /// and a bar on the right edge pushes it left of the bar.
    /// <paramref name="occupied"/> is how far earlier toasts in the stack already reach, gaps included.
    /// </summary>
    /// <param name="screen">The monitor that holds the bar.</param>
    /// <param name="bar">The bar window.</param>
    /// <param name="width">Toast width.</param>
    /// <param name="height">Toast height.</param>
    /// <param name="occupied">Pixels earlier toasts take.</param>
    /// <param name="gap">Pixels between the toast, the screen edge, the bar, and the next toast.</param>
    /// <returns>The toast's place.</returns>
    public static PixelRect Seat(PixelRect screen, PixelRect bar, int width, int height, int occupied, int gap)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        occupied = Math.Max(0, occupied);
        gap = Math.Max(0, gap);
        var horizontal = bar.Width >= bar.Height && bar.Height > 0;
        var alongTop = horizontal && bar.Y - screen.Y <= bar.Height;
        var alongBottom = horizontal && !alongTop && screen.Y + screen.Height - (bar.Y + bar.Height) <= bar.Height;
        var alongRight = !horizontal && bar.Width > 0 && screen.X + screen.Width - (bar.X + bar.Width) <= bar.Width;
        var right = alongRight ? bar.X : screen.X + screen.Width;
        var x = Math.Max(screen.X, right - gap - width);
        if (alongTop)
        {
            return new PixelRect(x, bar.Y + bar.Height + gap + occupied, width, height);
        }

        var floor = (alongBottom ? bar.Y : screen.Y + screen.Height) - gap - occupied;
        return new PixelRect(x, floor - height, width, height);
    }

    /// <summary>
    /// How tall a toast is, in physical pixels: as tall as its card with the padding around it, up to <see cref="TallestToast"/>.
    /// A card with a title and no body is one line tall, with no room kept for a body.
    /// </summary>
    /// <param name="card">What the card and its padding measure, in device-independent pixels.</param>
    /// <param name="scale">Physical pixels per device-independent pixel.</param>
    /// <returns>The height, at least one pixel.</returns>
    public static int Height(double card, double scale) =>
        Math.Max(1, (int)Math.Ceiling(Math.Min(Math.Ceiling(card), TallestToast) * scale));

    /// <summary>The pixels the stack of toasts may take, from the bar's inner edge to the far edge of <paramref name="screen"/>.</summary>
    /// <param name="screen">The monitor that holds the bar.</param>
    /// <param name="bar">The bar window.</param>
    /// <returns>The room along the stack. The whole height when the bar runs along a side.</returns>
    public static int Room(PixelRect screen, PixelRect bar)
    {
        var horizontal = bar.Width >= bar.Height && bar.Height > 0;
        var alongTop = horizontal && bar.Y - screen.Y <= bar.Height;
        var alongBottom = horizontal && !alongTop && screen.Y + screen.Height - (bar.Y + bar.Height) <= bar.Height;
        return alongTop ? screen.Y + screen.Height - (bar.Y + bar.Height)
            : alongBottom ? bar.Y - screen.Y
            : screen.Height;
    }

    /// <summary>How far a toast at <paramref name="slot"/> travels to the right to leave <paramref name="screen"/> entirely.</summary>
    /// <param name="screen">The monitor that holds the toast.</param>
    /// <param name="slot">The toast's resting place.</param>
    /// <returns>Pixels, never negative.</returns>
    public static int Travel(PixelRect screen, PixelRect slot) => Math.Max(0, screen.X + screen.Width - slot.X);

    /// <summary>
    /// How far right of its resting place a sliding toast is, in pixels.
    /// Arriving, it starts <paramref name="travel"/> off the screen edge and settles on the flyout curve.
    /// Leaving, it starts at rest and picks up speed until it is off the screen.
    /// </summary>
    /// <param name="travel">The distance from rest to off screen, from <see cref="Travel"/>.</param>
    /// <param name="time">Zero at the start of the slide and one at the end. Values outside clamp.</param>
    /// <param name="arriving">True for the slide in, false for the slide out.</param>
    /// <returns>The offset from the resting place.</returns>
    public static int SlideOffset(int travel, double time, bool arriving)
    {
        travel = Math.Max(0, travel);
        var moved = arriving ? 1 - MotionCurve.Decelerate.Progress(time) : MotionCurve.Accelerate.Progress(time);
        return (int)Math.Round(travel * moved);
    }

    /// <summary>How long a toast takes to reach a new slot in the stack. The bar moves a chip to a new place in the same time.</summary>
    public static TimeSpan RestackLength => UiMotion.Normal;

    /// <summary>
    /// How far from its new slot a toast is while the stack closes a gap or makes room, in pixels along the stack.
    /// It starts where the window is and settles on the flyout curve, so most of the way is covered early.
    /// </summary>
    /// <param name="from">Where the window was drawn when its slot moved.</param>
    /// <param name="to">The new slot, on the same axis.</param>
    /// <param name="elapsed">Time since the slot moved. A time before the start or past <see cref="RestackLength"/> clamps.</param>
    /// <returns>The offset from <paramref name="to"/>: <paramref name="from"/> less <paramref name="to"/> at the start, and zero from <see cref="RestackLength"/> on.</returns>
    public static int RestackOffset(int from, int to, TimeSpan elapsed)
    {
        var left = 1 - MotionCurve.Decelerate.Progress(elapsed / RestackLength);
        return (int)Math.Round((from - to) * left);
    }
}
