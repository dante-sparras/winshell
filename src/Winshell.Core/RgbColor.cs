namespace Winshell.Core;

/// <summary>An opaque sRGB color.</summary>
/// <param name="R">Red, 0 through 255.</param>
/// <param name="G">Green, 0 through 255.</param>
/// <param name="B">Blue, 0 through 255.</param>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Black.</summary>
    public static RgbColor Black => new(0, 0, 0);

    /// <summary>White.</summary>
    public static RgbColor White => new(255, 255, 255);
}
