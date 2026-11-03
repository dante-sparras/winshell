namespace Winshell.Media;

/// <summary>
/// The names of the apps behind media sessions, looked up off the loop and kept for the life of the
/// process. Lives on the UI thread.
/// </summary>
internal sealed class AppNames(IEventLoop loop, Log log, Func<string, string> describe, Action named)
{
    private readonly Dictionary<string, string?> _names = [];

    /// <summary>
    /// The app's name, or null while it is looked up. When the lookup ends, <c>named</c> runs on the loop.
    /// </summary>
    public string? Get(string appId)
    {
        if (_names.TryGetValue(appId, out var name))
            return name;
        _names[appId] = null;
        LookUp(appId);
        return null;
    }

    private void LookUp(string appId)
    {
        // Not the thread pool. A lookup that had already failed still waited for a pool thread to publish
        // the id, and a busy pool left the session unnamed.
        var thread = new Thread(() =>
        {
            var name = appId;
            try
            {
                name = describe(appId);
            }
#pragma warning disable CA1031 // A failed lookup still names the app, by its id.
            catch (Exception e)
#pragma warning restore CA1031
            {
                var message = e.Message;
                loop.Post(() => log.Warn("media", $"Cannot name {appId}: {message}"));
            }
            loop.Post(() =>
            {
                _names[appId] = name;
                named();
            });
        })
        {
            IsBackground = true,
            Name = "Media app name",
        };
        thread.Start();
    }
}
