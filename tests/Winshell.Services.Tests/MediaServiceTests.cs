using System.ComponentModel;
using Windows.Media.Control;
using Winshell.Media;

namespace Winshell.Services.Tests;

public sealed class MediaServiceTests
{
    private const GlobalSystemMediaTransportControlsSessionPlaybackStatus Playing = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    private const GlobalSystemMediaTransportControlsSessionPlaybackStatus Paused = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
    private const GlobalSystemMediaTransportControlsSessionPlaybackStatus Stopped = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped;
    private const GlobalSystemMediaTransportControlsSessionPlaybackStatus Changing = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;

    private readonly List<string> _warnings = [];
    private readonly Log _log;
    private readonly SmtcSession _native = SmtcSession.CreateForTests();

    public MediaServiceTests() => _log = new Log { Sink = (_, _, message) => _warnings.Add(message) };

    private sealed class FakeSessions : IMediaSessions
    {
        private readonly List<SmtcSession> _sessions = [];

        public IReadOnlyList<SmtcSession> Sessions => _sessions;

        public event Action<SmtcSession>? SessionAdded;

        public event Action<SmtcSession>? SessionRemoved;

        public SmtcSession Add(string appId)
        {
            var session = SmtcSession.CreateForTests(appId);
            _sessions.Add(session);
            SessionAdded?.Invoke(session);
            return session;
        }

        public void Remove(SmtcSession session)
        {
            _sessions.Remove(session);
            SessionRemoved?.Invoke(session);
        }
    }

    private MediaSession Session() => new(_native, _log);

    private void Track(string title, string artist = "", string album = "", string albumArtist = "", int number = 0, string[]? genres = null) =>
        _native.ApplyTrack(title, artist, album, albumArtist, number, genres, null);

    private void Timeline(double position, double end) =>
        _native.ApplyTimeline(TimeSpan.Zero, TimeSpan.FromSeconds(end), TimeSpan.FromSeconds(position), default, TimeProvider.System.GetUtcNow());

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void The_sessions_follow_Windows_and_one_that_stays_is_the_same_object()
    {
        var sessions = new FakeSessions();
        var first = sessions.Add("a.exe");
        using var media = new MediaService(new ManualEventLoop(), _log, sessions);
        var second = sessions.Add("b.exe");
        var kept = media.Sessions[1];

        sessions.Remove(first);

        Assert.Same(kept, Assert.Single(media.Sessions));
        Assert.Equal("b.exe", kept.AppId);
        Assert.Same(second, kept.Native);
    }

    [Fact]
    public void A_session_that_left_stops_reporting()
    {
        var sessions = new FakeSessions();
        var native = sessions.Add("a.exe");
        using var media = new MediaService(new ManualEventLoop(), _log, sessions);
        var changes = Watch(media.Sessions[0]);

        sessions.Remove(native);
        native.SetControls(canPlay: true);

        Assert.Empty(changes);
    }

    [Fact]
    public void A_disposed_service_stops_following_Windows()
    {
        var sessions = new FakeSessions();
        var native = sessions.Add("a.exe");
        var media = new MediaService(new ManualEventLoop(), _log, sessions);
        var changes = Watch(media.Sessions[0]);

        media.Dispose();
        sessions.Add("b.exe");
        native.SetControls(canPlay: true);

        Assert.Single(media.Sessions);
        Assert.Empty(changes);
    }

    [Fact]
    public void Can_control_is_false_with_no_command_true_with_one_and_false_when_it_goes_away()
    {
        var session = Session();
        var changes = Watch(session);
        int Count() => changes.Count(c => c == nameof(MediaSession.CanControl));

        Assert.False(session.CanControl);

        _native.SetControls(canPause: true);
        Assert.True(session.CanControl);
        Assert.Equal(1, Count());

        _native.SetControls(canPause: true, canGoNext: true);
        _native.SetControls(canGoNext: true);
        Assert.True(session.CanControl);
        Assert.Equal(1, Count());

        _native.SetControls();
        Assert.False(session.CanControl);
        Assert.Equal(2, Count());

        _native.SetControls(canSeek: true);
        Assert.True(session.CanControl);
        Assert.Equal(3, Count());
    }

