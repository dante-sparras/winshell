using System.Security.Cryptography;

namespace Winshell.Media;

/// <summary>
/// Track art from Windows comes as image data, not a url. It is kept in <see cref="TempImageStore"/>, named by its
/// content, so the same art is one file. The url is the use-count key this caller holds: each one returned is
/// released once.
/// </summary>
internal static class ArtCache
{
    private static readonly TempImageStore Images = new(Path.Combine(Path.GetTempPath(), "winshell", "media-art"));

    /// <summary>
    /// Reads art another app hands over and stores it, or returns null when it is not a picture within the store's
    /// limits. Empty art is an empty url.
    /// </summary>
    public static async Task<string?> StoreAsync(Stream input)
    {
        var data = await Images.ReadAsync(input).ConfigureAwait(false);
        if (data is null)
            return null;
        if (data.Length == 0)
            return "";
        return Images.Store(Key(data), data);
    }

    /// <summary>Gives up one use of an art url, and deletes the file when nothing uses it.</summary>
    public static void Release(string url)
    {
        if (TryKey(url, out var key))
            Images.Release(key);
    }

    /// <summary>
    /// <see cref="Release"/> on the thread pool, for the UI thread: the store's lock is held while art is written,
    /// and the last release deletes the file.
    /// </summary>
    public static void ReleaseLater(string url)
    {
        if (url.Length > 0)
            _ = Task.Run(() => Release(url));
    }

    /// <summary>Deletes art left behind by earlier runs, and their folders once empty.</summary>
    public static void CleanOld()
    {
        Images.CleanOld();
    }

    private static string Key(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant();
    }

    private static bool TryKey(string url, out string key)
    {
        key = "";
        if (url.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile)
            return false;
        key = Path.GetFileNameWithoutExtension(uri.LocalPath);
        return key.Length > 0;
    }
}
