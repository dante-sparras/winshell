using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>An icon's pixels as a WinUI bitmap, which is premultiplied. A tray icon is straight alpha.</summary>
internal static class TrayImage
{
    public static WriteableBitmap? Bitmap(PixelImage? image)
    {
        if (image is null)
            return null;
        var pixels = new byte[image.Bgra.Length];
        image.Bgra.CopyTo(pixels);
        if (!image.Premultiplied)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var alpha = pixels[i + 3];
                if (alpha == 255)
                    continue;
                pixels[i] = (byte)((pixels[i] * alpha + 127) / 255);
                pixels[i + 1] = (byte)((pixels[i + 1] * alpha + 127) / 255);
                pixels[i + 2] = (byte)((pixels[i + 2] * alpha + 127) / 255);
            }
        }
        var bitmap = new WriteableBitmap(image.Width, image.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels);
        bitmap.Invalidate();
        return bitmap;
    }
}
