using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>What one media session says about itself, kept up to date from the system media controls.</summary>
internal sealed class SmtcSession
{
    private readonly SmtcSessionLink? _link;
    private readonly SessionArt _art;
    private bool _released;
    private DateTimeOffset _positionTime = TimeProvider.System.GetUtcNow();
    private double _position;

    public SmtcSession(IEventLoop loop, Log log, GlobalSystemMediaTransportControlsSession session, int id, string appName)
    {
        Id = id;
        AppId = IdOf(session);
        AppName = appName;
        _art = new SessionArt(loop, reason => log.Warn("media", $"Cannot read art from {AppName}: {reason}"), OnArtChanged);
        _link = new SmtcSessionLink(loop, log, this, session);
    }

    /// <summary>No Windows session. Tests change what this object stores, and its commands land in <see cref="Sent"/>.</summary>
    internal static SmtcSession CreateForTests(string appId = "test") => new(appId);

    private SmtcSession(string appId)
    {
        // A test passes no thumbnail, so the art never reads one and never posts.
        _art = new SessionArt(null!, _ => { }, OnArtChanged);
        AppId = appId;
        AppName = appId;
        Sent = [];
    }

    /// <summary>What a test session was asked to do. Null on a real one.</summary>
    internal List<string>? Sent { get; }

    /// <summary>Sets the playback commands this session accepts and reports which of them changed.</summary>
    internal void SetControls(bool canPlay = false, bool canPause = false, bool canStop = false,
        bool canGoNext = false, bool canGoPrevious = false, bool canSeek = false)
    {
        var changed = new List<string>();
        Set(canPlay, CanPlay, v => CanPlay = v, "canPlay", changed);
        Set(canPause, CanPause, v => CanPause = v, "canPause", changed);
        Set(canStop, CanStop, v => CanStop = v, "canStop", changed);
        Set(canGoNext, CanGoNext, v => CanGoNext = v, "canGoNext", changed);
        Set(canGoPrevious, CanGoPrevious, v => CanGoPrevious = v, "canGoPrevious", changed);
        Set(canSeek, CanSeek, v => CanSeek = v, "canSeek", changed);
        if (changed.Count == 0)
            return;
        changed.Add("canTogglePlaying");
        Changed?.Invoke(changed, false);
    }

