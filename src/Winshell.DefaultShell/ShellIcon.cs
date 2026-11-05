using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// An icon for a task button. This asks the Windows shell for the program path or packaged app id and keeps the
/// bitmap for WinUI.
/// </summary>
internal static class ShellIcon
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
        var loaded = Bitmap(path, size);
        Cache[key] = loaded;
        return loaded;
    }

    /// <summary>The icon as a WinUI bitmap. A bitmap WinUI cannot make is no icon.</summary>
    private static ImageSource? Bitmap(string path, int size)
    {
        try
        {
            return TrayImage.Bitmap(ShellImage.Load(path, size));
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            return null;
        }
    }
}
