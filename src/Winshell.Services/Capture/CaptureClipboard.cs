using Winshell.Native;

namespace Winshell.Capture;

internal static class CaptureClipboard
{
    /// <summary>A top-down BITMAPV5HEADER followed by straight-alpha BGRA pixels, including explicit channel masks.</summary>
    internal static byte[] EncodeDib(PixelImage image)
    {
        var length = checked(image.Width * image.Height * 4);
        using var stream = new MemoryStream(checked(124 + length));
        using var writer = new BinaryWriter(stream);
        writer.Write(124);
        writer.Write(image.Width);
        writer.Write(-image.Height);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(3); // BI_BITFIELDS
        writer.Write(length);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(0x00ff0000u); writer.Write(0x0000ff00u); writer.Write(0x000000ffu); writer.Write(0xff000000u);
        writer.Write(0x73524742u); // LCS_sRGB
        writer.Write(new byte[36]); // CIEXYZTRIPLE
        writer.Write(0); writer.Write(0); writer.Write(0); // gamma
        writer.Write(4); // LCS_GM_IMAGES
        writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(image.StraightBgra());
        return stream.ToArray();
    }

    public static void Copy(PixelImage image, nint owner) => ClipboardImage.SetDibV5(EncodeDib(image), owner);
}