    [Fact]
    public void Can_control_reports_the_loss_of_a_command_the_session_started_with()
    {
        _native.SetControls(canPlay: true);
        var session = Session();
        var changes = Watch(session);
        Assert.True(session.CanControl);

        _native.SetControls();

        Assert.False(session.CanControl);
        Assert.Contains(nameof(MediaSession.CanControl), changes);
    }

    [Fact]
    public void Every_change_names_a_property_of_the_session()
    {
        var session = Session();
        var changes = Watch(session);

        Track("Song", "Artist", "Album", "Band", 3, ["Rock"]);
        _native.ApplyPlayback(new SmtcPlayback(Playing)
        {
            Rate = 2,
            CanPlay = true,
            CanPause = true,
            CanStop = true,
            CanGoNext = true,
            CanGoPrevious = true,
            CanSeek = true,
            ShuffleSupported = true,
            LoopSupported = true,
            RateSupported = true,
            Shuffle = true,
            LoopState = 1,
        });
        Timeline(5, 125);

        Assert.All(changes, name => Assert.NotNull(typeof(MediaSession).GetProperty(name)));
        Assert.Contains(nameof(MediaSession.TrackAlbumArtist), changes);
        Assert.Contains(nameof(MediaSession.MaxRate), changes);
        Assert.Contains(nameof(MediaSession.LengthSupported), changes);
    }

