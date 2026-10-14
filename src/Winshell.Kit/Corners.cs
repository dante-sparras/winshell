using Microsoft.UI.Xaml;

namespace Winshell.Kit;

/// <summary>Corner radii from the Host look.</summary>
public static class Corners
{
    /// <summary>A button, a row, a chip, or a field.</summary>
    public static CornerRadius Control => new(Look.Current.ControlCorner);

    /// <summary>A panel, a card, or anything else that floats over other content.</summary>
    public static CornerRadius Overlay => new(Look.Current.CornerSize);

    /// <summary>Round corners that make a square of <paramref name="edge"/> a circle, such as a day marked today.</summary>
    /// <param name="edge">The square's edge.</param>
    /// <returns>Half the edge on every corner.</returns>
    public static CornerRadius Circle(double edge) => new(edge / 2);
}
