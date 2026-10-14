namespace Winshell.Core;

/// <summary>Which screen edge an empty-bar drag selects.</summary>
public static class BarEdgeDrag
{
    /// <summary>Travel, in physical pixels, before a drag picks an edge.</summary>
    public const int Threshold = 48;

    /// <summary>
    /// The edge a drag from <paramref name="pressX"/>, <paramref name="pressY"/> to
    /// <paramref name="releaseX"/>, <paramref name="releaseY"/> is heading toward.
    /// The longer axis of travel wins, and the release has to land on that half of <paramref name="screen"/>.
    /// A drag shorter than <see cref="Threshold"/> on both axes returns null.
    /// </summary>
    /// <param name="pressX">Press, in physical pixels.</param>
    /// <param name="pressY">Press, in physical pixels.</param>
    /// <param name="releaseX">Release, in physical pixels.</param>
    /// <param name="releaseY">Release, in physical pixels.</param>
    /// <param name="screen">The monitor that contains the bar.</param>
    /// <returns>The edge to write, or null when the drag stays put.</returns>
    public static BarEdge? Choose(int pressX, int pressY, int releaseX, int releaseY, PixelRect screen)
    {
        var dx = releaseX - pressX;
        var dy = releaseY - pressY;
        if (Math.Abs(dx) < Threshold && Math.Abs(dy) < Threshold)
        {
            return null;
        }

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            var mid = screen.X + (screen.Width / 2);
            if (dx < 0 && releaseX < mid)
            {
                return BarEdge.Left;
            }

            if (dx > 0 && releaseX >= mid)
            {
                return BarEdge.Right;
            }

            return null;
        }

        var middle = screen.Y + (screen.Height / 2);
        if (dy < 0 && releaseY < middle)
        {
            return BarEdge.Top;
        }

        if (dy > 0 && releaseY >= middle)
        {
            return BarEdge.Bottom;
        }

        return null;
    }
}
