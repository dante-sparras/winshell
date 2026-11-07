namespace Winshell.Notifications;

/// <summary>App logos Windows hands over as image data, claimed in the <see cref="TempImageStore"/>.</summary>
internal static class NotificationAppIcons
{
    private static readonly HashSet<string> Logged = [];
    private static readonly Lock Gate = new();

    /// <summary>
    /// Reads <paramref name="appKey"/>'s logo and returns its claimed url, or "" when Windows gives none or the read
    /// fails. Release a url once. A failure is logged at most once per app. Call off the UI thread.
    /// </summary>
    public static string Read(string appKey, string label, IAppLogoSource source, Log log,
        CancellationToken cancellation)
    {
        try
        {
            using var stream = source.Open(cancellation);
            if (stream is null)
                return "";
            var url = TempImageStore.Shared.Claim(stream, cancellation);
            if (url is null)
                LogOnce(appKey, label, log, TempImageStore.Refused);
            return url ?? "";
        }
        catch (OperationCanceledException)
        {
            return "";
        }
#pragma warning disable CA1031 // One app's logo must not stop the read, or the notifications beside it.
        catch (Exception e)
#pragma warning restore CA1031
        {
            LogOnce(appKey, label, log, e.Message.Length > 0 ? e.Message : e.GetType().Name);
            return "";
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
}
