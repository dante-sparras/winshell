namespace Winshell.Core;

/// <summary>
/// The screen edge as a state push names it.
/// <see cref="BarEdge"/> is the value settings store. The push carries a word, and Winland insets from that.
/// </summary>
public static class ShellEdge
{
    /// <summary>The word a state push carries: <c>top</c>, <c>bottom</c>, <c>left</c>, or <c>right</c>.</summary>
    /// <param name="edge">The bar's edge.</param>
    /// <returns>The word.</returns>
    public static string Name(BarEdge edge) => edge switch
    {
        BarEdge.Bottom => "bottom",
        BarEdge.Left => "left",
        BarEdge.Right => "right",
        _ => "top",
    };
}
