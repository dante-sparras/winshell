using System.Diagnostics;
using System.Globalization;
using Winshell.Native;

namespace Winshell.Media;

/// <summary>One app's entry in the Windows media controls.</summary>
public sealed class MediaSession : ObservableObject
{
    private readonly SmtcSession _session;
    private readonly Log _log;
    private bool _canControl;
    private bool _canSwitchTo;

    internal MediaSession(SmtcSession session, Log log)
    {
        _session = session;
        _log = log;
        _canControl = AcceptsControl();
        _canSwitchTo = FindCanSwitchTo();
        session.Changed += OnChanged;
        session.Seeked += RefreshPosition;
    }

    internal SmtcSession Native => _session;

    /// <summary>Whether the app accepts <see cref="Play"/>.</summary>
    public bool CanPlay => _session.CanPlay;

    /// <summary>Whether the app accepts <see cref="Pause"/>.</summary>
    public bool CanPause => _session.CanPause;

    /// <summary>Whether the app accepts <see cref="Stop"/>.</summary>
    public bool CanStop => _session.CanStop;

    /// <summary>
    /// Whether <see cref="TogglePlaying"/> can work: <see cref="CanPause"/> while playing, <see cref="CanPlay"/>
    /// otherwise.
    /// </summary>
    public bool CanTogglePlaying => IsPlaying ? _session.CanPause : _session.CanPlay;

    /// <summary>Whether the app accepts a new <see cref="Position"/> and <see cref="Seek"/>.</summary>
    public bool CanSeek => _session.CanSeek;

    /// <summary>Whether the app accepts <see cref="Next"/>.</summary>
    public bool CanGoNext => _session.CanGoNext;

    /// <summary>Whether the app accepts <see cref="Previous"/>.</summary>
    public bool CanGoPrevious => _session.CanGoPrevious;

    /// <summary>True when the session accepts at least one playback command.</summary>
    public bool CanControl => _canControl;

    /// <summary>True when Windows can bring the app's window forward. It is worked out on each read.</summary>
    public bool CanSwitchTo => _canSwitchTo = FindCanSwitchTo();

    /// <summary>The name Windows shows for the app.</summary>
    public string AppName => _session.AppName;

    /// <summary>The app's AppUserModelID, such as <c>Spotify.exe</c> or a Store app's id.</summary>
    public string AppId => _session.AppId;

    /// <summary>
    /// Where playback is in the track, in seconds. It counts forward while playing, but a change is only raised
    /// when the app reports a position, seeks, changes track, or pauses or plays. To show it moving, call
    /// <see cref="RefreshPosition"/> from a timer.
    /// </summary>
    public double Position
    {
        get => _session.PositionNow();
        set
        {
            if (!_session.PositionSupported)
            {
                Warn("Cannot set the position of {0} because position is not supported.");
                return;
            }
            if (!_session.CanSeek)
            {
                Warn("Cannot set the position of {0} because CanSeek is false.");
                return;
            }
            _session.SetPosition(value);
        }
    }

    /// <summary>Whether the app reports where playback is. When false, <see cref="Position"/> is 0.</summary>
    public bool PositionSupported => _session.PositionSupported;

    /// <summary>The track's length in seconds. When the app does not report one, it is the same as <see cref="Position"/>.</summary>
    public double Length => _session.LengthSupported ? _session.Length : _session.PositionNow();

    /// <summary>Whether the app reports the track's length.</summary>
    public bool LengthSupported => _session.LengthSupported;

    /// <summary>
    /// The track as Windows reports it: <c>title</c>, <c>artist</c>, <c>album</c> and <c>albumArtist</c>, and when the
    /// app gives them, <c>artUrl</c>, <c>duration</c> in seconds, <c>trackNumber</c>, and <c>genres</c>, a list.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata
    {
        get
        {
            var metadata = new Dictionary<string, object?>
            {
                ["title"] = _session.Title,
                ["artist"] = _session.Artist,
                ["album"] = _session.Album,
                ["albumArtist"] = _session.AlbumArtist,
            };
            if (_session.ArtUrl.Length > 0)
                metadata["artUrl"] = _session.ArtUrl;
            if (_session.LengthSupported)
                metadata["duration"] = _session.Length;
            if (_session.TrackNumber > 0)
                metadata["trackNumber"] = _session.TrackNumber;
            if (_session.Genres.Count > 0)
                metadata["genres"] = _session.Genres;
            return metadata;
        }
    }

