namespace Winshell.Core;

/// <summary>
/// A cubic-bezier easing from (0, 0) to (1, 1), written the way CSS and the Windows motion guidance write it.
/// </summary>
/// <param name="X1">First control point, time.</param>
/// <param name="Y1">First control point, progress.</param>
/// <param name="X2">Second control point, time.</param>
/// <param name="Y2">Second control point, progress.</param>
public readonly record struct MotionCurve(double X1, double Y1, double X2, double Y2)
{
    /// <summary>The Windows flyout entrance, cubic-bezier(0.1, 0.9, 0.2, 1). Most of the travel happens early, then it settles.</summary>
    public static MotionCurve Decelerate { get; } = new(0.1, 0.9, 0.2, 1);

    /// <summary>Ease-out cubic, cubic-bezier(0.33, 1, 0.68, 1). A short turn or fade that should not linger.</summary>
    public static MotionCurve EaseOut { get; } = new(0.33, 1, 0.68, 1);

    /// <summary>The Windows exit, cubic-bezier(0.7, 0, 1, 0.5). It starts slowly and leaves at speed, such as a toast sliding off the screen.</summary>
    public static MotionCurve Accelerate { get; } = new(0.7, 0, 1, 0.5);

    /// <summary>
    /// Portion of the motion at <paramref name="time"/>. Zero is the start and one is the end.
    /// </summary>
    /// <param name="time">Zero at the start and one at the end. Values outside that range clamp.</param>
    /// <returns>How far along the motion is.</returns>
    public double Progress(double time)
    {
        var target = Clamp(time);
        if (target is 0 or 1)
        {
            return target;
        }

        var u = target;
        for (var i = 0; i < 8; i++)
        {
            var x = Sample(u, X1, X2) - target;
            if (Math.Abs(x) < 1e-6)
            {
                break;
            }

            var slope = Slope(u, X1, X2);
            if (Math.Abs(slope) < 1e-6)
            {
                break;
            }

            u = Clamp(u - (x / slope));
        }

        return Sample(u, Y1, Y2);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    /// <summary>Cubic bezier from 0 to 1 with control points <paramref name="p1"/> and <paramref name="p2"/>.</summary>
    private static double Sample(double u, double p1, double p2)
    {
        var one = 1 - u;
        return (3 * one * one * u * p1) + (3 * one * u * u * p2) + (u * u * u);
    }

    private static double Slope(double u, double p1, double p2)
    {
        var one = 1 - u;
        return (3 * one * one * p1) + (6 * one * u * (p2 - p1)) + (3 * u * u * (1 - p2));
    }
}
