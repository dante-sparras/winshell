using Winshell.Native;

namespace Winshell;

/// <summary>The icons the Windows shell shows for programs, files and packaged apps.</summary>
public static class ShellImage
{
    /// <summary>Reads the icon the Windows shell shows for an item. Blocks while the shell reads it.</summary>
    /// <param name="shellPath">
    /// A program or file path, or a shell path such as <c>shell:AppsFolder\</c> followed by a packaged app id.
    /// </param>
    /// <param name="size">The width and height asked for, in pixels. The icon can be larger; a smaller one is scaled up.</param>
    /// <returns>The icon, premultiplied, or null when the shell has none for the item.</returns>
    public static PixelImage? Load(string shellPath, int size)
    {
        ArgumentNullException.ThrowIfNull(shellPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        return ShellItemImage.Icon(shellPath, size) is { } icon
            ? new PixelImage(icon.Width, icon.Height, icon.Bgra, true)
            : null;
    }
}
