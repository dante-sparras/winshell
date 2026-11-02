using System.Collections.ObjectModel;
using Winshell.Audio;
using Winshell.Media;
using Winshell.Power;
using Windows.Media.Control;

namespace Winshell.DefaultShell.Tests;

/// <summary>The on-screen display's behaviour, driven without a window so nothing on this PC changes.</summary>
public sealed class OnScreenDisplayTests
{
    [Fact]
    public void A_volume_change_after_the_baseline_sits_above_the_work_area()
    {
        var time = new FakeTimeProvider();
        var model = new OnScreenDisplayModel(time) { AnimationsEnabled = false };
        var primary = Primary();
        model.SetScreens([primary, Secondary()]);
        model.VolumeKnown = true;
        model.Volume = 0.2;
        model.Volume = 0.9;
        Settle(time);
        Assert.False(model.Open);

        model.Volume = 0.4;

        Assert.Equal("volume", model.Mode);
        Assert.Equal("Volume 40 percent", model.Announcement);
        Assert.True(model.ShowLevel);
        Assert.False(model.ShowMedia);
        Assert.Equal(primary, model.Screen);
        Assert.Equal("\uE994", model.LevelGlyph);
        Assert.Equal(64, model.PanelHeight);
        AssertPlaced();
    }

    [Fact]
    public void Mute_volume_and_the_microphone_are_announced_without_a_level_write()
    {
        var time = new FakeTimeProvider();
        var model = new OnScreenDisplayModel(time) { AnimationsEnabled = false };
        model.VolumeKnown = true;
        model.Volume = 0.8;
        model.MicrophoneKnown = true;
        model.MicrophoneMuted = false;
        Settle(time);

        model.PlaybackMuted = true;
        Assert.Equal("Volume muted", model.Announcement);
        Assert.Equal("\uE74F", model.LevelGlyph);
        Assert.Equal(0.8, model.Volume);

        model.MicrophoneMuted = true;
        Assert.Equal("microphone", model.Mode);
        Assert.Equal("Microphone muted", model.Announcement);
        Assert.False(model.ShowLevel);
        Assert.Equal(64, model.PanelHeight);
        Assert.Equal("\uF781", model.MicrophoneGlyph);

        model.MicrophoneMuted = false;
        Assert.Equal("Microphone on", model.Announcement);
        Assert.Equal("\uE720", model.MicrophoneGlyph);
    }

    [Fact]
    public void Brightness_arriving_after_startup_baselines_then_shows_on_that_monitor()
    {
        using var rig = new Rig();
        rig.Bind();
        var second = rig.Screens[1];
        rig.Settle();

        rig.Push(Monitor("DISPLAY2", "Other", 10, 0, 50));
        Assert.False(rig.Model.Open);

        rig.Push(Monitor("DISPLAY2", "Other", 25, 0, 50));
        Assert.Equal("brightness", rig.Model.Mode);
        Assert.Equal("Brightness 50 percent", rig.Model.Announcement);
        Assert.Equal("DISPLAY2", rig.Model.Screen!.Name);
        Assert.Equal(@"\\.\DISPLAY2", rig.Model.Screen.Id);
        Assert.Equal("Monitor B", rig.Model.Screen.Model);
        Assert.Equal("\uE706", rig.Model.LevelGlyph);
        AssertPlaced();

        rig.Push(Monitor(@"\\.\DISPLAY2", "Other", 0, 0, 50));
        rig.Push(Monitor(@"\\.\DISPLAY2", "Other", 40, 0, 50));
        Assert.Same(second, rig.Model.Screen);
        Assert.Equal("Brightness 80 percent", rig.Model.Announcement);

        Hide(rig.Model, rig.Time);
        rig.Push(Monitor("OTHER", "Monitor B", 0, 0, 50));
        rig.Push(Monitor("OTHER", "Monitor B", 10, 0, 50));
        Assert.Same(second, rig.Model.Screen);

        var replaced = Screen("DISPLAY2", "Monitor B", false, 2000);
        rig.Screens[1] = replaced;
        Assert.Same(replaced, rig.Model.Screen);
    }

