namespace Winshell.Core;

/// <summary>The angle of a bar's overflow chevron.</summary>
public static class BarChevron
{
    /// <summary>
    /// Angle of an up-chevron, in degrees. Zero points up.
    /// Closed, the chevron points at the panel. Open, it has turned halfway around.
    /// </summary>
    /// <param name="edge">Screen edge the bar occupies.</param>
    /// <param name="open">True while the overflow panel is open.</param>
    /// <returns>The angle.</returns>
    public static double Angle(BarEdge edge, bool open)
    {
        var closed = edge switch
        {
            BarEdge.Bottom => 0,
            BarEdge.Left => 90,
            BarEdge.Right => 270,
            _ => 180,
        };
        return closed + (open ? 180 : 0);
    }
}
