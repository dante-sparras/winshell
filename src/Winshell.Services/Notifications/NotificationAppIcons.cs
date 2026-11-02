using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Winshell.Notifications;

/// <summary>One read of a logo. Release <see cref="Hold"/> once, whether or not the url is kept.</summary>
internal readonly record struct IconRead(string Url, IconHold Hold);

/// <summary>A claim on one app's icon file. The default value claims nothing.</summary>
internal readonly record struct IconHold(string Key, long Token)
{
    public bool Active => Key is { Length: > 0 };
}

/// <summary>
/// App logos Windows hands over as image data. One file per app, in this process's temp folder, given to the UI
/// as a <c>file:///</c> url. The file goes away when nothing is showing that app's notifications, so a later
/// generation of the same app writes a new file rather than deleting the one a previous read still names.
/// </summary>
internal static class NotificationAppIcons
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "winshell", "notification-icons");
    private static readonly string Folder = Path.Combine(Root, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
    private static readonly Dictionary<string, Slot> Slots = [];
    private static readonly HashSet<string> Logged = [];
    private static readonly Lock Gate = new();
    private static long _tokens;
    private static int _cleaned;

    /// <summary>The most logo an app can hand over, in bytes. The same cap as track art.</summary>
    internal const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The most pixels a logo can decode to. The same cap as track art.</summary>
    internal const long MaxPixels = 4096 * 4096;

    /// <summary>
    /// Reads <paramref name="appKey"/>'s logo and returns its url, or "" when Windows gives none or the read
    /// fails. A failure is logged at most once per app. Call off the UI thread.
    /// </summary>
    public static IconRead Read(string appKey, string label, IAppLogoSource source, Log log, CancellationToken cancellation)
    {
        Slot slot;
        var reader = false;
        lock (Gate)
        {
            if (!Slots.TryGetValue(appKey, out slot!))
                Slots[appKey] = slot = new Slot(Interlocked.Increment(ref _tokens));
            slot.Users++;
            if (slot.Url is not null)
                return new IconRead(slot.Url, new IconHold(appKey, slot.Token));
            if (!slot.Reading)
            {
                slot.Reading = true;
                reader = true;
            }
        }

        var hold = new IconHold(appKey, slot.Token);
        if (!reader)
        {
            try
            {
                slot.Ready.Wait(cancellation);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return new IconRead("", hold);
            }
            lock (Gate)
                return new IconRead(slot.Url ?? "", hold);
        }

        // Old icons are older than a day, so this does not have to finish before the first picture is shown.
        if (Interlocked.Exchange(ref _cleaned, 1) == 0)
            CleanOld();

        var url = "";
        try
        {
            url = ReadSource(appKey, slot.Token, label, source, log, cancellation);
        }
        catch (OperationCanceledException)
        {
            url = "";
        }
#pragma warning disable CA1031 // One app's logo must not stop the read, or the notifications beside it.
        catch (Exception e)
        {
            LogOnce(appKey, label, log, e.Message.Length > 0 ? e.Message : e.GetType().Name);
            url = "";
        }
#pragma warning restore CA1031
        finally
        {
            lock (Gate)
            {
                slot.Url = url;
                slot.Reading = false;
                slot.Ready.Set();
            }
        }
        return new IconRead(url, hold);
    }

    /// <summary>Gives up one claim. Deletes the file when the last notification of that app has let go.</summary>
    public static void Release(IconHold hold)
    {
        if (!hold.Active)
            return;
        Slot? retiring = null;
        string? delete = null;
        lock (Gate)
        {
            if (!Slots.TryGetValue(hold.Key, out var slot) || slot.Token != hold.Token)
                return;
            slot.Users--;
            if (slot.Users > 0)
                return;
            Slots.Remove(hold.Key);
            retiring = slot;
            delete = slot.Url is { Length: > 0 } url ? url : null;
        }
        retiring?.Dispose();
        if (delete is not null)
            DeleteFile(delete);
    }

    /// <summary>Deletes icons left behind by earlier runs, and their folders once empty.</summary>
    public static void CleanOld() => CleanOld(Root, Folder);

    internal static void CleanOld(string root, string keepFolder)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                if (File.GetLastWriteTimeUtc(file) < TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(-1))
                    File.Delete(file);
            }
            foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                if (!string.Equals(dir, keepFolder, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ReadSource(string appKey, long token, string label, IAppLogoSource source, Log log, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = source.Open(cancellation);
        if (stream is null)
            return "";
        var data = ReadLimited(stream, cancellation);
        if (data is null)
        {
            LogOnce(appKey, label, log, "it is not a picture under the size limit");
            return "";
        }
        if (data.Length == 0)
            return "";
        if (!TryDescribe(data, out var extension))
        {
            LogOnce(appKey, label, log, "it is not a picture under the size limit");
            return "";
        }
        var url = Write(appKey, token, data, extension);
        if (url.Length == 0)
            LogOnce(appKey, label, log, "it is not a picture under the size limit");
        return url;
    }

    private static byte[]? ReadLimited(Stream input, CancellationToken cancellation)
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

    /// <summary>PNG, JPEG, GIF, BMP or WebP within <see cref="MaxPixels"/>. SVG is left out: the shell would render it at whatever size it asks for.</summary>
    internal static bool TryDescribe(ReadOnlySpan<byte> data, out string extension) =>
        ImageHeader.TryRead(data, out extension, out var width, out var height) && width > 0 && height > 0 && width <= MaxPixels / height;

    private static string Write(string appKey, long token, byte[] data, string extension)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appKey)))[..32].ToLowerInvariant()
            + "-" + token.ToString(CultureInfo.InvariantCulture) + extension;
        var path = Path.Combine(Folder, name);
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
            return new Uri(path).AbsoluteUri;
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
            return "";
        }
    }

    private static void DeleteFile(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile)
            return;
        var path = uri.LocalPath;
        var root = Path.GetFullPath(Folder + Path.DirectorySeparatorChar);
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void LogOnce(string appKey, string label, Log log, string reason)
    {
        lock (Gate)
        {
            if (!Logged.Add(appKey))
                return;
        }
        var who = label.Length > 0 ? label : "an app";
        log.Warn("notifications", $"Cannot read the icon from {who}: {reason}");
    }

    private sealed class Slot(long token) : IDisposable
    {
        public long Token { get; } = token;
        public int Users;
        public string? Url;
        public bool Reading;
        public ManualResetEventSlim Ready { get; } = new(false);

        public void Dispose() => Ready.Dispose();
    }
}
