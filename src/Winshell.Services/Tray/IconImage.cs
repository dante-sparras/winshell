using Winshell.Native;

namespace Winshell.Tray;

/// <summary>Draws icons into straight-alpha images: from an icon handle, or from the resource it was loaded from.</summary>
internal static class IconImage
{
    /// <summary>Where the icon was loaded from, or null for an icon the app drew or copied.</summary>
    public static IconResource? ResourceOf(nint icon)
    {
        return IconBitmap.SourceOf(icon) is { } source && source.Module.Length > 0 &&
               (source.Id != 0 || source.Name.Length > 0)
            ? new IconResource(source.Module, source.Id, source.Name)
            : null;
    }

    /// <summary>
    /// Draws an icon from its resource at a size in pixels, so it is sharp at any scale. Null when the module or the
    /// resource is gone or is not on a local drive.
    /// </summary>
    public static PixelImage? FromResource(IconResource resource, int size)
    {
        if (!IsLocalPath(resource.Module))
            return null;
        using var icon = OwnedIcon.Load(resource.Module, resource.Id, resource.Name, size);
        return icon is null ? null : FromHandle(icon.Handle, size);
    }

    /// <summary>
    /// False for a network path, which would open a connection, and for device paths. Among the paths that start with
    /// two separators only \\?\C:\ is local.
    /// </summary>
    public static bool IsLocalPath(string path)
    {
        static bool Separator(char c)
        {
            return c is '\\' or '/';
        }

        if (path.Length < 2 || !Separator(path[0]) || !Separator(path[1]))
            return true;
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 7 && char.IsAsciiLetter(path[4]) &&
               path[5] == ':' && path[6] == '\\';
    }

    /// <summary>Draws an icon into a straight-alpha image, at its own size unless one is given.</summary>
    public static PixelImage? FromHandle(nint icon, int size = 0)
    {
        return IconBitmap.Draw(icon, size) is { } drawn ? new PixelImage(drawn.Size, drawn.Size, drawn.Bgra) : null;
    }
}
