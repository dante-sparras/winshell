using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>
/// DWM's live previews of one window drawn into another. A thumbnail handle belongs to whoever registered it, which
/// unregisters it once.
/// </summary>
internal static class DwmThumbnails
{
    /// <summary>Draws <paramref name="source"/> into <paramref name="destination"/>. The handle, or 0 when DWM refused.</summary>
    internal static nint Register(nint destination, nint source)
    {
        return PInvoke.DwmRegisterThumbnail((HWND)destination, (HWND)source, out var thumbnail).Succeeded
            ? thumbnail
            : 0;
    }

    /// <summary>The source window's size in pixels. False when DWM does not say.</summary>
    internal static bool TrySourceSize(nint thumbnail, out int width, out int height)
    {
        var ok = PInvoke.DwmQueryThumbnailSourceSize(thumbnail, out var size).Succeeded;
        (width, height) = ok ? (size.cx, size.cy) : (0, 0);
        return ok;
    }

    /// <summary>Where the thumbnail draws, which part of the source, how opaque, and whether it shows at all.</summary>
    internal static bool Update(nint thumbnail, RECT destination, RECT source, byte opacity, bool visible)
    {
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            Flags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_RECTSOURCE | PInvoke.DWM_TNP_OPACITY |
                    PInvoke.DWM_TNP_VISIBLE,
            Destination = destination,
            Source = source,
            Opacity = opacity,
            Visible = visible ? 1 : 0
        };
        return Win32.DwmUpdateThumbnailProperties(thumbnail, properties) == 0;
    }

    internal static void Unregister(nint thumbnail)
    {
        _ = PInvoke.DwmUnregisterThumbnail(thumbnail);
    }
}
