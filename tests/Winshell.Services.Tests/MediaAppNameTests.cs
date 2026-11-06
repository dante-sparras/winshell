using Winshell.Apps;
using Winshell.Media;

namespace Winshell.Services.Tests;

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
            return "Spotify Music";
        }, () => named++);

        // Both return the fallback name while the lookup is still blocked.
        Assert.Equal("Spotify", names.Get("Spotify.exe"));
        Assert.Equal("Spotify", names.Get("Spotify.exe"));
        lookupMayEnd.Set();
        Wait.Until(() => named > 0, "the app name", loop.Process);

        Assert.Equal("Spotify Music", names.Get("Spotify.exe"));
        Assert.Equal(1, lookups);
        Assert.Equal(1, named);
        Assert.NotEqual(Environment.CurrentManagedThreadId, lookupThread);
    }

    [Fact]
    public void A_failed_lookup_keeps_the_fallback_name()
    {
        var loop = new ManualEventLoop();
        var warnings = new List<string>();
        var log = new Log { Sink = (_, category, message) => warnings.Add($"{category}: {message}") };
        var names = new AppNames(loop, log, _ => throw new InvalidOperationException("no shell"), () => { });

        Assert.Equal("Player", names.Get("Player.exe"));
        Wait.Until(() => warnings.Count > 0, "the lookup warning", loop.Process);

        Assert.Equal("Player", names.Get("Player.exe"));
        Assert.Contains(warnings, w => w.Contains("no shell", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("VLC.EXE", "VLC")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic",
        "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")]
    public void The_fallback_name_is_the_program_without_its_extension_or_else_the_app_id(string appId, string name)
    {
        Assert.Equal(name, AppIdentity.FallbackName(appId));
    }
}
