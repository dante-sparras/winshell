namespace Winshell.Core;

/// <summary>
/// The hairline Windows 11 draws on a window. Dark coverage was sampled from the desktop
/// window manager over black, where the edge is white at 47/255. Light uses the same coverage
/// in black, so a light window darkens by the same step. Only the side that faces the desktop
/// is drawn.
/// </summary>
/// <param name="A">Alpha, 0 through 255.</param>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="Left">Left thickness in device-independent pixels.</param>
/// <param name="Top">Top thickness in device-independent pixels.</param>
/// <param name="Right">Right thickness in device-independent pixels.</param>
/// <param name="Bottom">Bottom thickness in device-independent pixels.</param>
public readonly record struct WindowBorder(
    byte A,
    byte R,
    byte G,
    byte B,
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    private const byte Coverage = 47;

    /// <summary>
    /// The desktop-facing edge of a bar. <paramref name="pixels"/> is the physical thickness
    /// Windows reports for a window border. It becomes device-independent pixels at <paramref name="scale"/>.
    /// A missing thickness or scale is one device-independent pixel. High contrast uses
    /// <paramref name="contrastR"/>, <paramref name="contrastG"/>, and <paramref name="contrastB"/> opaque.
    /// </summary>
    /// <param name="light">True for the light system theme.</param>
    /// <param name="highContrast">True when a high contrast theme is active.</param>
    /// <param name="contrastR">High contrast red.</param>
    /// <param name="contrastG">High contrast green.</param>
    /// <param name="contrastB">High contrast blue.</param>
    /// <param name="edge">Screen edge the bar occupies.</param>
    /// <param name="pixels">Physical pixels of the window border. Zero when Windows does not say.</param>
    /// <param name="scale">Physical pixels per device-independent pixel.</param>
    /// <returns>The color and the one side to stroke.</returns>
    public static WindowBorder For(
        bool light,
        bool highContrast,
        byte contrastR,
        byte contrastG,
        byte contrastB,
        BarEdge edge,
        int pixels,
        double scale)
    {
        var dip = pixels > 0 && scale > 0 ? pixels / scale : 1;
        var (left, top, right, bottom) = edge switch
        {
            BarEdge.Bottom => (0d, dip, 0d, 0d),
            BarEdge.Left => (0d, 0d, dip, 0d),
            BarEdge.Right => (dip, 0d, 0d, 0d),
            _ => (0d, 0d, 0d, dip),
        };

        if (highContrast)
        {
            return new WindowBorder(255, contrastR, contrastG, contrastB, left, top, right, bottom);
        }

        return light
            ? new WindowBorder(Coverage, 0, 0, 0, left, top, right, bottom)
            : new WindowBorder(Coverage, 255, 255, 255, left, top, right, bottom);
    }
}
