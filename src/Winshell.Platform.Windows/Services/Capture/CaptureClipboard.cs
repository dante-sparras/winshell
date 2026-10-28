using System.ComponentModel;
using System.Runtime.InteropServices;
using SkiaSharp;
using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows.Services.Capture;

internal static partial class CaptureClipboard
{
    /// <summary>A top-down BITMAPV5HEADER followed by straight-alpha BGRA pixels, including explicit channel masks.</summary>
    internal static byte[] EncodeDib(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            throw new InvalidOperationException("Cannot read capture pixels.");
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
        var pixels = new byte[length];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, length);
        writer.Write(pixels);
        return stream.ToArray();
    }

    public static void Copy(SKImage image, nint owner)
    {
        var dib = EncodeDib(image);
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)dib.Length);
        if (memory == 0)
            throw new Win32Exception();
        try
        {
            var pointer = Win32.GlobalLock(memory);
            if (pointer == 0)
                throw new Win32Exception();
            try { Marshal.Copy(dib, 0, pointer, dib.Length); }
            finally { Win32.GlobalUnlock(memory); }
            if (!Win32.OpenClipboard(owner))
                throw new Win32Exception();
            try
            {
                if (!Win32.EmptyClipboard() || Win32.SetClipboardData(17, memory) == 0) // CF_DIBV5
                    throw new Win32Exception();
                memory = 0; // The clipboard owns the allocation now.
            }
            finally { Win32.CloseClipboard(); }
        }
        finally
        {
            if (memory != 0)
                GlobalFree(memory);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}