    /// <summary>A number that goes up by one each time the title, artist or album changes.</summary>
    public uint UniqueId => _session.UniqueId;

    /// <summary>The track's title. Empty when the app gives none.</summary>
    public string TrackTitle => _session.Title;

    /// <summary>The track's artist. Empty when the app gives none.</summary>
    public string TrackArtist => _session.Artist;

    /// <summary>The track's album. Empty when the app gives none.</summary>
    public string TrackAlbum => _session.Album;

    /// <summary>The artist of the track's album. Empty when the app gives none.</summary>
    public string TrackAlbumArtist => _session.AlbumArtist;

    /// <summary>A <c>file:///</c> url of the art Windows gives, written to the temp folder.</summary>
    public string TrackArtUrl => _session.ArtUrl;

    /// <summary>Setting it calls <see cref="Play"/>, <see cref="Pause"/> or <see cref="Stop"/>.</summary>
    public MediaPlaybackState PlaybackState
    {
        get => _session.PlaybackState;
        set
        {
            if (value == PlaybackState)
                return;
            switch (value)
            {
                case MediaPlaybackState.Stopped:
                    if (_session.CanStop)
                        _session.Stop();
                    else
                        Warn("Cannot set the playback state of {0} to Stopped because CanStop is false.");
                    break;
                case MediaPlaybackState.Playing:
                    if (_session.CanPlay)
                        _session.Play();
                    else
                        Warn("Cannot set the playback state of {0} to Playing because CanPlay is false.");
                    break;
                case MediaPlaybackState.Paused:
                    if (_session.CanPause)
                        _session.Pause();
                    else
                        Warn("Cannot set the playback state of {0} to Paused because CanPause is false.");
                    break;
                default:
                    Warn($"Cannot set the playback state of {{0}} to unknown value {(int)value}.");
                    break;
            }
        }
    }

    /// <summary>True while playing. Setting it plays or pauses.</summary>
    public bool IsPlaying
    {
        get => PlaybackState == MediaPlaybackState.Playing;
        set
        {
            if (value != IsPlaying)
                TogglePlaying();
        }
    }

    /// <summary>
    /// What the app repeats. <see cref="MediaLoopState.None"/> when <see cref="LoopSupported"/> is false, and then
    /// setting it does nothing.
    /// </summary>
    public MediaLoopState LoopState
    {
        get => (MediaLoopState)_session.LoopState;
        set
        {
            if (!_session.LoopSupported)
            {
                Warn("Cannot set the loop state of {0} because loop state is not supported.");
                return;
            }
            if (value != LoopState)
                _session.SetLoopState((int)value);
        }
    }

    /// <summary>Whether the app accepts a new <see cref="LoopState"/>.</summary>
    public bool LoopSupported => _session.LoopSupported;

    /// <summary>The playback speed; 1 is normal.</summary>
    public double Rate
    {
        get => _session.Rate;
        set
        {
            if (value == _session.Rate)
                return;
            if (value < MinRate || value > MaxRate)
            {
                Warn($"Cannot set the rate of {{0}} to {value.ToString(CultureInfo.InvariantCulture)}, outside of {MinRate.ToString(CultureInfo.InvariantCulture)} to {MaxRate.ToString(CultureInfo.InvariantCulture)}.");
                return;
            }
            _session.SetRate(value);
        }
    }

    /// <summary>Windows does not say which rates an app accepts; these are the common ones when it takes any.</summary>
    public double MinRate => _session.RateSupported ? 0.25 : 1;

    /// <summary>The fastest <see cref="Rate"/> to set: 4 when the app accepts rate changes, otherwise 1.</summary>
    public double MaxRate => _session.RateSupported ? 4 : 1;

    /// <summary>
    /// Whether the app plays in shuffled order. False when <see cref="ShuffleSupported"/> is false, and then setting
    /// it does nothing.
    /// </summary>
    public bool Shuffle
    {
        get => _session.Shuffle;
        set
        {
            if (!_session.ShuffleSupported)
            {
                Warn("Cannot set shuffle for {0} because shuffle is not supported.");
                return;
            }
            _session.SetShuffle(value);
        }
    }

