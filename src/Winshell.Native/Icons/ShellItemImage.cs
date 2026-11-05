using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>The icons the Windows shell shows for its items: programs, files and packaged apps.</summary>
internal static unsafe partial class ShellItemImage
{
    // SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK | SIIGBF_SCALEUP.
    private const int IconFlags = 0x4 | 0x1 | 0x100;

    /// <summary>
    /// The icon for the item <paramref name="parsingName"/> names, at <paramref name="size"/> by
    /// <paramref name="size"/> pixels or larger, as premultiplied BGRA with the top row first. Null when the shell has
    /// no such item or no 32-bit icon for it.
    /// </summary>
    internal static (int Width, int Height, byte[] Bgra)? Icon(string parsingName, int size)
    {
        if (SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItemImageFactory).GUID, out var pointer) < 0 || pointer == 0)
            return null;
        IShellItemImageFactory? factory = null;
        try
        {
            factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(pointer);
            if (factory.GetImage(new SIZE { Cx = size, Cy = size }, IconFlags, out var bitmap) != 0 || bitmap == 0)
                return null;
            try
            {
                return Pixels(bitmap);
            }
            finally
            {
                Win32.DeleteObject(bitmap);
            }
        }
        finally
        {
            if (factory is not null)
                Marshal.ReleaseComObject(factory);
            Marshal.Release(pointer);
        }
    }

    /// <summary>A 32-bit DIB's pixels, top row first, with no gap between rows.</summary>
    private static (int Width, int Height, byte[] Bgra)? Pixels(nint bitmap)
    {
        if (Win32.GetObjectW(bitmap, sizeof(BITMAP), out var info) == 0 || info.bmBits == 0 || info.bmBitsPixel != 32)
            return null;
        var width = info.bmWidth;
        var height = Math.Abs(info.bmHeight);
        if (width <= 0 || height <= 0 || info.bmWidthBytes <= 0)
            return null;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        var row = Math.Min(stride, info.bmWidthBytes);
        for (var y = 0; y < height; y++)
        {
            // A positive height is a bottom-up DIB.
            var from = info.bmHeight > 0 ? height - 1 - y : y;
            new ReadOnlySpan<byte>((byte*)info.bmBits + (long)from * info.bmWidthBytes, row).CopyTo(pixels.AsSpan(y * stride, row));
        }
        return (width, height, pixels);
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Cx;
        public int Cy;
    }

    /// <summary>
    /// Handwritten: CsWin32's declaration takes an <c>IBindCtx</c>, whose generated monikers use the banned
    /// <c>System.Runtime.InteropServices.ComTypes.FILETIME</c>.
    /// </summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
}
