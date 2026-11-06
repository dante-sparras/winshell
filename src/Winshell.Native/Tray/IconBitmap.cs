using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>Draws icons into straight-alpha BGRA pixels, and reads the resource an icon was loaded from.</summary>
internal static unsafe class IconBitmap
{
    /// <summary>The module an icon was loaded from, and its resource: a numeric id, or a name when the id is 0.</summary>
    internal readonly record struct Source(string Module, ushort Id, string Name);

    /// <summary>Where Windows says the icon was loaded from, or null for an icon handle it does not know.</summary>
    internal static Source? SourceOf(nint icon)
    {
        var info = new ICONINFOEXW { cbSize = (uint)sizeof(ICONINFOEXW) };
        if (icon == 0 || !PInvoke.GetIconInfoEx((HICON)icon, &info))
            return null;
        DeleteBitmaps(info.hbmColor, info.hbmMask);
        return new Source(info.szModName.ToString(), info.wResID, info.szResName.ToString());
    }

    /// <summary>
    /// Draws an icon into <paramref name="size"/> by <paramref name="size"/> pixels, or at its own size, between 16
    /// and 256, when <paramref name="size"/> is 0 or less. Null when the icon cannot be drawn.
    /// </summary>
    internal static (int Size, byte[] Bgra)? Draw(nint icon, int size)
    {
        ICONINFO info;
        if (icon == 0 || !PInvoke.GetIconInfo((HICON)icon, &info))
            return null;
        try
        {
            if (size <= 0)
            {
                size = 32;
                if (Win32.GetObjectW(info.hbmColor != default ? info.hbmColor : info.hbmMask, sizeof(BITMAP),
                        out var bmp) != 0)
                    size = Math.Clamp(bmp.bmWidth, 16, 256);
            }

            return Pixels((HICON)icon, size) is { } bgra ? (size, bgra) : null;
        }
        finally
        {
            DeleteBitmaps(info.hbmColor, info.hbmMask);
        }
    }

    private static byte[]? Pixels(HICON icon, int size)
    {
        var header = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32
            }
        };
        var dc = PInvoke.CreateCompatibleDC(default);
        if (dc == default)
            return null;
        HBITMAP dib = default;
        try
        {
            void* bits;
            dib = PInvoke.CreateDIBSection(dc, &header, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
            if (dib == default)
                return null;
            var old = PInvoke.SelectObject(dc, dib);
            PInvoke.DrawIconEx(dc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_NORMAL);
            var pixels = new Span<uint>(bits, size * size);
            var hasAlpha = false;
            foreach (var p in pixels)
                if ((p & 0xff000000) != 0)
                {
                    hasAlpha = true;
                    break;
                }

            if (!hasAlpha)
            {
                // An old icon: draw its mask, which is black where the icon is opaque.
                var mask = new uint[size * size];
                PInvoke.DrawIconEx(dc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_MASK);
                pixels.CopyTo(mask);
                PInvoke.DrawIconEx(dc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_NORMAL);
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = (mask[i] & 0xffffff) == 0 ? pixels[i] | 0xff000000 : 0;
            }

            var bgra = new Span<byte>(bits, size * size * 4).ToArray();
            PInvoke.SelectObject(dc, old);
            return bgra;
        }
        finally
        {
            PInvoke.DeleteDC(dc);
            if (dib != default)
                Win32.DeleteObject(dib);
        }
    }

    private static void DeleteBitmaps(HBITMAP color, HBITMAP mask)
    {
        if (color != default)
            Win32.DeleteObject(color);
        if (mask != default)
            Win32.DeleteObject(mask);
    }
}