    [Fact]
    public void Media_stays_longer_and_grows_when_the_level_is_known()
    {
        var time = new FakeTimeProvider();
        var model = new OnScreenDisplayModel(time) { AnimationsEnabled = false };
        model.MediaKnown = true;
        model.MediaTitle = "Song";
        model.MediaArtist = "Artist";
        model.MediaPlaying = false;
        Settle(time);

        model.MediaPlaying = true;
        Assert.Equal("media", model.Mode);
        Assert.Equal("Playing Song, Artist", model.Announcement);
        Assert.True(model.ShowMedia);
        Assert.False(model.ShowLevel);
        Assert.Equal(80, model.PanelHeight);
        Assert.Equal("\uE8D6", OnScreenDisplayModel.ArtGlyph);
        Assert.Equal("Song", model.MediaHeading);

        model.VolumeKnown = true;
        model.Volume = 0.4;
        Assert.Equal("volume", model.Mode);
        Assert.Equal("Volume 40 percent. Playing Song, Artist", model.Announcement);
        Assert.True(model.ShowMedia);
        Assert.Equal(120, model.PanelHeight);
    }

    [Fact]
    public void The_level_hides_after_three_seconds_unless_the_pointer_is_inside()
    {
        var time = new FakeTimeProvider();
        var model = new OnScreenDisplayModel(time) { AnimationsEnabled = false };
        model.VolumeKnown = true;
        model.Volume = 0.2;
        Settle(time);
        model.Volume = 0.4;

        time.Advance(TimeSpan.FromMilliseconds(2000));
        model.Volume = 0.5;
        time.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(model.Open);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(model.Open);

        model.Volume = 0.6;
        model.PointerInside = true;
        Assert.True(model.PointerInside);
        time.Advance(TimeSpan.FromMilliseconds(5000));
        Assert.True(model.Open);
        model.PointerInside = false;
        Assert.False(model.PointerInside);
        time.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(model.Open);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(model.Open);
    }

    [Fact]
    public void Media_hides_after_five_seconds_and_motion_fades_the_level()
    {
        var time = new FakeTimeProvider();
        var model = new OnScreenDisplayModel(time) { AnimationsEnabled = false };
        model.MediaKnown = true;
        model.MediaTitle = "Song";
        model.MediaPlaying = false;
        Settle(time);
        model.MediaPlaying = true;
        Assert.Equal("Playing Song", model.Announcement);
        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.True(model.Open);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(model.Open);

        model.AnimationsEnabled = true;
        model.VolumeKnown = true;
        model.Volume = 0.2;
        model.Volume = 0.3;
        time.Advance(TimeSpan.FromMilliseconds(3000));
        Assert.True(model.Open);
        model.PointerInside = true;
        Assert.False(model.Hiding);
        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(model.Open);
        model.PointerInside = false;
        Assert.False(model.PointerInside);
        Assert.Equal("volume", model.Mode);
        time.Advance(TimeSpan.FromMilliseconds(3000));
        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.False(model.Open);
    }

    [Fact]
    public void Transport_buttons_are_the_only_press_that_leaves_the_panel()
    {
        var time = new FakeTimeProvider();
        var model = new OnScreenDisplayModel(time) { AnimationsEnabled = false };
        model.VolumeKnown = true;
        model.Volume = 0.4;
        model.MediaKnown = true;
        model.MediaTitle = "Song";
        model.MediaArtist = "Artist";
        model.MediaPlaying = false;
        model.CanTogglePlayback = true;
        model.CanGoNext = true;
        model.CanGoPrevious = false;
        Settle(time);
        model.MediaPlaying = true;

        var toggles = 0;
        var previous = 0;
        model.TogglePlaybackRequested += () => toggles++;
        model.PreviousRequested += () => previous++;
        model.Previous();
        Assert.Equal(0, previous);
        model.TogglePlayback();
        Assert.Equal(1, toggles);

        Assert.Equal("Playing Song, Artist", model.Announcement);
        Assert.Equal("Volume", model.LevelName);
        Assert.InRange(model.Level, 0.39, 0.41);
        Assert.Equal("Pause", model.PlayName);
        model.TogglePlayback();
        Assert.Equal(2, toggles);
        Assert.Equal(0.4, model.Volume);
    }

