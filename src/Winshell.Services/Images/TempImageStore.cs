using System.Globalization;

namespace Winshell;

/// <summary>
/// Picture bytes in a temp folder the caller names. The caller chooses the key, and releases every
/// <see cref="Store"/> and <see cref="Retain"/> once; the last release deletes the file. The extension comes from
/// the bytes. Each process has a folder of its own under the root, so one instance's cleanup cannot take a file
/// another instance is showing.
/// </summary>
internal sealed class TempImageStore
{
    /// <summary>The most a caller can hand over, in bytes.</summary>
    internal const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The most pixels a picture can decode to.</summary>
    internal const long MaxPixels = 4096 * 4096;

    private readonly string _root;
    private readonly string _folder;
    private readonly Dictionary<string, Use> _uses = [];
    private readonly Lock _gate = new();

    /// <summary>Pictures kept under <paramref name="root"/>. The directory is created when the first picture is written.</summary>
    public TempImageStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        _folder = Path.Combine(root, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reads at most <see cref="MaxBytes"/>. Null when the stream is longer, and empty when the stream is empty.</summary>
    public byte[]? Read(Stream input, CancellationToken cancellation = default)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return bytes.ToArray();
            if (bytes.Length + read > MaxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }
    }

    /// <summary>The asynchronous <see cref="Read"/>.</summary>
    public async Task<byte[]?> ReadAsync(Stream input, CancellationToken cancellation = default)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false);
            if (read == 0)
                return bytes.ToArray();
            if (bytes.Length + read > MaxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// Stores a picture under <paramref name="key"/> and returns its <c>file:///</c> url. The same key is one file,
    /// and each call is one more use. Empty data returns "". Data that is not a PNG, JPEG, GIF, BMP or WebP within
    /// <see cref="MaxBytes"/> and <see cref="MaxPixels"/> returns null, and so does a write that fails. SVG is left
    /// out: the shell would render it at whatever size it asks for.
    /// </summary>
    public string? Store(string key, ReadOnlySpan<byte> data)
    {
        RequireKey(key);
        if (data.Length == 0)
            return "";
        if (data.Length > MaxBytes || !TryPicture(data, out var extension))
            return null;

        lock (_gate)
        {
            if (_uses.TryGetValue(key, out var use))
            {
                use.Count++;
                return use.Url;
            }

            var path = Path.Combine(_folder, key + extension);
            if (!IsUnderFolder(path) || !TryWrite(path, data))
                return null;
            var url = new Uri(path).AbsoluteUri;
            _uses.Add(key, new Use(url, path));
            return url;
        }
    }

    /// <summary>
    /// <paramref name="uses"/> more uses of a key this store already holds. Null when it does not. Each use is
    /// released once, the same as a <see cref="Store"/>.
    /// </summary>
    public string? Retain(string key, int uses = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uses);
        if (string.IsNullOrEmpty(key))
            return null;
        lock (_gate)
        {
            if (!_uses.TryGetValue(key, out var use))
                return null;
            use.Count += uses;
            return use.Url;
        }
    }

    /// <summary>Gives up one use of <paramref name="key"/>, and deletes the file when nothing uses it.</summary>
    public void Release(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        string? path;
        lock (_gate)
        {
            if (!_uses.TryGetValue(key, out var use))
                return;
            if (use.Count > 1)
            {
                use.Count--;
                return;
            }

            _uses.Remove(key);
            path = use.Path;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An image loader still has it open; a later start cleans it up.
        }
    }

    /// <summary>Deletes pictures older than a day, and empty folders other than this process's. A root that is a link is left alone.</summary>
    public void CleanOld()
    {
        try
        {
            if (!Directory.Exists(_root) || (File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
                return;
            var options = new EnumerationOptions
                { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(_root, "*", options))
                if (File.GetLastWriteTimeUtc(file) < TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(-1))
                    File.Delete(file);
            foreach (var dir in Directory.EnumerateDirectories(_root, "*",
                         new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
                if (!string.Equals(dir, _folder, StringComparison.OrdinalIgnoreCase) &&
                    !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool IsUnderFolder(string path)
    {
        var folder = Path.GetFullPath(_folder + Path.DirectorySeparatorChar);
        return Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryPicture(ReadOnlySpan<byte> data, out string extension)
    {
        if (!ImageHeader.TryRead(data, out extension, out var width, out var height) || width <= 0 || height <= 0)
            return false;
        return (ulong)width * (ulong)height <= (ulong)MaxPixels;
    }

    private static bool TryWrite(string path, ReadOnlySpan<byte> data)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null)
            return false;
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
            }

            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            return false;
        }
    }

    private static void RequireKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key is "." or ".." || Path.GetFileName(key) != key)
            throw new ArgumentException("The key must be one file name.", nameof(key));
    }

    private sealed class Use(string url, string path)
    {
        public int Count = 1;
        public string Url { get; } = url;
        public string Path { get; } = path;
    }
}
