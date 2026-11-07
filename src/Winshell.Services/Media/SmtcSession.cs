using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Winshell.Media;

/// <summary>What one media session says about itself, kept up to date from the system media controls.</summary>
internal sealed class SmtcSession
{
    private readonly ISmtcLink _link;
    private readonly SessionArt _art;
    private readonly TimeProvider _time;
    private bool _released;
    private DateTimeOffset _positionTime;
    private double _position;

    public SmtcSession(IEventLoop loop, Log log, GlobalSystemMediaTransportControlsSession session, int id,
        string appId, string appName, TimeProvider time)
        : this(loop, log, id, appId, appName, time, self => new SmtcSessionLink(loop, log, self, session, time))
    {
    }

    // A test gives it a link that records commands, and a clock it advances.
    internal SmtcSession(IEventLoop loop, Log log, int id, string appId, string appName, TimeProvider time,
        Func<SmtcSession, ISmtcLink> link)
    {
        Id = id;
        AppId = appId;
        AppName = appName;
        _time = time;
        _positionTime = time.GetUtcNow();
        _art = new SessionArt(loop, reason => log.Warn("media", $"Cannot read art from {AppName}: {reason}"),
            () => Changed?.Invoke());
        _link = link(this);
    }

    /// <summary>
    /// The app id Windows reports for a session, or null when the session has closed. The same session gets a new
    /// object on every list.
    /// </summary>
    internal static string? IdOf(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.SourceAppUserModelId ?? "";
        }
        // Windows throws COMException for a session whose app closed after the list was taken.
        catch (COMException)
        {
            return null;
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
        Changed?.Invoke();
    }

    public string Title { get; private set; } = "";

    public string Artist { get; private set; } = "";

    public string Album { get; private set; } = "";

    public string AlbumArtist { get; private set; } = "";

    public IReadOnlyList<string> Genres { get; private set; } = [];

    public int TrackNumber { get; private set; }

    public string ArtUrl => _art.Url;

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

    public MediaLoopState LoopState { get; private set; }

    public double Rate { get; private set; } = 1;

    /// <summary>The track length in seconds, or 0 when the app does not say.</summary>
    public double Length { get; private set; }

    public bool LengthSupported => Length > 0;

    /// <summary>The app reports a timeline. Apps that do not report all zeros.</summary>
    public bool PositionSupported { get; private set; }

    public TimeSpan StartTime { get; private set; }

    /// <summary>
    /// Windows reported new values, some of which may be the same as before. A new track has a new
    /// <see cref="UniqueId"/>.
    /// </summary>
    public event Action? Changed;

    /// <summary>The position jumped: a seek, a new track, or a pause or play.</summary>
    public event Action? Seeked;

    /// <summary>The position now, in seconds, counting forward from the app's last report while playing.</summary>
    public double PositionNow()
    {
        if (!PositionSupported || PlaybackState == MediaPlaybackState.Stopped)
            return 0;
        var position = _position;
        if (PlaybackState == MediaPlaybackState.Playing)
            position += (_time.GetUtcNow() - _positionTime).TotalSeconds * Rate;
        return Length > 0 ? Math.Clamp(position, 0, Length) : Math.Max(0, position);
    }

    public void Start()
    {
        _link.Start();
    }

    /// <summary>Follows the new object Windows returned for this same session, without dropping the read in progress.</summary>
    public void Retarget(GlobalSystemMediaTransportControlsSession session)
    {
        _link.Retarget(session);
    }

    public void Release()
    {
        _released = true;
        _link.Release();
        _art.Release();
    }

    public void Play()
    {
        Send("play", s => s.TryPlayAsync());
    }

    public void Pause()
    {
        Send("pause", s => s.TryPauseAsync());
    }

    public void Stop()
    {
        Send("stop", s => s.TryStopAsync());
    }

    public void Next()
    {
        Send("next", s => s.TrySkipNextAsync());
    }

    public void Previous()
    {
        Send("previous", s => s.TrySkipPreviousAsync());
    }

    public void SetShuffle(bool shuffle)
    {
        Send("set shuffle", s => s.TryChangeShuffleActiveAsync(shuffle));
    }