    /// <summary>The app id Windows reports for a session. The same session gets a new object on every list.</summary>
    internal static string IdOf(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.SourceAppUserModelId ?? "";
        }
        catch (COMException)
        {
            return "";
        }
    }

    public int Id { get; }

    /// <summary>The app's AppUserModelID, such as <c>Spotify.exe</c> or a Store app's id.</summary>
    public string AppId { get; }

    /// <summary>The name Windows shows for the app.</summary>
    public string AppName { get; private set; }

    /// <summary>The shell finished naming this app. The session is already shown.</summary>
    internal void Rename(string name)
    {
        if (_released || name.Length == 0 || name == AppName)
            return;
        AppName = name;
        Changed?.Invoke(["appName"], false);
    }

    public string Title { get; private set; } = "";

    public string Artist { get; private set; } = "";

    public string Album { get; private set; } = "";

    public string AlbumArtist { get; private set; } = "";

    public IReadOnlyList<string> Genres { get; private set; } = [];

    public int TrackNumber { get; private set; }

    public string ArtUrl => _art.Url;

    /// <summary>An art read is running.</summary>
    internal bool ArtLoading => _art.Loading;

    public uint UniqueId { get; private set; }

    public MediaPlaybackState PlaybackState { get; private set; }

    public bool CanPlay { get; private set; }

    public bool CanPause { get; private set; }

    public bool CanStop { get; private set; }

    public bool CanGoNext { get; private set; }

    public bool CanGoPrevious { get; private set; }

    public bool CanSeek { get; private set; }

    public bool ShuffleSupported { get; private set; }

    public bool LoopSupported { get; private set; }

    public bool RateSupported { get; private set; }

    public bool Shuffle { get; private set; }

    /// <summary>The <c>MediaLoopState</c>.</summary>
    public int LoopState { get; private set; }

    public double Rate { get; private set; } = 1;

    /// <summary>The track length in seconds, or 0 when the app does not say.</summary>
    public double Length { get; private set; }

    public bool LengthSupported => Length > 0;

    /// <summary>The app reports a timeline. Apps that do not report all zeros.</summary>
    public bool PositionSupported { get; private set; }

    public TimeSpan StartTime { get; private set; }

    /// <summary>
    /// Values changed: the names of the changed properties, and whether the track changed, in which case
    /// <c>trackChanged</c> comes before the values and <c>postTrackChanged</c> after.
    /// </summary>
    public event Action<IReadOnlyList<string>, bool>? Changed;

    /// <summary>The position jumped: a seek, a new track, or a pause or play.</summary>
    public event Action? Seeked;

    /// <summary>The position now, in seconds, counting forward from the app's last report while playing.</summary>
    public double PositionNow()
    {
        if (!PositionSupported || PlaybackState == MediaPlaybackState.Stopped)
            return 0;
        var position = _position;
        if (PlaybackState == MediaPlaybackState.Playing)
            position += (TimeProvider.System.GetUtcNow() - _positionTime).TotalSeconds * Rate;
        return Length > 0 ? Math.Clamp(position, 0, Length) : Math.Max(0, position);
    }

    public void Start() => _link?.Start();

    /// <summary>Follows the new object Windows returned for this same session, without dropping the read in progress.</summary>
    public void Retarget(GlobalSystemMediaTransportControlsSession session) => _link?.Retarget(session);

    public void Release()
    {
        _released = true;
        _link?.Release();
        _art.Release();
    }

    // Commands. Each is asynchronous; Windows' events bring the result.

    public void Play() => Send("play", s => s.TryPlayAsync());

    public void Pause() => Send("pause", s => s.TryPauseAsync());

    public void Stop() => Send("stop", s => s.TryStopAsync());

    public void Next() => Send("next", s => s.TrySkipNextAsync());

    public void Previous() => Send("previous", s => s.TrySkipPreviousAsync());

    public void SetShuffle(bool shuffle) => Send("set shuffle", s => s.TryChangeShuffleActiveAsync(shuffle));

    public void SetLoopState(int state) => Send("set loopState", s => s.TryChangeAutoRepeatModeAsync((MediaPlaybackAutoRepeatMode)state));

    public void SetRate(double rate) => Send("set rate", s => s.TryChangePlaybackRateAsync(rate));

    public void SetPosition(double seconds)
    {
        seconds = Length > 0 ? Math.Clamp(seconds, 0, Length) : Math.Max(0, seconds);
        Send("set position", s => s.TryChangePlaybackPositionAsync((StartTime + TimeSpan.FromSeconds(seconds)).Ticks));
        // Show the new position now, as the app may report it late or not at all.
        _position = seconds;
        _positionTime = TimeProvider.System.GetUtcNow();
        Seeked?.Invoke();
    }

    private void Send(string what, Func<GlobalSystemMediaTransportControlsSession, global::Windows.Foundation.IAsyncOperation<bool>> command)
    {
        if (_link is not null)
            _link.Send(what, command);
        else
            Sent?.Add(what);
    }

    // Updates from Windows

    internal void ApplyTrack(string title, string artist, string album, string albumArtist, int trackNumber, IReadOnlyList<string>? genres,
        global::Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (title == Title && artist == Artist && album == Album && albumArtist == AlbumArtist
            && TrackNumber == trackNumber && SameGenres(genres))
        {
            // The title can land before the thumbnail. A later read with the same title still has to fetch art.
            if (ArtUrl.Length == 0 && thumbnail is not null && !_art.Loading)
                _art.Load(thumbnail);
            return;
        }
        var trackChanged = title != Title || artist != Artist || album != Album;
        var changed = new List<string>();
        if (trackChanged)
        {
            UniqueId++;
            changed.Add("uniqueId");
        }
        Set(title, Title, v => Title = v, "trackTitle", changed);
        Set(artist, Artist, v => Artist = v, "trackArtist", changed);
        Set(album, Album, v => Album = v, "trackAlbum", changed);
        Set(albumArtist, AlbumArtist, v => AlbumArtist = v, "trackAlbumArtist", changed);
        Genres = genres?.ToArray() ?? [];
        TrackNumber = trackNumber;
        changed.Add("metadata");
        Changed?.Invoke(changed, trackChanged);
        if (trackChanged)
            Seeked?.Invoke();
        _art.Load(thumbnail);
    }

    private void OnArtChanged() => Changed?.Invoke(["trackArtUrl", "metadata"], false);

    internal void ApplyPlayback(SmtcPlayback info)
    {
        var changed = new List<string>();
        var state = info.Status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            // "Changing" is a moment between tracks: keep the state it had, so it does not flicker.
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState,
            _ => MediaPlaybackState.Stopped,
        };
        var seeked = false;
        if (state != PlaybackState || info.Rate != Rate)
        {
            // Count the position up to now with the old state, then from now with the new one.
            _position = PositionNow();
            _positionTime = TimeProvider.System.GetUtcNow();
            seeked = true;
        }
        Set(state, PlaybackState, v => PlaybackState = v, "playbackState", changed);
        if (changed.Contains("playbackState"))
            changed.Add("isPlaying");
        Set(info.CanPlay, CanPlay, v => CanPlay = v, "canPlay", changed);
        Set(info.CanPause, CanPause, v => CanPause = v, "canPause", changed);
        Set(info.CanStop, CanStop, v => CanStop = v, "canStop", changed);
        Set(info.CanGoNext, CanGoNext, v => CanGoNext = v, "canGoNext", changed);
        Set(info.CanGoPrevious, CanGoPrevious, v => CanGoPrevious = v, "canGoPrevious", changed);
        Set(info.CanSeek, CanSeek, v => CanSeek = v, "canSeek", changed);
        Set(info.ShuffleSupported, ShuffleSupported, v => ShuffleSupported = v, "shuffleSupported", changed);
        Set(info.LoopSupported, LoopSupported, v => LoopSupported = v, "loopSupported", changed);
        Set(info.RateSupported, RateSupported, v => RateSupported = v, "minRate", changed);
        if (changed.Contains("minRate"))
            changed.Add("maxRate");
        Set(ShuffleSupported && info.Shuffle, Shuffle, v => Shuffle = v, "shuffle", changed);
        Set(LoopSupported ? info.LoopState : 0, LoopState, v => LoopState = v, "loopState", changed);
        Set(info.Rate, Rate, v => Rate = v, "rate", changed);
        if (changed.Count == 0)
            return;
        changed.Add("canTogglePlaying");
        Changed?.Invoke(changed, false);
        if (seeked)
            Seeked?.Invoke();
    }

    internal void ApplyTimeline(TimeSpan start, TimeSpan end, TimeSpan reported, DateTimeOffset updated, DateTimeOffset received)
    {
        var length = Math.Max(0, (end - start).TotalSeconds);
        var supported = length > 0 || reported > TimeSpan.Zero;
        var position = (reported - start).TotalSeconds;
        // A poll repeats the last report. Applying it again restarts interpolation and looks like a seek.
        if (StartTime == start && Length == length && PositionSupported == supported
            && Math.Abs(_position - position) < 0.001)
            return;
        var changed = new List<string>();
        StartTime = start;
        Set(supported, PositionSupported, v => PositionSupported = v, "positionSupported", changed);
        Set(length, Length, v => Length = v, "length", changed);
        if (changed.Contains("length"))
            changed.Add("lengthSupported");
        // The app says when it took the position; trust that unless the time is clearly wrong.
        var time = updated.Year > 2000 && updated <= received && received - updated < TimeSpan.FromHours(1) ? updated : received;
        _position = position;
        _positionTime = time;
        if (changed.Count > 0)
            Changed?.Invoke(changed, false);
        Seeked?.Invoke();
    }

    private bool SameGenres(IReadOnlyList<string>? genres)
    {
        var count = genres?.Count ?? 0;
        if (count != Genres.Count)
            return false;
        for (var i = 0; i < count; i++)
        {
            if (genres![i] != Genres[i])
                return false;
        }
        return true;
    }

    private static void Set<T>(T value, T current, Action<T> assign, string name, List<string> changed)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
            return;
        assign(value);
        changed.Add(name);
    }
}
