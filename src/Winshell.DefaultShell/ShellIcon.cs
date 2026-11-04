using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// An icon for a task button. This asks the Windows shell for the program path or packaged app id and keeps the
/// bitmap for WinUI.
/// </summary>
internal static partial class ShellIcon
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The path the shell reads. A packaged app id contains <c>!</c> and is the Start icon for that id. A program
    /// path, or a path that already starts with <c>shell:</c>, is read as it is. A <c>file://</c> URI, a
    /// <c>%VAR%</c> and the escapes in the path are expanded first, and anything from <c>?</c> on is left out.
    /// </summary>
    public static string ShellPath(string icon)
    {
        if (icon.Length == 0)
            return icon;
        // "name?path=..." names the icon; the query is not part of the path.
        var query = icon.IndexOf('?');
        var name = query >= 0 ? icon[..query] : icon;
        var path = Expand(name);
        if (path.Length == 0 || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return path;
        if (path.Contains('!') && !Tasks.IsProgramPath(path))
            return @"shell:AppsFolder\" + path;
        return path;
    }

    private static string Expand(string name) =>
        Environment.ExpandEnvironmentVariables(Uri.UnescapeDataString(name).Replace("file://", "", StringComparison.OrdinalIgnoreCase).TrimStart('/'));

    /// <summary>A size in logical pixels, in pixels on the screen that holds <paramref name="root"/>.</summary>
    public static int Pixels(XamlRoot? root, int size) =>
        Math.Max(1, (int)Math.Ceiling(size * Math.Max(root?.RasterizationScale ?? 1, 1)));

    /// <summary>The icon at <paramref name="size"/> pixels, or null when Windows has none.</summary>
    public static ImageSource? Load(string icon, int size)
    {
        var path = ShellPath(icon);
        if (path.Length == 0 || size < 1)
            return null;
        var key = size + "|" + path;
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var loaded = LoadUncached(path, size);
        Cache[key] = loaded;
        return loaded;
    }

    private static ImageSource? LoadUncached(string path, int size)
    {
        try
        {
            SHCreateItemFromParsingName(path, 0, typeof(IShellItemImageFactory).GUID, out var factory);
            // SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK | SIIGBF_SCALEUP.
            if (factory.GetImage(new SIZE { Cx = size, Cy = size }, 0x4 | 0x1 | 0x100, out var bitmap) != 0 || bitmap == 0)
                return null;
            try
            {
                return BitmapOf(bitmap);
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private static WriteableBitmap? BitmapOf(nint bitmap)
    {
        if (GetObjectW(bitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0 || info.bmBits == 0 || info.bmBitsPixel != 32)
            return null;
        var width = info.bmWidth;
        var height = Math.Abs(info.bmHeight);
        if (width <= 0 || height <= 0)
            return null;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        unsafe
        {
            var source = (byte*)info.bmBits;
            fixed (byte* destination = pixels)
            {
                for (var y = 0; y < height; y++)
                {
                    var from = info.bmHeight > 0 ? height - 1 - y : y;
                    Buffer.MemoryCopy(source + from * info.bmWidthBytes, destination + y * stride, stride, Math.Min(stride, info.bmWidthBytes));
                }
            }
        }
        var image = new WriteableBitmap(width, height);
        using (var stream = image.PixelBuffer.AsStream())
            stream.Write(pixels);
        image.Invalidate();
        return image;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, out BITMAP bitmap);
}
