using Winshell.Platform.Windows.Services.Media;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Naming the app behind a media session asks the shell and can scan processes. The UI thread must not wait for it.
/// </summary>
public class MediaAppNameTests
{
    [Fact]
    public void An_app_is_named_once_off_the_ui_thread()
    {
        var loop = new ManualEventLoop();
        using var lookupMayEnd = new ManualResetEventSlim();
        var lookups = 0;
        var lookupThread = 0;
        var named = 0;
        var names = new AppNames(loop, new Log(), appId =>
        {
            Interlocked.Increment(ref lookups);
            lookupThread = Environment.CurrentManagedThreadId;
            lookupMayEnd.Wait(TimeSpan.FromSeconds(10));
            return "Spotify";
        }, () => named++);

        // Both return while the lookup is still blocked.
        Assert.Null(names.Get("Spotify.exe"));
        Assert.Null(names.Get("Spotify.exe"));
        lookupMayEnd.Set();
        Wait.Until(() => named > 0, "the app name", loop.Process);

        Assert.Equal("Spotify", names.Get("Spotify.exe"));
        Assert.Equal(1, lookups);
        Assert.Equal(1, named);
        Assert.NotEqual(Environment.CurrentManagedThreadId, lookupThread);
    }

    [Fact]
    public void A_failed_lookup_names_the_app_by_its_id()
    {
        var loop = new ManualEventLoop();
        var warnings = new List<string>();
        var named = 0;
        var log = new Log { Sink = (_, category, message) => warnings.Add($"{category}: {message}") };
        var names = new AppNames(loop, log, _ => throw new InvalidOperationException("no shell"), () => named++);

        Assert.Null(names.Get("Player.exe"));
        Wait.Until(() => named > 0, "the app name", loop.Process);

        Assert.Equal("Player.exe", names.Get("Player.exe"));
        Assert.Contains(warnings, w => w.Contains("no shell", StringComparison.Ordinal));
    }
}
