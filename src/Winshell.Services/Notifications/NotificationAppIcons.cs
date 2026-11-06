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
/// The bytes are a <see cref="TempImageStore"/>. The key this caller holds is the app plus that generation.
/// </summary>
internal static class NotificationAppIcons
{
    private static readonly TempImageStore Images =
        new(Path.Combine(Path.GetTempPath(), "winshell", "notification-icons"));

    private static readonly Dictionary<string, Slot> Slots = [];
    private static readonly HashSet<string> Logged = [];
    private static readonly Lock Gate = new();
    private static long _tokens;
    private static int _cleaned;

    /// <summary>
    /// Reads <paramref name="appKey"/>'s logo and returns its url, or "" when Windows gives none or the read
    /// fails. A failure is logged at most once per app. Call off the UI thread.
    /// </summary>
    public static IconRead Read(string appKey, string label, IAppLogoSource source, Log log,
        CancellationToken cancellation)
    {
        Slot slot;
        var reader = false;
        lock (Gate)
        {
            if (!Slots.TryGetValue(appKey, out slot!))
                Slots[appKey] = slot = new Slot(Interlocked.Increment(ref _tokens));
            slot.Users++;
            if (slot.Url is not null)
            {
                if (slot.Url.Length > 0)
                    Images.Retain(FileKey(appKey, slot.Token));
                return new IconRead(slot.Url, new IconHold(appKey, slot.Token));
            }

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
            {
                return new IconRead(slot.Url ?? "", hold);
            }
        }

        // Old icons are older than a day, so this does not have to finish before the first picture is shown.
        if (Interlocked.Exchange(ref _cleaned, 1) == 0)
            Images.CleanOld();

        var key = FileKey(appKey, slot.Token);
        var url = "";
        try
        {
            url = ReadSource(appKey, key, label, source, log, cancellation);
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
            var held = url.Length > 0;
            lock (Gate)
            {
                var tracked = Slots.TryGetValue(appKey, out var current) && current.Token == slot.Token &&
                              slot.Users > 0;
                if (held && !tracked)
                {
                    Images.Release(key);
                    url = "";
                }
                else if (held && slot.Users > 1)
                {
                    // Store already counted the reader. The others were waiting and have not counted their own.
                    Images.Retain(key, slot.Users - 1);
                }

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
        var releaseFile = false;
        lock (Gate)
        {
            if (!Slots.TryGetValue(hold.Key, out var slot) || slot.Token != hold.Token)
                return;
            slot.Users--;
            releaseFile = slot.Url is { Length: > 0 };
            if (slot.Users == 0)
            {
                Slots.Remove(hold.Key);
                retiring = slot;
            }
        }

        if (releaseFile)
            Images.Release(FileKey(hold.Key, hold.Token));
        retiring?.Dispose();
    }

    /// <summary>Deletes icons left behind by earlier runs, and their folders once empty.</summary>
    public static void CleanOld()
    {
        Images.CleanOld();
    }

    private static string ReadSource(string appKey, string key, string label, IAppLogoSource source, Log log,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = source.Open(cancellation);
        if (stream is null)
            return "";
        var data = Images.Read(stream, cancellation);
        if (data is null)
        {
            LogOnce(appKey, label, log, "it is not a picture under the size limit");
            return "";
        }

        if (data.Length == 0)
            return "";
        var url = Images.Store(key, data);
        if (url is not { Length: > 0 })
        {
            LogOnce(appKey, label, log, "it is not a picture under the size limit");
            return "";
        }

        return url;
    }

    private static string FileKey(string appKey, long token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appKey)))[..32].ToLowerInvariant()
               + "-" + token.ToString(CultureInfo.InvariantCulture);
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

        public void Dispose()
        {
            Ready.Dispose();
        }
    }
}