    [Fact]
    public void A_new_track_is_told_before_and_after_its_properties_change()
    {
        var session = Session();
        var heard = new List<string>();
        session.TrackChanged += () => heard.Add("before");
        session.PostTrackChanged += () => heard.Add("after");
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaSession.TrackTitle))
                heard.Add("title");
        };

        Track("Song");
        Assert.Equal(["before", "title", "after"], heard);
        Assert.Equal((uint)1, session.UniqueId);

        heard.Clear();
        Track("Song", number: 2);
        Assert.Empty(heard);
        Assert.Equal((uint)1, session.UniqueId);
    }

    [Fact]
    public void Metadata_has_what_the_app_gives()
    {
        var session = Session();

        Track("Song", "Artist", "Album", "Band");
        Assert.Equal(["album", "albumArtist", "artist", "title"], session.Metadata.Keys.Order(StringComparer.Ordinal));

        Track("Song", "Artist", "Album", "Band", 3, ["Rock"]);
        Timeline(5, 125);

        Assert.Equal("Band", session.Metadata["albumArtist"]);
        Assert.Equal(125d, session.Metadata["duration"]);
        Assert.Equal(3, session.Metadata["trackNumber"]);
        Assert.Equal(["Rock"], (IReadOnlyList<string>)session.Metadata["genres"]!);
        Assert.Equal("Song", session.TrackTitle);
        Assert.Equal("Artist", session.TrackArtist);
        Assert.Equal("Album", session.TrackAlbum);
        Assert.Equal("Band", session.TrackAlbumArtist);
    }

    [Fact]
    public void The_playback_commands_reach_the_app_when_it_accepts_them()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Paused) { CanPlay = true, CanPause = true, CanStop = true, CanGoNext = true, CanGoPrevious = true });
        Assert.True(session.CanTogglePlaying);

        session.Play();
        session.Next();
        session.Previous();
        session.IsPlaying = true;
        _native.ApplyPlayback(new SmtcPlayback(Playing) { CanPlay = true, CanPause = true, CanStop = true });
        session.TogglePlaying();
        session.IsPlaying = false;
        session.Stop();
        session.PlaybackState = MediaPlaybackState.Stopped;

        Assert.Equal(["play", "next", "previous", "play", "pause", "pause", "stop", "stop"], _native.Sent);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Asking_for_the_state_it_is_in_sends_nothing()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Playing) { CanPlay = true });

        session.Play();

        Assert.Empty(_native.Sent!);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void A_control_the_app_does_not_allow_does_nothing_and_warns()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Paused));
        var refused = new Action[]
        {
            session.Play,
            session.Stop,
            session.Next,
            session.Previous,
            () => session.Seek(5),
            () => session.Position = 5,
            () => session.Shuffle = true,
            () => session.LoopState = MediaLoopState.List,
            () => session.Rate = 2,
            session.SwitchTo,
        };

        for (var i = 0; i < refused.Length; i++)
        {
            refused[i]();
            Assert.Equal(i + 1, _warnings.Count);
        }
        Assert.Empty(_native.Sent!);
    }

    [Fact]
    public void An_unknown_playback_state_is_refused()
    {
        var session = Session();

        session.PlaybackState = (MediaPlaybackState)7;

        Assert.Empty(_native.Sent!);
        Assert.Contains("unknown value 7", Assert.Single(_warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void A_moment_between_tracks_keeps_the_state_it_had()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Playing));
        var changes = Watch(session);

        _native.ApplyPlayback(new SmtcPlayback(Changing));

        Assert.Equal(MediaPlaybackState.Playing, session.PlaybackState);
        Assert.True(session.IsPlaying);
        Assert.Empty(changes);
    }

    [Fact]
    public void Shuffle_and_repeat_show_only_while_the_app_lets_Windows_change_them()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Playing) { Shuffle = true, LoopState = 2 });
        Assert.False(session.Shuffle);
        Assert.Equal(MediaLoopState.None, session.LoopState);

        _native.ApplyPlayback(new SmtcPlayback(Playing) { Shuffle = true, LoopState = 2, ShuffleSupported = true, LoopSupported = true });
        Assert.True(session.Shuffle);
        Assert.Equal(MediaLoopState.List, session.LoopState);

        session.Shuffle = false;
        session.LoopState = MediaLoopState.List;
        session.LoopState = MediaLoopState.Track;

        Assert.Equal(["set shuffle", "set loopState"], _native.Sent);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(0.25, true)]
    [InlineData(4, true)]
    [InlineData(0.2, false)]
    [InlineData(4.5, false)]
    public void A_rate_is_asked_for_between_the_limits(double rate, bool asked)
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Playing) { RateSupported = true });
        Assert.Equal(0.25, session.MinRate);
        Assert.Equal(4, session.MaxRate);

        session.Rate = rate;

        Assert.Equal(asked ? 1 : 0, _native.Sent!.Count);
        Assert.Equal(asked ? 0 : 1, _warnings.Count);
    }

    [Fact]
    public void With_no_speed_control_the_rate_stays_at_one()
    {
        var session = Session();

        Assert.Equal(1, session.Rate);
        Assert.Equal(1, session.MinRate);
        Assert.Equal(1, session.MaxRate);
    }

    [Fact]
    public void A_stopped_or_silent_session_is_at_position_zero()
    {
        var session = Session();
        Assert.Equal(0, session.Position);
        Assert.False(session.PositionSupported);

        _native.ApplyPlayback(new SmtcPlayback(Stopped));
        Timeline(5, 125);

        Assert.True(session.PositionSupported);
        Assert.Equal(0, session.Position);
    }

    [Fact]
    public void Seeking_shows_the_new_position_at_once_and_stays_on_the_track()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Paused) { CanSeek = true });
        Timeline(5, 125);
        Assert.Equal(5, session.Position);
        Assert.Equal(125, session.Length);
        var changes = Watch(session);

        session.Seek(10);
        Assert.Equal(15, session.Position);
        Assert.Equal([nameof(MediaSession.Position)], changes);

        session.Seek(-100);
        Assert.Equal(0, session.Position);

        session.Position = 500;
        Assert.Equal(125, session.Position);

        Assert.Equal(["set position", "set position", "set position"], _native.Sent);
    }

    [Fact]
    public void A_track_with_no_length_reports_its_position_as_the_length()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Paused));
        Timeline(5, 0);
        Assert.False(session.LengthSupported);
        Assert.Equal(5, session.Length);
        var changes = Watch(session);

        session.RefreshPosition();

        Assert.Equal([nameof(MediaSession.Position), nameof(MediaSession.Length)], changes);
    }

    [Fact]
    public void A_poll_that_repeats_the_last_report_is_not_a_seek()
    {
        var session = Session();
        _native.ApplyPlayback(new SmtcPlayback(Paused));
        Timeline(5, 125);
        var changes = Watch(session);

        Timeline(5, 125);

        Assert.Empty(changes);
    }

    [Fact]
    public void A_session_of_a_store_app_can_be_switched_to_and_one_with_no_window_cannot()
    {
        Assert.True(new MediaSession(SmtcSession.CreateForTests("Publisher.App_abc!App"), _log).CanSwitchTo);
        Assert.False(Session().CanSwitchTo);
        Assert.False(new MediaSession(SmtcSession.CreateForTests("no-such-program.exe"), _log).CanSwitchTo);
    }
}
