using System.Diagnostics;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Winshell.Platform.Windows.Services.Media;
using Winshell.Runtime;
using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Creates one system media session in this process, reads its metadata, artwork, and controls, then closes it.
/// </summary>
public sealed class MediaTests : IDisposable
{
    private readonly string _title = "Winshell probe " + Guid.NewGuid().ToString("N");

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly ITestOutputHelper _output;
    private readonly string _directory;
    private readonly ManualEventLoop _loop = new();
    private readonly QmlEngine _engine;
    private readonly QObject _root;

    public MediaTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "shell.qml"), """
            import QtQuick
            import Winshell.Media
            Item {
              property var media: Media
              property var sessionList: Media.sessions.values
              property int listRepeat: MediaLoopState.List
              property int playing: MediaPlaybackState.Playing
              property int paused: MediaPlaybackState.Paused
            }
            """);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var log = new Log { Sink = (_, category, message) => output.WriteLine($"{category}: {message}") };
        _engine = new QmlEngine(registry, _loop, log, _directory);
        _root = _engine.CreateRoot(Path.Combine(_directory, "shell.qml"));
        _loop.Process();
    }

    [Fact]
    public async Task A_session_reports_metadata_artwork_and_controls()
    {
        Assert.Equal(2, _root.ReadProperty("listRepeat"));
        Assert.Equal(1, _root.ReadProperty("playing"));
        Assert.Equal(2, _root.ReadProperty("paused"));

        var media = (MediaService)_root.ReadProperty("media")!;
        using var art = new InMemoryRandomAccessStream();
        var writer = new DataWriter(art);
        writer.WriteBytes(Png);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        art.Seek(0);
        var player = new MediaPlayer { CommandManager = { IsEnabled = false } };
        try
        {
            var controls = player.SystemMediaTransportControls;
            controls.IsEnabled = true;
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsStopEnabled = true;
            controls.IsNextEnabled = true;
            controls.PlaybackStatus = MediaPlaybackStatus.Playing;
            controls.ButtonPressed += (_, args) =>
            {
                if (args.Button == SystemMediaTransportControlsButton.Pause)
                    controls.PlaybackStatus = MediaPlaybackStatus.Paused;
                else if (args.Button == SystemMediaTransportControlsButton.Play)
                    controls.PlaybackStatus = MediaPlaybackStatus.Playing;
            };
            var updater = controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.Update();
            // Session registration is independent of its property events. Let the real consumer subscribe before
            // publishing the one metadata/art update, just as the timeline below is published after discovery.
            var appId = Path.GetFileName(Environment.ProcessPath);
            Assert.True(await WaitUntil(() => media.Sessions.Values.OfType<MediaSession>()
                .Any(s => string.Equals(s.AppId, appId, StringComparison.OrdinalIgnoreCase))),
                "Windows did not register this process's probe session.");
            updater.MusicProperties.Title = _title;
            updater.MusicProperties.Artist = "Winshell";
            updater.MusicProperties.AlbumTitle = "Live";
            updater.MusicProperties.AlbumArtist = "Winshell";
            updater.MusicProperties.TrackNumber = 3;
            updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(art);
            updater.Update();

            var session = await WaitForSession(media);
            Assert.NotNull(session);
            Assert.True(await WaitUntil(() => session.TrackArtUrl.Length > 0),
                "The probe's metadata arrived without its artwork.");
            // Windows publishes metadata/art and the timeline independently. Publish the timeline only after
            // discovery so this test always exercises the later timeline event instead of depending on a race.
            Assert.False(session.LengthSupported);
            controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(125),
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = TimeSpan.FromSeconds(125),
                Position = TimeSpan.FromSeconds(5),
            });
            Assert.True(await WaitUntil(() => session.LengthSupported && session.PlaybackState == 1),
                "The discovered session did not receive its timeline and playing state.");
            _output.WriteLine($"session: {session.AppName} ({session.AppId}) state {session.PlaybackState} art {session.TrackArtUrl}");
            Assert.Equal(_title, session.TrackTitle);
            Assert.Equal("Winshell", session.TrackArtist);
            Assert.Equal("Live", session.TrackAlbum);
            Assert.Equal("Winshell", session.TrackAlbumArtist);
            Assert.False(string.IsNullOrWhiteSpace(session.AppName));
            var metadata = session.Metadata;
            Assert.Equal(_title, metadata["title"] as string);
            Assert.Equal("Winshell", metadata["artist"] as string);
            Assert.Equal("Live", metadata["album"] as string);
            Assert.Equal(125d, metadata["duration"]);
            Assert.Equal(3, metadata["trackNumber"]);
            Assert.StartsWith("file:///", session.TrackArtUrl, StringComparison.Ordinal);
            Assert.True(File.Exists(new Uri(session.TrackArtUrl).LocalPath));
            _output.WriteLine($"controls play {session.CanPlay} pause {session.CanPause} stop {session.CanStop} next {session.CanGoNext}");
            Assert.False(session.CanPlay);
            Assert.True(session.CanPause);
            Assert.True(session.CanStop);
            Assert.True(session.CanGoNext);
            Assert.Equal(1, session.PlaybackState);

            session.Pause();
            var paused = await WaitForState(session, 2);
            Assert.True(paused, "The session did not pause.");
            Assert.True(session.CanPlay);
            session.Play();
            Assert.True(await WaitForState(session, 1), "The session did not resume.");
        }
        finally
        {
            player.SystemMediaTransportControls.IsEnabled = false;
            player.Dispose();
        }
    }

    private async Task<MediaSession?> WaitForSession(MediaService media)
    {
        MediaSession? session = null;
        await WaitUntil(() =>
        {
            session = media.Sessions.Values.OfType<MediaSession>().FirstOrDefault(s => s.TrackTitle == _title);
            return session is not null;
        });
        return session;
    }

    private Task<bool> WaitForState(MediaSession session, int state) =>
        WaitUntil(() => session.PlaybackState == state);

    private async Task<bool> WaitUntil(Func<bool> ready)
    {
        // GSMTC publication/commands cross processes. Use an elapsed-time deadline and keep pumping the engine,
        // including when a shared runner delays a continuation. No fixed sleep stands in for readiness.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            _loop.Process();
            if (ready())
                return true;
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(30))
                return false;
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        _engine.Dispose();
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }
}
