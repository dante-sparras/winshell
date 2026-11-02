using SkiaSharp;

namespace Winshell.Platform.Windows.Services;

/// <summary>Where the QML stack, which draws with Skia, takes a service's pixels.</summary>
internal static class PixelImageSkia
{
    /// <summary>A copy of the picture, which the caller disposes.</summary>
    public static unsafe SKImage ToSkImage(this PixelImage image)
    {
        var alpha = image.Premultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul;
        fixed (byte* pixels = image.Bgra)
            return SKImage.FromPixelCopy(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, alpha), (nint)pixels, image.Width * 4);
    }
}
