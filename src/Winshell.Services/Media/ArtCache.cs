using System.Globalization;
using System.Security.Cryptography;

namespace Winshell.Media;

/// <summary>
/// Track art from Windows comes as image data, not a url. It is written to a file in the temp folder, named by
/// its content, so the same art is one file, and handed out as a <c>file:///</c> url. Each process has a folder
/// of its own: an instance deletes the art it no longer uses, which must not be art another instance shows.
/// </summary>
internal static class ArtCache
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "winshell", "media-art");

    private static readonly string Folder =
        Path.Combine(Root, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    private static readonly Dictionary<string, int> Uses = [];
    private static readonly Lock Gate = new();

    /// <summary>The most art an app can hand over, in bytes.</summary>
    internal const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The most pixels art can decode to.</summary>
    internal const long MaxPixels = 4096 * 4096;

    /// <summary>
    /// Reads art another app hands over and stores it, or returns null when it is not a PNG, JPEG, GIF, BMP or
    /// WebP picture within <see cref="MaxBytes"/> and <see cref="MaxPixels"/>. That leaves out SVG, which the
    /// shell would render at whatever size it asks for.
    /// </summary>
    public static async Task<string?> StoreAsync(Stream input, string? contentType)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > MaxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }

        var data = bytes.ToArray();
        return data.Length == 0 || IsBitmap(data) ? Store(data, contentType) : null;
    }

    private static bool IsBitmap(byte[] data)
    {
        return ImageHeader.ReadSize(data) is (> 0 and var width, > 0 and var height) &&
               (ulong)width * height <= MaxPixels;
    }

    /// <summary>Writes the art and returns its url. Each url returned must be released once.</summary>
    public static string Store(byte[] data, string? contentType)
    {
        if (data.Length == 0)
            return "";
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            _ => ".jpg"
        };
        var path = Path.Combine(Folder,
            Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant() + extension);
        lock (Gate)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(path, data);
            }

            var url = new Uri(path).AbsoluteUri;
            Uses[url] = Uses.GetValueOrDefault(url) + 1;
            return url;
        }
    }

    /// <summary>Gives up one use of an art url, and deletes the file when nothing uses it.</summary>
    public static void Release(string url)
    {
        if (url.Length == 0)
            return;
        lock (Gate)
        {
            if (!Uses.TryGetValue(url, out var count))
                return;
            if (count > 1)
            {
                Uses[url] = count - 1;
                return;
            }

            Uses.Remove(url);
            try
            {
                File.Delete(new Uri(url).LocalPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An image loader still has it open; a later start cleans it up.
            }
        }
    }

    /// <summary>
    /// <see cref="Release"/> on the thread pool, for the UI thread: the lock is held while art is written, and
    /// the last release deletes the file.
    /// </summary>
    public static void ReleaseLater(string url)
    {
        if (url.Length > 0)
            _ = Task.Run(() => Release(url));
    }

    /// <summary>Deletes art left behind by earlier runs, and their folders once empty.</summary>
    public static void CleanOld()
    {
        CleanOld(Root);
    }

    internal static void CleanOld(string root)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return;
            var options = new EnumerationOptions
                { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
                if (File.GetLastWriteTimeUtc(file) < TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(-1))
                    File.Delete(file);
            foreach (var dir in Directory.EnumerateDirectories(root, "*",
                         new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
                if (!string.Equals(dir, Folder, StringComparison.OrdinalIgnoreCase) &&
                    !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
