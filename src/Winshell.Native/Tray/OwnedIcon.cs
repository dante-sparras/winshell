using Windows.Win32;
using Windows.Win32.System.LibraryLoader;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winshell.Native;

/// <summary>An icon this process made, destroyed when disposed.</summary>
internal sealed class OwnedIcon : IDisposable
{
    private OwnedIcon(nint handle) => Handle = handle;

    /// <summary>The icon handle, or 0 once disposed.</summary>
    internal nint Handle { get; private set; }

    /// <summary>
    /// Loads an icon from a program's or library's resources at a size in pixels. Windows picks the image in the icon
    /// group closest to the size, and scales it only if none matches. The library is only mapped as data, and only
    /// while the icon loads. Null when the module or the resource is gone.
    /// </summary>
    internal static OwnedIcon? Load(string module, ushort id, string name, int size)
    {
        var library = PInvoke.LoadLibraryEx(module, LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (library == default)
            return null;
        try
        {
            var icon = id != 0
                ? Win32.LoadImageW(library, id, Win32.IMAGE_ICON, size, size, 0)
                : Win32.LoadImageW(library, name, Win32.IMAGE_ICON, size, size, 0);
            return icon == 0 ? null : new OwnedIcon(icon);
        }
        finally
        {
            PInvoke.FreeLibrary(library);
        }
    }

    public void Dispose()
    {
        if (Handle == 0)
            return;
        PInvoke.DestroyIcon((HICON)Handle);
        Handle = 0;
    }
}
