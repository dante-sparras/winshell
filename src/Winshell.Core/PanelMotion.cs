
namespace Winshell.Core;

/// <summary>
/// The window around a sliding panel. The extra strip lies under the bar.
/// <paramref name="StartX"/> and <paramref name="StartY"/> move the card into that strip. Zero is the resting place.
/// </summary>
/// <param name="X">Window left, in the same pixels as <paramref name="StartX"/>.</param>
/// <param name="Y">Window top, in the same pixels as <paramref name="StartY"/>.</param>
/// <param name="Width">Window width, including the strip.</param>
/// <param name="Height">Window height, including the strip.</param>
/// <param name="StartX">Card translation at the start. Negative tucks left, under a left bar.</param>
/// <param name="StartY">Card translation at the start. Negative tucks up, under a top bar.</param>
public readonly record struct PanelShell(int X, int Y, int Width, int Height, int StartX, int StartY);

/// <summary>How far a panel is from its resting place. Positive <paramref name="X"/> is right. Positive <paramref name="Y"/> is down.</summary>
/// <param name="X">Horizontal distance, in device-independent pixels.</param>
/// <param name="Y">Vertical distance, in device-independent pixels.</param>
public readonly record struct PanelShift(int X, int Y);

/// <summary>
/// The slide a panel takes out of the bar and back into it.
/// The entrance matches the Windows 11 flyout: most of the travel happens early, then the panel settles.
/// The exit is that motion reversed, so the panel leaves slowly and finishes into the bar.
/// </summary>
public static class PanelMotion
{
    /// <summary>Device-independent pixels of travel. One bar thick, so the panel starts tucked under the bar.</summary>
    public const int Travel = 48;

    /// <summary>How long the entrance takes.</summary>
    public static readonly TimeSpan EnterDuration = UiMotion.Normal;

    /// <summary>How long the exit takes.</summary>
    public static readonly TimeSpan LeaveDuration = UiMotion.Fast;

    /// <summary>
    /// Offset from the resting place toward <paramref name="edge"/>.
    /// Zero when <paramref name="motion"/> is false, which is animation effects turned off.
    /// </summary>
    /// <param name="edge">Screen edge the bar occupies.</param>
    /// <param name="motion">False when the panel should appear in place.</param>
    /// <returns>The start of the entrance, and the end of the exit.</returns>
    public static PanelShift TowardBar(BarEdge edge, bool motion)
    {
        if (!motion)
        {
            return default;
        }

        return edge switch
        {
            BarEdge.Top => new PanelShift(0, -Travel),
            BarEdge.Bottom => new PanelShift(0, Travel),
            BarEdge.Left => new PanelShift(-Travel, 0),
            BarEdge.Right => new PanelShift(Travel, 0),
            _ => default,
        };
    }

    /// <summary>
    /// Portion of the entrance at <paramref name="time"/>. Zero is the start and one is resting.
    /// The curve is cubic-bezier(0.1, 0.9, 0.2, 1), so a quarter of the time has already covered most of the slide.
    /// </summary>
    /// <param name="time">Zero at the start and one at the end. Values outside that range clamp.</param>
    /// <returns>How far along the entrance is.</returns>
    public static double EnterProgress(double time) => MotionCurve.Decelerate.Progress(time);

    /// <summary>
    /// Portion of the exit at <paramref name="time"/>. Zero is resting and one is back at the bar.
    /// The panel is still near the start at the halfway mark.
    /// </summary>
    /// <param name="time">Zero at the start and one at the end. Values outside that range clamp.</param>
    /// <returns>How far along the exit is.</returns>
    public static double LeaveProgress(double time) => 1 - MotionCurve.Decelerate.Progress(1 - Clamp(time));

    /// <summary>
    /// The window for a card that rests at <paramref name="rest"/>.
    /// <paramref name="travel"/> pixels of the window lie under <paramref name="edge"/>.
    /// Zero travel is the card alone, with no starting translation.
    /// </summary>
    /// <param name="edge">Screen edge the bar occupies.</param>
    /// <param name="rest">The card's top-left when it has finished sliding.</param>
    /// <param name="width">Card width.</param>
    /// <param name="height">Card height.</param>
    /// <param name="travel">Pixels tucked under the bar. Zero when the card should not move.</param>
    /// <returns>The window rectangle and the card's starting translation.</returns>
    public static PanelShell Tuck(BarEdge edge, PanelOrigin rest, int width, int height, int travel)
    {
        var cardWidth = Math.Max(0, width);
        var cardHeight = Math.Max(0, height);
        if (travel <= 0)
        {
            return new PanelShell(rest.X, rest.Y, cardWidth, cardHeight, 0, 0);
        }

        return edge switch
        {
            BarEdge.Top => new PanelShell(rest.X, rest.Y - travel, cardWidth, cardHeight + travel, 0, -travel),
            BarEdge.Bottom => new PanelShell(rest.X, rest.Y, cardWidth, cardHeight + travel, 0, travel),
            BarEdge.Left => new PanelShell(rest.X - travel, rest.Y, cardWidth + travel, cardHeight, -travel, 0),
            BarEdge.Right => new PanelShell(rest.X, rest.Y, cardWidth + travel, cardHeight, travel, 0),
            _ => new PanelShell(rest.X, rest.Y, cardWidth, cardHeight, 0, 0),
        };
    }

    /// <summary>The point <paramref name="progress"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The point at progress zero.</param>
    /// <param name="to">The point at progress one.</param>
    /// <param name="progress">Zero returns <paramref name="from"/>. One returns <paramref name="to"/>. Values outside that range clamp.</param>
    /// <returns>The interpolated point, in the same pixel space as the arguments.</returns>
    public static PanelOrigin Along(PanelOrigin from, PanelOrigin to, double progress)
    {
        var t = Clamp(progress);
        return new PanelOrigin(
            (int)Math.Round(from.X + ((to.X - from.X) * t)),
            (int)Math.Round(from.Y + ((to.Y - from.Y) * t)));
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