    /// <summary>Whether the app accepts a new <see cref="Shuffle"/>.</summary>
    public bool ShuffleSupported => _session.ShuffleSupported;

    /// <summary>The track changed. It comes before the track's properties are updated.</summary>
    public event Action? TrackChanged;

    /// <summary>The track changed, after the track's properties are updated.</summary>
    public event Action? PostTrackChanged;

    /// <summary>Asks the app to play. Logs a warning when <see cref="CanPlay"/> is false.</summary>
    public void Play() => PlaybackState = MediaPlaybackState.Playing;

    /// <summary>Asks the app to pause. Logs a warning when <see cref="CanPause"/> is false.</summary>
    public void Pause() => PlaybackState = MediaPlaybackState.Paused;

    /// <summary>Asks the app to stop. Logs a warning when <see cref="CanStop"/> is false.</summary>
    public void Stop() => PlaybackState = MediaPlaybackState.Stopped;

    /// <summary>Calls <see cref="Pause"/> while playing, and <see cref="Play"/> otherwise.</summary>
    public void TogglePlaying()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    /// <summary>Asks the app to skip to the next track. Logs a warning when <see cref="CanGoNext"/> is false.</summary>
    public void Next()
    {
        if (!_session.CanGoNext)
        {
            Warn("Cannot call Next() on {0} because CanGoNext is false.");
            return;
        }
        _session.Next();
    }

    /// <summary>Asks the app to go back a track. Logs a warning when <see cref="CanGoPrevious"/> is false.</summary>
    public void Previous()
    {
        if (!_session.CanGoPrevious)
        {
            Warn("Cannot call Previous() on {0} because CanGoPrevious is false.");
            return;
        }
        _session.Previous();
    }

    /// <summary>Windows has no relative seek, so this sets the position to the current one plus the offset.</summary>
    public void Seek(double offset)
    {
        if (!_session.CanSeek)
        {
            Warn("Cannot call Seek() on {0} because CanSeek is false.");
            return;
        }
        _session.SetPosition(_session.PositionNow() + offset);
    }

    /// <summary>Brings the app's window forward.</summary>
    public void SwitchTo()
    {
        if (MainWindow() is var window and not 0)
        {
            if (Win32.IsIconic(window))
                Win32.ShowWindow(window, 9); // SW_RESTORE
            Win32.SetForegroundWindow(window);
        }
        else if (_session.AppId.Contains('!'))
        {
            // A Store app is single-instance: starting it again brings its window forward.
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{_session.AppId}"))?.Dispose();
        }
        else
        {
            Warn("Cannot call SwitchTo() on {0} because CanSwitchTo is false.");
        }
    }

    /// <summary>Raises a change of <see cref="Position"/>, so a binding reads where playback has got to.</summary>
    public void RefreshPosition()
    {
        Changed(nameof(Position));
        Changed(!_session.LengthSupported, nameof(Length));
    }

    internal void Detach()
    {
        _session.Changed -= OnChanged;
        _session.Seeked -= RefreshPosition;
    }

    // Windows reports each command on its own. One flag says whether any of them works.
    private bool AcceptsControl() =>
        _session.CanPlay || _session.CanPause || _session.CanStop
        || _session.CanSeek || _session.CanGoNext || _session.CanGoPrevious;

    private bool FindCanSwitchTo() => _session.AppId.Contains('!') || MainWindow() != 0;

    /// <summary>The main window of the program behind the session, when its id is a program name.</summary>
    private nint MainWindow()
    {
        var appId = _session.AppId;
        if (!appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return 0;
        foreach (var process in Process.GetProcessesByName(appId[..^4]))
        {
            using (process)
            {
                if (process.MainWindowHandle != 0)
                    return process.MainWindowHandle;
            }
        }
        return 0;
    }

    private void Warn(string message) => _log.Warn("media", string.Format(CultureInfo.InvariantCulture, message, AppName));

    private void OnChanged(IReadOnlyList<string> names, bool trackChanged)
    {
        if (trackChanged)
            TrackChanged?.Invoke();
        foreach (var name in names)
            Changed(char.ToUpperInvariant(name[0]) + name[1..]);
        Set(ref _canControl, AcceptsControl(), nameof(CanControl));
        Set(ref _canSwitchTo, FindCanSwitchTo(), nameof(CanSwitchTo));
        if (trackChanged)
            PostTrackChanged?.Invoke();
    }
}
