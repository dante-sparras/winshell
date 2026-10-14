using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell.Kit;

/// <summary>
/// A weather condition at any size, for the forecast. It is the bar's mark (<see cref="WeatherMarks"/>) scaled up,
/// so the panel and the bar show one drawing: the Fluent sun, moon, or cloud, with thin strokes under the cloud.
/// The caller names the glyph and the tooltip. The Kit does not know a weather code.
/// </summary>
public static class WeatherIcons
{
    /// <summary>The icon for <paramref name="glyph"/> in a square of <paramref name="size"/>, with <paramref name="tooltip"/> as its name.</summary>
    /// <param name="glyph">A weather glyph name, such as <c>weather-sun</c>.</param>
    /// <param name="tooltip">The name shown when the pointer rests on the icon.</param>
    /// <param name="size">The square's edge in device-independent pixels.</param>
    /// <param name="ink">The brush it draws in. Null draws in the foreground.</param>
    /// <returns>The icon.</returns>
    public static FrameworkElement Element(string glyph, string tooltip, double size, Brush? ink = null)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(tooltip);
        var mark = new Grid { Width = WeatherMarks.Size, Height = WeatherMarks.Size };
        WeatherMarks.Fill(mark, glyph, ink ?? Paint.Foreground);
        var icon = new Viewbox
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = mark,
        };
        ToolTipService.SetToolTip(icon, tooltip);
        return icon;
    }
}