    [Fact]
    public void The_default_shell_shows_the_panel_from_its_services_and_does_not_reserve_space()
    {
        using var rig = new Rig();
        var speakers = rig.Graph.Add(new FakePlayback("Speakers"));
        speakers.SetAudio(0.2f, false);
        rig.Graph.Use(speakers);
        var mic = rig.Graph.AddRecording("Microphone");
        rig.Graph.UseRecording(mic);
        rig.Bind();
        rig.Settle();
        AssertPlaced();

        speakers.SetAudio(0.4f, false);
        Assert.True(rig.Model.Open);
        Assert.Equal("Volume 40 percent", rig.Model.Announcement);
        Assert.Equal(64, rig.Model.PanelHeight);
        Assert.Equal(0.4f, speakers.Volumes[0]);

        Hide(rig.Model, rig.Time);
        mic.SetMuted(true);
        Assert.Equal("Microphone muted", rig.Model.Announcement);
        Assert.True(mic.Muted);
    }

    [Fact]
    public void A_monitor_that_appears_later_does_not_show_until_its_brightness_changes()
    {
        using var rig = new Rig();
        rig.Bind();
        rig.Settle();

        rig.Push(Monitor("DISPLAY2", "Monitor B", 40));
        Assert.False(rig.Model.Open);

        rig.Push(Monitor("DISPLAY2", "Monitor B", 55));
        Assert.Equal("brightness", rig.Model.Mode);
        Assert.Equal("Brightness 55 percent", rig.Model.Announcement);
        Assert.Equal("DISPLAY2", rig.Model.Screen!.Name);
        Assert.Equal(@"\\.\DISPLAY2", rig.Model.Screen.Id);
        Assert.Equal(55, rig.Brightness.Monitors[0].Brightness);
    }

    [Fact]
    public void Each_monitor_shows_its_own_change_even_at_the_level_another_one_has()
    {
        using var rig = new Rig();
        rig.Bind();
        rig.Push(Monitor("DISPLAY1", "Monitor A", 30), Monitor("DISPLAY2", "Monitor B", 70));
        rig.Settle();
        Assert.False(rig.Model.Open);

        rig.Push(Monitor("DISPLAY1", "Monitor A", 30), Monitor("DISPLAY2", "Monitor B", 55));
        Assert.Equal("Brightness 55 percent", rig.Model.Announcement);
        Assert.Equal("DISPLAY2", rig.Model.Screen!.Name);

        Hide(rig.Model, rig.Time);
        rig.Push(Monitor("DISPLAY1", "Monitor A", 55), Monitor("DISPLAY2", "Monitor B", 55));
        Assert.True(rig.Model.Open);
        Assert.Equal("Brightness 55 percent", rig.Model.Announcement);
        Assert.Equal("DISPLAY1", rig.Model.Screen!.Name);

        Hide(rig.Model, rig.Time);
        rig.Push(Monitor("DISPLAY1", "Monitor A", 55), Monitor("DISPLAY2", "Monitor B", 55), Monitor("DISPLAY2", "Monitor C", 10));
        Assert.False(rig.Model.Open);
    }

    [Fact]
    public void A_playing_session_shows_the_track_and_its_controls_call_the_session()
    {
        using var rig = new Rig();
        rig.Bind();
        rig.Settle();
        var session = Track(playing: false);
        rig.Sessions.Add(session);
        Assert.False(rig.Model.Open);

        session.ApplyPlayback(Playback(playing: true));
        Assert.Equal("media", rig.Model.Mode);
        Assert.Equal("Playing Song, Artist", rig.Model.Announcement);
        Assert.Equal(80, rig.Model.PanelHeight);
        rig.Model.TogglePlayback();
        Assert.NotNull(session.Sent);
        Assert.Contains("pause", session.Sent);
        rig.Model.Previous();
        Assert.DoesNotContain("previous", session.Sent);
    }

