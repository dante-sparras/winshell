namespace Winshell.Core;

/// <summary>A rectangle in physical pixels.</summary>
/// <param name="X">Left.</param>
/// <param name="Y">Top.</param>
/// <param name="Width">Width. Zero is empty.</param>
/// <param name="Height">Height. Zero is empty.</param>
public readonly record struct ShellRect(int X, int Y, int Width, int Height);

/// <summary>The strip a bar occupies on one monitor. Thickness is physical pixels.</summary>
public static class BarStrip
{
    /// <summary>Top and bottom bar height, in device-independent pixels. The Windows 11 taskbar is 48.</summary>
    public const int ThicknessDips = 48;

    /// <summary>
    /// The strip <paramref name="edge"/> takes from <paramref name="screen"/>.
    /// A thickness of zero leaves an empty strip on that edge. A thickness past the screen leaves the rest of the screen one pixel.
    /// </summary>
    /// <param name="screen">One monitor, whole, in physical pixels.</param>
    /// <param name="edge">The edge the bar occupies.</param>
    /// <param name="thickness">Bar thickness in physical pixels.</param>
    /// <returns>The strip.</returns>
    public static ShellRect Place(ShellRect screen, BarEdge edge, int thickness)
    {
        var width = Math.Max(1, screen.Width);
        var height = Math.Max(1, screen.Height);
        var horizontal = edge is BarEdge.Left or BarEdge.Right;
        var room = horizontal ? width : height;
        var taken = thickness <= 0 ? 0 : Math.Min(thickness, room - 1);
        return edge switch
        {
            BarEdge.Left => new ShellRect(screen.X, screen.Y, taken, height),
            BarEdge.Right => new ShellRect(screen.X + width - taken, screen.Y, taken, height),
            BarEdge.Bottom => new ShellRect(screen.X, screen.Y + height - taken, width, taken),
            _ => new ShellRect(screen.X, screen.Y, width, taken),
        };
    }
}
