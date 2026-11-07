namespace Winshell.Notifications;

/// <summary>
/// Reads app logos Windows hands over as image data and claims them in the <see cref="TempImageStore"/>. A failure is
/// logged at most once per app.
/// </summary>
internal sealed class NotificationAppIcons(IEventLoop loop, Log log, CancellationToken stop)
{
    private readonly HashSet<string> _logged = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Reads <paramref name="appKey"/>'s logo off the UI thread and posts its claimed url to <paramref name="landed"/>,
    /// or "" when Windows gives none or the read fails. Whoever hears a url releases it once.
    /// </summary>
    public void Read(string appKey, string label, IAppLogoSource source, Action<string> landed)
    {
        // Not the thread pool: the logo read blocks on Windows, and publishing the url has to run even when every
        // pool thread is busy.
        var thread = new Thread(() =>
        {
            var url = Claim(appKey, label, source);
            loop.Post(() => landed(url));
        })
        {
            IsBackground = true,
            Name = "Notification icon"
        };
        thread.Start();
    }

    private string Claim(string appKey, string label, IAppLogoSource source)
    {
        try
        {
            using var stream = source.Open(stop);
            if (stream is null)
                return "";
            var url = TempImageStore.Shared.Claim(stream, stop);
            if (url is null)
                LogOnce(appKey, label, TempImageStore.Refused);
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
            LogOnce(appKey, label, e.Message.Length > 0 ? e.Message : e.GetType().Name);
            return "";
        }
    }

    private void LogOnce(string appKey, string label, string reason)
    {
        lock (_gate)
        {
            if (!_logged.Add(appKey))
                return;
        }

        log.Warn("notifications", $"Cannot read the icon from {label}: {reason}");
    }
}