    private static void AssertPlaced()
    {
        Assert.Equal(360, OnScreenDisplayModel.PanelWidth);
        Assert.Equal(8, OnScreenDisplayModel.BottomGap);
    }

    private static void Settle(FakeTimeProvider time) => time.Advance(TimeSpan.FromMilliseconds(OnScreenDisplayModel.SettleMs));

    private static void Hide(OnScreenDisplayModel model, FakeTimeProvider time)
    {
        model.PointerInside = false;
        time.Advance(TimeSpan.FromMilliseconds(6000));
        Assert.False(model.Open);
    }

    private static ScreenInfo Primary() => Screen("DISPLAY1", "Monitor A", true, 0);

    private static ScreenInfo Secondary() => Screen("DISPLAY2", "Monitor B", false, 1920);

    private static ScreenInfo Screen(string name, string model, bool primary, double x) =>
        new(@"\\.\" + name, name, model, "", "", new QRect(x, 0, 1920, 1080), new QRect(x, 0, 1920, 1040), 1, 0, 0, 60, primary);

    private static BrightnessInfo Monitor(string deviceName, string name, int brightness, int minimum = 0, int maximum = 100) =>
        new(deviceName, name, BrightnessConnection.External, minimum, maximum, brightness);

    private static SmtcSession Track(bool playing)
    {
        var session = SmtcSession.CreateForTests("Player");
        session.ApplyTrack("Song", "Artist", "", "", 0, null, null);
        session.ApplyPlayback(Playback(playing));
        return session;
    }

    private static SmtcPlayback Playback(bool playing) => new(playing
        ? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
        : GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
    {
        CanPlay = true,
        CanPause = true,
        CanGoNext = true,
    };

    /// <summary>The services the panel follows. Nothing here reads Windows.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Loop = new ManualEventLoop();
            var log = new Log { Sink = static (_, _, _) => { } };
            Graph = new FakeAudioGraph();
            BrightnessSource = new FakeSource<BrightnessSnapshot>(new BrightnessSnapshot(true, []));
            Sessions = new SessionList();
            Audio = new AudioService(Loop, log, Graph);
            Brightness = new BrightnessService(Loop, log, BrightnessSource, new BrightnessWriter(static (_, _) => 0u));
            Media = new MediaService(Loop, log, Sessions);
            Screens.Add(Primary());
            Screens.Add(Secondary());
            Time = new FakeTimeProvider();
            Model = new OnScreenDisplayModel(Time) { AnimationsEnabled = false };
        }

        public ManualEventLoop Loop { get; }

        public FakeAudioGraph Graph { get; }

        public FakeSource<BrightnessSnapshot> BrightnessSource { get; }

        public SessionList Sessions { get; }

        public AudioService Audio { get; }

        public BrightnessService Brightness { get; }

        public MediaService Media { get; }

        public ObservableCollection<ScreenInfo> Screens { get; } = new();

        public FakeTimeProvider Time { get; }

        public OnScreenDisplayModel Model { get; }

        public void Bind() => _ = new OnScreenDisplayHost(Model, Audio, Brightness, Media, Screens);

        public void Settle()
        {
            OnScreenDisplayTests.Settle(Time);
            Assert.False(Model.Open);
        }

        public void Push(params BrightnessInfo[] monitors)
        {
            BrightnessSource.Push(new BrightnessSnapshot(true, monitors));
            Loop.Process();
        }

        public void Dispose()
        {
            Audio.Dispose();
            Brightness.Dispose();
            Media.Dispose();
        }
    }

    private sealed class SessionList : IMediaSessions
    {
        private readonly List<SmtcSession> _sessions = [];

        public IReadOnlyList<SmtcSession> Sessions => _sessions;

        public event Action<SmtcSession>? SessionAdded;

#pragma warning disable CS0067 // MediaService subscribes. These tests do not remove a session.
        public event Action<SmtcSession>? SessionRemoved;
#pragma warning restore CS0067

        public void Add(SmtcSession session)
        {
            _sessions.Add(session);
            SessionAdded?.Invoke(session);
        }
    }
}
