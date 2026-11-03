using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Tray;

/// <summary>An icon resource in a program or library: a numeric id, or a name when the id is 0.</summary>
internal sealed record IconResource(string Module, ushort Id, string Name);

/// <summary>Draws icons into straight-alpha images: from an icon handle, or from the resource it was loaded from.</summary>
internal static unsafe partial class IconImage
{
    /// <summary>Where the icon was loaded from, or null for an icon the app drew or copied.</summary>
    public static IconResource? ResourceOf(nint icon)
    {
        var info = new ICONINFOEXW { cbSize = (uint)sizeof(ICONINFOEXW) };
        if (icon == 0 || !GetIconInfoExW(icon, ref info))
            return null;
        if (info.hbmColor != 0)
            Win32.DeleteObject(info.hbmColor);
        if (info.hbmMask != 0)
            Win32.DeleteObject(info.hbmMask);
        var module = new string(info.szModName);
        var name = new string(info.szResName);
        return module.Length > 0 && (info.wResID != 0 || name.Length > 0) ? new IconResource(module, info.wResID, name) : null;
    }

    /// <summary>
    /// Draws an icon from its resource at a size in pixels, so it is sharp at any scale. Null when the module or the
    /// resource is gone or is not on a local drive. The library is only mapped as data.
    /// </summary>
    public static PixelImage? FromResource(IconResource resource, int size)
    {
        if (!IsLocalPath(resource.Module))
            return null;
        var module = LoadLibraryExW(resource.Module, 0, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module == 0)
            return null;
        try
        {
            // Windows picks the image in the icon group closest to the size, and scales it only if none matches.
            var icon = resource.Id != 0
                ? Win32.LoadImageW(module, resource.Id, IMAGE_ICON, size, size, 0)
                : Win32.LoadImageW(module, resource.Name, IMAGE_ICON, size, size, 0);
            if (icon == 0)
                return null;
            try
            {
                return FromHandle(icon, size);
            }
            finally
            {
                DestroyIcon(icon);
            }
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>
    /// False for a network path, which would open a connection, and for device paths. Among the paths that start with
    /// two separators only \\?\C:\ is local.
    /// </summary>
    public static bool IsLocalPath(string path)
    {
        static bool Separator(char c) => c is '\\' or '/';
        if (path.Length < 2 || !Separator(path[0]) || !Separator(path[1]))
            return true;
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 7 && char.IsAsciiLetter(path[4]) && path[5] == ':' && path[6] == '\\';
    }

    /// <summary>Draws an icon into a straight-alpha image, at its own size unless one is given.</summary>
    public static PixelImage? FromHandle(nint icon, int size = 0)
    {
        if (icon == 0 || !GetIconInfo(icon, out var info))
            return null;
        try
        {
            if (size <= 0)
            {
                size = 32;
                if (Win32.GetObjectW(info.hbmColor != 0 ? info.hbmColor : info.hbmMask, sizeof(BITMAP), out var bmp) != 0)
                    size = Math.Clamp(bmp.bmWidth, 16, 256);
            }
            var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
            var dc = CreateCompatibleDC(0);
            if (dc == 0)
                return null;
            nint dib = 0;
            try
            {
                dib = CreateDIBSection(dc, &header, 0, out var bits, 0, 0);
                if (dib == 0)
                    return null;
                var old = SelectObject(dc, dib);
                DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_NORMAL);
                var pixels = new Span<uint>((void*)bits, size * size);
                var hasAlpha = false;
                foreach (var p in pixels)
                {
                    if ((p & 0xff000000) != 0)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
                if (!hasAlpha)
                {
                    // An old icon: draw its mask, which is black where the icon is opaque.
                    var mask = new uint[size * size];
                    DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_MASK);
                    pixels.CopyTo(mask);
                    DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_NORMAL);
                    for (var i = 0; i < pixels.Length; i++)
                        pixels[i] = (mask[i] & 0xffffff) == 0 ? pixels[i] | 0xff000000 : 0;
                }
                var image = new PixelImage(size, size, new Span<byte>((void*)bits, size * size * 4).ToArray());
                SelectObject(dc, old);
                return image;
            }
            finally
            {
                DeleteDC(dc);
                if (dib != 0)
                    Win32.DeleteObject(dib);
            }
        }
        finally
        {
            if (info.hbmColor != 0)
                Win32.DeleteObject(info.hbmColor);
            if (info.hbmMask != 0)
                Win32.DeleteObject(info.hbmMask);
        }
    }

    private const uint DI_MASK = 0x1, DI_NORMAL = 0x3;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2, LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    private const uint IMAGE_ICON = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFOEXW
    {
        public uint cbSize;
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
        public ushort wResID;
        public fixed char szModName[260];
        public fixed char szResName[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfoExW(nint icon, ref ICONINFOEXW info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