    public void SetLoopState(MediaLoopState state)
    {
        Send("set loopState", s => s.TryChangeAutoRepeatModeAsync(SmtcPlayback.ModeOf(state)));
    }

    public void SetRate(double rate)
    {
        Send("set rate", s => s.TryChangePlaybackRateAsync(rate));
    }

    public void SetPosition(double seconds)
    {
        seconds = Length > 0 ? Math.Clamp(seconds, 0, Length) : Math.Max(0, seconds);
        Send("set position", s => s.TryChangePlaybackPositionAsync((StartTime + TimeSpan.FromSeconds(seconds)).Ticks));
        // Show the new position now, as the app may report it late or not at all.
        _position = seconds;
        _positionTime = _time.GetUtcNow();
        Seeked?.Invoke();
    }

    private void Send(string what, Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        _link.Send(what, command);
    }

    internal void ApplyTrack(string title, string artist, string album, string albumArtist, int trackNumber,
        IReadOnlyList<string>? genres,
        IRandomAccessStreamReference? thumbnail)
    {
        if (title == Title && artist == Artist && album == Album && albumArtist == AlbumArtist
            && TrackNumber == trackNumber && SameGenres(genres))
        {
            // The title can land before the thumbnail. A later read with the same title still has to fetch art.
            if (ArtUrl.Length == 0 && thumbnail is not null && !_art.Loading)
                _art.Load(thumbnail);
            return;
        }

        var newTrack = title != Title || artist != Artist || album != Album;
        if (newTrack)
            UniqueId++;
        Title = title;
        Artist = artist;
        Album = album;
        AlbumArtist = albumArtist;
        Genres = genres?.ToArray() ?? [];
        TrackNumber = trackNumber;
        Changed?.Invoke();
        if (newTrack)
            Seeked?.Invoke();
        _art.Load(thumbnail);
    }

    internal void ApplyPlayback(SmtcPlayback info)
    {
        var state = info.Status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            // "Changing" is a moment between tracks: keep the state it had, so it does not flicker.
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState,
            _ => MediaPlaybackState.Stopped
        };
        var seeked = false;
        if (state != PlaybackState || info.Rate != Rate)
        {
            // Count the position up to now with the old state, then from now with the new one.
            _position = PositionNow();
            _positionTime = _time.GetUtcNow();
            seeked = true;
        }

        PlaybackState = state;
        CanPlay = info.CanPlay;
        CanPause = info.CanPause;
        CanStop = info.CanStop;
        CanGoNext = info.CanGoNext;
        CanGoPrevious = info.CanGoPrevious;
        CanSeek = info.CanSeek;
        ShuffleSupported = info.ShuffleSupported;
        LoopSupported = info.LoopSupported;
        RateSupported = info.RateSupported;
        Shuffle = info.ShuffleSupported && info.Shuffle;
        LoopState = info.LoopSupported ? info.LoopState : MediaLoopState.None;
        Rate = info.Rate;
        Changed?.Invoke();
        if (seeked)
            Seeked?.Invoke();
    }

    internal void ApplyTimeline(TimeSpan start, TimeSpan end, TimeSpan reported, DateTimeOffset updated,
        DateTimeOffset received)
    {
        var length = Math.Max(0, (end - start).TotalSeconds);
        var supported = length > 0 || reported > TimeSpan.Zero;
        var position = (reported - start).TotalSeconds;
        // A poll repeats the last report. Applying it again restarts interpolation and looks like a seek.
        if (StartTime == start && Length == length && PositionSupported == supported
            && Math.Abs(_position - position) < 0.001)
            return;
        StartTime = start;
        PositionSupported = supported;
        Length = length;
        // The app says when it took the position; trust that unless the time is clearly wrong.
        var time = updated.Year > 2000 && updated <= received && received - updated < TimeSpan.FromHours(1)
            ? updated
            : received;
        _position = position;
        _positionTime = time;
        Changed?.Invoke();
        Seeked?.Invoke();
    }

    private bool SameGenres(IReadOnlyList<string>? genres)
    {
        var count = genres?.Count ?? 0;
        if (count != Genres.Count)
            return false;
        for (var i = 0; i < count; i++)
            if (genres![i] != Genres[i])
                return false;
        return true;
    }
}
