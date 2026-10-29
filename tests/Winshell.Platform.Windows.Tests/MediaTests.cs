using System.Diagnostics;
using Windows.Media;
using Windows.Storage.Streams;
using Winshell.Platform.Windows.Native;
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
        ExerciseSession(media, art);
    }

    private void ExerciseSession(MediaService media, IRandomAccessStream art)
    {
        // Give the desktop publisher an actual HWND and keep its creating thread pumping messages. A source-less
        // MediaPlayer leaves registration up to its implicit playback machinery, which was unreliable on CI.
        var window = Win32.CreateWindowExW(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, "STATIC",
            "Winshell media probe", Win32.WS_POPUP | Win32.WS_VISIBLE, -4000, -4000, 80, 40, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        SystemMediaTransportControls? controls = null;
        try
        {
            controls = SystemMediaTransportControlsInterop.GetForWindow(window);
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
            var registrationTitle = _title + " registering";
            updater.MusicProperties.Title = registrationTitle;
            updater.Update();
            // A nonempty, unique title identifies this publisher without assuming Windows uses the exe name as
            // its AppUserModelID. Observe registration before publishing the metadata/art change under test.
            Assert.True(WaitUntil(() => media.Sessions.Values.OfType<MediaSession>()
                .Any(s => s.TrackTitle == registrationTitle)),
                Missing(media, "Windows did not publish the HWND-owned registration title."));
            updater.MusicProperties.Title = _title;
            updater.MusicProperties.Artist = "Winshell";
            updater.MusicProperties.AlbumTitle = "Live";
            updater.MusicProperties.AlbumArtist = "Winshell";
            updater.MusicProperties.TrackNumber = 3;
            updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(art);
            updater.Update();

            var session = WaitForSession(media);
            Assert.NotNull(session);
            Assert.True(WaitUntil(() => session.TrackArtUrl.Length > 0),
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
            Assert.True(WaitUntil(() => session.LengthSupported && session.PlaybackState == 1),
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
            var paused = WaitForState(session, 2);
            Assert.True(paused, "The session did not pause.");
            Assert.True(session.CanPlay);
            session.Play();
            Assert.True(WaitForState(session, 1), "The session did not resume.");
        }
        finally
        {
            try
            {
                if (controls is not null)
                    controls.IsEnabled = false;
            }
            finally
            {
                Assert.True(Win32.DestroyWindow(window), "The probe window did not close on its creating thread.");
            }
        }
    }

    private MediaSession? WaitForSession(MediaService media)
    {
        MediaSession? session = null;
        WaitUntil(() =>
        {
            session = media.Sessions.Values.OfType<MediaSession>().FirstOrDefault(s => s.TrackTitle == _title);
            return session is not null;
        });
        return session;
    }

    private bool WaitForState(MediaSession session, int state) =>
        WaitUntil(() => session.PlaybackState == state);

    private static string Missing(MediaService media, string message)
    {
        var sessions = media.Sessions.Values.OfType<MediaSession>()
            .Select(session => $"{session.AppId} [{session.TrackTitle}]")
            .ToArray();
        return sessions.Length == 0
            ? message + " The service listed no sessions."
            : message + " Saw " + string.Join(", ", sessions) + ".";
    }

    private bool WaitUntil(Func<bool> ready)
    {
        // Stay on the HWND's creating thread while pumping both Windows and the engine. Every phase still has a
        // readiness condition and an elapsed-time deadline; waiting for messages is not a substitute for readiness.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            for (var i = 0; i < 200 && Win32.PeekMessageW(out var message, 0, 0, 0, Win32.PM_REMOVE); i++)
            {
                Win32.TranslateMessage(message);
                Win32.DispatchMessageW(message);
            }
            _loop.Process();
            if (ready())
                return true;
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(30))
                return false;
            Win32.MsgWaitForMultipleObjectsEx(0, [], 50, Win32.QS_ALLINPUT, Win32.MWMO_INPUTAVAILABLE);
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
