using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Winshell.Native;

/// <summary>
/// The items the Windows shell shows, found by parsing name: programs, files and packaged apps, such as
/// <c>shell:AppsFolder\{AppUserModelID}</c>.
/// </summary>
internal static unsafe partial class ShellItems
{
    private const SIIGBF IconFlags = SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_SCALEUP;

    /// <summary>The name the shell shows for the item, or null when it has no such item.</summary>
    internal static string? Name(string parsingName)
    {
        return Use<IShellItem, string?>(parsingName, null,
            item => item.GetDisplayName(0, out var name) == 0 ? name : null);
    }

    /// <summary>
    /// The item's icon at <paramref name="size"/> by <paramref name="size"/> pixels or larger, as premultiplied BGRA
    /// with the top row first. Null when the shell has no such item or no 32-bit icon for it.
    /// </summary>
    internal static (int Width, int Height, byte[] Bgra)? Icon(string parsingName, int size)
    {
        return Use<IShellItemImageFactory, (int, int, byte[])?>(parsingName, null, factory =>
        {
            var result = factory.GetImage(new SIZE(size, size), IconFlags, out var bitmap);
            if (bitmap == 0)
                return null;
            try
            {
                return result == 0 ? Pixels(bitmap) : null;
            }
            finally
            {
                Win32.DeleteObject(bitmap);
            }
        });
    }

    /// <summary>
    /// Creates the item as <typeparamref name="T"/>, passes it to <paramref name="use"/> and releases it.
    /// <paramref name="missing"/> when the shell has no such item.
    /// </summary>
    private static TResult Use<T, TResult>(string parsingName, TResult missing, Func<T, TResult> use) where T : class
    {
        if (SHCreateItemFromParsingName(parsingName, 0, typeof(T).GUID, out var pointer) < 0 || pointer == 0)
            return missing;
        T? item = null;
        try
        {
            item = (T)Marshal.GetObjectForIUnknown(pointer);
            return use(item);
        }
        finally
        {
            if (item is not null)
                Marshal.ReleaseComObject(item);
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
            new ReadOnlySpan<byte>((byte*)info.bmBits + (long)from * info.bmWidthBytes, row).CopyTo(
                pixels.AsSpan(y * stride, row));
        }

        return (width, height, pixels);
    }

    /// <summary>
    /// Handwritten: CsWin32's declaration takes an <c>IBindCtx</c>, whose generated monikers use the banned
    /// <c>System.Runtime.InteropServices.ComTypes.FILETIME</c>.
    /// </summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out nint bitmap);
    }
}

/// <summary>IShellItem. Only <c>GetDisplayName</c> is used; the two methods before it keep it on its vtable slot.</summary>
[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler();

    void GetParent();

    [PreserveSig]
    int GetDisplayName(int sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
}
