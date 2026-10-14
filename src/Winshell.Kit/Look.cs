namespace Winshell.Kit;

/// <summary>
/// The sizes the Kit draws with, in device-independent pixels. The product passes the Host look's computed sizes to <see cref="Use"/>.
/// Controls built after <see cref="Use"/> take the new sizes. Before that, the sizes are the Windows ones: body 14, caption 12, corner 8,
/// control corner 4, gutter 16.
/// </summary>
public static class Look
{
    private static Sizes _current = new(14, 12, 8, 4, 16);

    /// <summary>The sizes in use.</summary>
    public static Sizes Current => _current;

    /// <summary>Draws later chrome at these sizes.</summary>
    /// <param name="bodySize">Primary text.</param>
    /// <param name="captionSize">Secondary text.</param>
    /// <param name="cornerSize">A panel corner.</param>
    /// <param name="controlCorner">A button or a row.</param>
    /// <param name="gutterSize">Space between a panel's edge and its content.</param>
    public static void Use(double bodySize, double captionSize, double cornerSize, double controlCorner, double gutterSize) =>
        _current = new(bodySize, captionSize, cornerSize, controlCorner, gutterSize);

    /// <summary>The five sizes <see cref="Look"/> draws with.</summary>
    /// <param name="BodySize">Primary text.</param>
    /// <param name="CaptionSize">Secondary text.</param>
    /// <param name="CornerSize">A panel corner.</param>
    /// <param name="ControlCorner">A button or a row.</param>
    /// <param name="GutterSize">Space between a panel's edge and its content.</param>
    public readonly record struct Sizes(double BodySize, double CaptionSize, double CornerSize, double ControlCorner, double GutterSize);
}
