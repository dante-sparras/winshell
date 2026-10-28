using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Media;

/// <summary>The <c>Media</c> singleton: one <c>MediaSession</c> for each session the system media controls know.</summary>
public sealed class MediaService : QObject
{
    private readonly SmtcSessions _sessions;
    private readonly Dictionary<SmtcSession, MediaSession> _sessionsByNative = [];

    public MediaService(QmlEngine engine)
    {
        Engine = engine;
        _sessions = SmtcSessions.Get(engine.Loop, engine.Log);
        Sessions = new ObjectModel { Engine = engine };
        Sessions.SetOwner(this);
        foreach (var session in _sessions.Sessions)
            OnSessionAdded(session);
        _sessions.SessionAdded += OnSessionAdded;
        _sessions.SessionRemoved += OnSessionRemoved;
    }

    /// <summary>The media sessions Windows shows in the media controls.</summary>
    [Qml]
    public ObjectModel Sessions { get; }

    private void OnSessionAdded(SmtcSession session)
    {
        var wrapper = new MediaSession(session) { Engine = Engine };
        wrapper.SetOwner(this);
        _sessionsByNative[session] = wrapper;
        Sessions.Add(wrapper);
    }

    private void OnSessionRemoved(SmtcSession session)
    {
        if (!_sessionsByNative.Remove(session, out var wrapper))
            return;
        Sessions.Remove(wrapper);
        wrapper.Destroy();
    }

    protected override void OnDestroy()
    {
        _sessions.SessionAdded -= OnSessionAdded;
        _sessions.SessionRemoved -= OnSessionRemoved;
    }

    internal static void Register(ModuleRegistry registry)
    {
        var module = registry.Module("Winshell.Media");
        module.Singleton(e => new MediaService(e), "Media")
            .Uncreatable<MediaSession>("MediaSession", "MediaSessions can only be acquired from Media")
            .Add(StandardModules.EnumType("MediaPlaybackState", [new("Stopped", 0), new("Playing", 1), new("Paused", 2)], module.Name))
            .Add(StandardModules.EnumType("MediaLoopState", [new("None", 0), new("Track", 1), new("List", 2)], module.Name));
    }
}

/// <summary>The <c>MediaSession</c>, one app's entry in the Windows media controls.</summary>
[QmlSignal("trackChanged")]
[QmlSignal("postTrackChanged")]
public sealed class MediaSession : QObject
{
    // Bindings on position depend on this hidden name, which only changes when positionChanged is emitted, so
    // position updates when the app seeks or when QML emits positionChanged() itself, as QML expects.
    private const string PositionKey = "position:tick";

    private readonly SmtcSession _session;

    internal MediaSession(SmtcSession session)
    {
        _session = session;
        session.Changed += OnChanged;
        session.Seeked += OnSeeked;
        Connect("positionChanged", _ =>
        {
            NotifyChanged(PositionKey);
            if (!_session.LengthSupported)
                NotifyChanged("length");
        });
    }

    [Qml] public bool CanPlay { get { Track(); return _session.CanPlay; } }

    [Qml] public bool CanPause { get { Track(); return _session.CanPause; } }

    [Qml] public bool CanStop { get { Track(); return _session.CanStop; } }

    [Qml]
    public bool CanTogglePlaying
    {
        get { Track(); return IsPlayingNow ? _session.CanPause : _session.CanPlay; }
    }

    [Qml] public bool CanSeek { get { Track(); return _session.CanSeek; } }

    [Qml] public bool CanGoNext { get { Track(); return _session.CanGoNext; } }

    [Qml] public bool CanGoPrevious { get { Track(); return _session.CanGoPrevious; } }

    /// <summary>True when Windows can bring the app's window forward.</summary>
    [Qml] public bool CanSwitchTo => _session.AppId.Contains('!') || MainWindow() != 0;

    /// <summary>The name Windows shows for the app.</summary>
    [Qml] public string AppName => _session.AppName;

    /// <summary>The app's AppUserModelID, such as <c>Spotify.exe</c> or a Store app's id.</summary>
    [Qml] public string AppId => _session.AppId;

    [Qml]
    public double Position
    {
        get { TrackQml(PositionKey); return _session.PositionNow(); }
        set
        {
            if (!_session.PositionSupported)
            {
                Warn("Cannot set position of {0} because position is not supported.");
                return;
            }
            if (!_session.CanSeek)
            {
                Warn("Cannot set position of {0} because canSeek is false.");
                return;
            }
            _session.SetPosition(value);
        }
    }

    [Qml] public bool PositionSupported { get { Track(); return _session.PositionSupported; } }

    [Qml]
    public double Length
    {
        get
        {
            Track();
            if (_session.LengthSupported)
                return _session.Length;
            TrackQml(PositionKey);
            return _session.PositionNow();
        }
    }

    [Qml] public bool LengthSupported { get { Track(); return _session.LengthSupported; } }

    /// <summary>The track as Windows reports it: title, artist, album, art, and duration in seconds.</summary>
    [Qml]
    public IReadOnlyDictionary<string, object?> Metadata
    {
        get
        {
            Track();
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
                metadata["genres"] = _session.Genres.Cast<object?>().ToList();
            return metadata;
        }
    }

    [Qml] public uint UniqueId { get { Track(); return _session.UniqueId; } }

    [Qml] public string TrackTitle { get { Track(); return _session.Title; } }

    [Qml] public string TrackArtist { get { Track(); return _session.Artist; } }

