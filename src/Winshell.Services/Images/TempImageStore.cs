using System.Globalization;
using System.Security.Cryptography;

namespace Winshell;

/// <summary>
/// Pictures other apps hand over as bytes, such as track art and notification logos, kept as temp files a UI loads
/// by <c>file:///</c> url. A file is named by its content, so the same picture is one file. Each claim is released
/// once, and the last release deletes the file. Every Winshell process on the machine shares the root, and each keeps
/// its files in a folder named by its process id, so one instance's cleanup cannot take a file another is showing.
/// </summary>
internal sealed class TempImageStore
{
    /// <summary>The most a picture can be, in bytes.</summary>
    internal const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The most pixels a picture can decode to.</summary>
    internal const long MaxPixels = 4096 * 4096;

    /// <summary>Why <see cref="Claim"/> returned null.</summary>
    internal const string Refused = "it is not a picture under the size limit";

    private readonly string _root;
    private readonly string _folder;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Use> _uses = [];
    private readonly Lock _gate = new();
    private int _cleaning;

    /// <summary>Pictures kept under <paramref name="root"/>. The directory is created when the first picture is written.</summary>
    public TempImageStore(string root, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        _folder = Path.Combine(root, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _time = time;
    }

    /// <summary>The store this process shows pictures from.</summary>
    public static TempImageStore Shared { get; } =
        new(Path.Combine(Path.GetTempPath(), "winshell", "images"), TimeProvider.System);

    /// <summary>
    /// Reads a picture and claims its file. Returns the file's url, or "" when the stream is empty. Returns null
    /// for data that is not a PNG, JPEG, GIF, BMP or WebP within <see cref="MaxBytes"/> and <see cref="MaxPixels"/>,
    /// and for a write that fails. SVG is left out: the shell would render it at whatever size it asks for. The read
    /// blocks, so call off the UI thread.
    /// </summary>
    public string? Claim(Stream input, CancellationToken cancellation = default)
    {
        var data = Read(input, cancellation);
        if (data is null)
            return null;
        if (data.Length == 0)
            return "";
        if (ImageHeader.Read(data) is not var (extension, width, height) || width == 0 || height == 0 ||
            (ulong)width * height > MaxPixels)
            return null;

        var path = Path.Combine(_folder, Convert.ToHexStringLower(SHA256.HashData(data))[..32] + extension);
        var url = new Uri(path).AbsoluteUri;
        lock (_gate)
        {
            if (_uses.TryGetValue(url, out var use))
            {
                use.Count++;
                return url;
            }

            // A failed delete leaves the previous file. It holds these bytes, so reuse it: replacing it would fail
            // while a loader still has it open.
            if (!File.Exists(path) && !TryWrite(path, data))
                return null;
            _uses.Add(url, new Use(path));
            return url;
        }
    }

    /// <summary>Gives up one claim of <paramref name="url"/>, and deletes the file when nothing claims it.</summary>
    public void Release(string url)
    {
        lock (_gate)
        {
            if (!_uses.TryGetValue(url, out var use))
                return;
            if (use.Count > 1)
            {
                use.Count--;
                return;
            }

            _uses.Remove(url);
            try
            {
                File.Delete(use.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An image loader still has it open. The file stays, and the next claim of these bytes reuses it.
            }
        }
    }

    /// <summary>
    /// <see cref="Release"/> on the thread pool, for the UI thread: the lock is held while a picture is written, and
    /// the last release deletes the file.
    /// </summary>
    public void ReleaseLater(string url)
    {
        if (url.Length > 0)
            _ = Task.Run(() => Release(url));
    }

    /// <summary>
    /// Runs <see cref="CleanOld"/> on the thread pool the first time it is called, for what earlier runs left behind.
    /// Old pictures are older than a day, so this does not have to finish before the first one is shown.
    /// </summary>
    /// <param name="failed">Gets why some old pictures were not deleted.</param>
    public void CleanOldOnce(Action<string> failed)
    {
        if (Interlocked.Exchange(ref _cleaning, 1) == 0)
            _ = Task.Run(() =>
            {
                if (CleanOld() is { } failure)
                    failed(failure);
            });
    }

    /// <summary>
    /// Deletes pictures older than a day, and empty folders other than this process's. A root that is a link is left
    /// alone. Returns why some were not deleted, or null.
    /// </summary>
    internal string? CleanOld()
    {
        // One file another process holds must not keep the rest: each entry is tried, and the first failure is kept.
        string? failure = null;
        try
        {
            if (!Directory.Exists(_root) || (File.GetAttributes(_root) & FileAttributes.ReparsePoint) != 0)
                return null;
            var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-1);
            var options = new EnumerationOptions
                { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(_root, "*", options))
                Try(() =>
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                });
            foreach (var dir in Directory.EnumerateDirectories(_root, "*",
                         new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
                Try(() =>
                {
                    if (!string.Equals(dir, _folder, StringComparison.OrdinalIgnoreCase) &&
                        !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure ??= e.Message;
        }

        return failure is null ? null : $"Cannot clean old pictures from {_root}: {failure}";

        void Try(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failure ??= e.Message;
            }
        }
    }

    /// <summary>At most <see cref="MaxBytes"/>. Null when the stream is longer.</summary>
    private static byte[]? Read(Stream input, CancellationToken cancellation)
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

    private static bool TryWrite(string path, byte[] data)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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

    private sealed class Use(string path)
    {
        public int Count = 1;
        public string Path { get; } = path;
    }
}