    [Qml] public string TrackAlbum { get { Track(); return _session.Album; } }

    [Qml] public string TrackAlbumArtist { get { Track(); return _session.AlbumArtist; } }

    /// <summary>A <c>file:///</c> url of the art Windows gives, written to the temp folder.</summary>
    [Qml] public string TrackArtUrl { get { Track(); return _session.ArtUrl; } }

    [Qml]
    public int PlaybackState
    {
        get { Track(); return _session.PlaybackState; }
        set
        {
            if (value == _session.PlaybackState)
                return;
            switch (value)
            {
                case MediaPlaybackStates.Stopped:
                    if (!_session.CanStop)
                    {
                        Warn("Cannot set playbackState of {0} to Stopped because canStop is false.");
                        return;
                    }
                    _session.Stop();
                    break;
                case MediaPlaybackStates.Playing:
                    if (!_session.CanPlay)
                    {
                        Warn("Cannot set playbackState of {0} to Playing because canPlay is false.");
                        return;
                    }
                    _session.Play();
                    break;
                case MediaPlaybackStates.Paused:
                    if (!_session.CanPause)
                    {
                        Warn("Cannot set playbackState of {0} to Paused because canPause is false.");
                        return;
                    }
                    _session.Pause();
                    break;
                default:
                    Warn($"Cannot set playbackState of {{0}} to unknown value {value}");
                    break;
            }
        }
    }

    [Qml]
    public bool IsPlaying
    {
        get { Track(); return IsPlayingNow; }
        set
        {
            if (value != IsPlayingNow)
                TogglePlaying();
        }
    }

    private bool IsPlayingNow => _session.PlaybackState == MediaPlaybackStates.Playing;

    [Qml]
    public int LoopState
    {
        get { Track(); return _session.LoopState; }
        set
        {
            if (!_session.LoopSupported)
            {
                Warn("Cannot set loopState of {0} because loop state is not supported.");
                return;
            }
            if (value != _session.LoopState)
                _session.SetLoopState(value);
        }
    }

    [Qml] public bool LoopSupported { get { Track(); return _session.LoopSupported; } }

    [Qml]
    public double Rate
    {
        get { Track(); return _session.Rate; }
        set
        {
            if (value == _session.Rate)
                return;
            if (value < MinRate || value > MaxRate)
            {
                Warn($"Cannot set rate for {{0}} to {value} which is outside of minRate and maxRate {MinRate} {MaxRate}");
                return;
            }
            _session.SetRate(value);
        }
    }

    /// <summary>Windows does not say which rates an app accepts; these are the common ones when it takes any.</summary>
    [Qml] public double MinRate { get { Track(); return _session.RateSupported ? 0.25 : 1; } }

    [Qml] public double MaxRate { get { Track(); return _session.RateSupported ? 4 : 1; } }

    [Qml]
    public bool Shuffle
    {
        get { Track(); return _session.Shuffle; }
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

    [Qml] public bool ShuffleSupported { get { Track(); return _session.ShuffleSupported; } }

    [Qml]
    public void Play() => PlaybackState = MediaPlaybackStates.Playing;

    [Qml]
    public void Pause() => PlaybackState = MediaPlaybackStates.Paused;

    [Qml]
    public void Stop() => PlaybackState = MediaPlaybackStates.Stopped;

    [Qml]
    public void TogglePlaying()
    {
        if (IsPlayingNow)
            Pause();
        else
            Play();
    }

    [Qml]
    public void Next()
    {
        if (!_session.CanGoNext)
        {
            Warn("Cannot call next() on {0} because canGoNext is false.");
            return;
        }
        _session.Next();
    }

    [Qml]
    public void Previous()
    {
        if (!_session.CanGoPrevious)
        {
            Warn("Cannot call previous() on {0} because canGoPrevious is false.");
            return;
        }
        _session.Previous();
    }

    /// <summary>Windows has no relative seek, so this sets the position to the current one plus the offset.</summary>
    [Qml]
    public void Seek(double offset)
    {
        if (!_session.CanSeek)
        {
            Warn("Cannot call seek() on {0} because canSeek is false.");
            return;
        }
        _session.SetPosition(_session.PositionNow() + offset);
    }

    /// <summary>Brings the app's window forward.</summary>
    [Qml]
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{_session.AppId}"))?.Dispose();
        }
        else
        {
            Warn("Cannot call switchTo() on {0} because canSwitchTo is false.");
        }
    }

    /// <summary>The main window of the program behind the session, when its id is a program name.</summary>
    private nint MainWindow()
    {
        var appId = _session.AppId;
        if (!appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return 0;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(appId[..^4]))
        {
            using (process)
            {
                if (process.MainWindowHandle != 0)
                    return process.MainWindowHandle;
            }
        }
        return 0;
    }

    private void Warn(string message) =>
        (Engine?.Log ?? Log.Default).Warn("media", string.Format(System.Globalization.CultureInfo.InvariantCulture, message, AppName));

    private void OnChanged(IReadOnlyList<string> names, bool trackChanged)
    {
        if (trackChanged)
            Emit("trackChanged");
        foreach (var name in names)
            NotifyChanged(name);
        if (trackChanged)
            Emit("postTrackChanged");
    }

    private void OnSeeked() => Emit("positionChanged");

    protected override void OnDestroy()
    {
        _session.Changed -= OnChanged;
        _session.Seeked -= OnSeeked;
    }
}
