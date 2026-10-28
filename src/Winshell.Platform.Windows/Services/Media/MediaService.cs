using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Media;

/// <summary>The <c>Media</c> singleton: a player for each media session Windows knows.</summary>
public sealed class MediaService : QObject
{
    private readonly SmtcSessions _sessions;
    private readonly Dictionary<SmtcSession, MediaSession> _players = [];

    public MediaService(QmlEngine engine)
    {
        Engine = engine;
        _sessions = SmtcSessions.Get(engine.Loop, engine.Log);
        Players = new ObjectModel { Engine = engine };
        Players.SetOwner(this);
        foreach (var session in _sessions.Sessions)
            OnSessionAdded(session);
        _sessions.SessionAdded += OnSessionAdded;
        _sessions.SessionRemoved += OnSessionRemoved;
    }

    [Qml]
    public ObjectModel Players { get; }

    private void OnSessionAdded(SmtcSession session)
    {
        var player = new MediaSession(session) { Engine = Engine };
        player.SetOwner(this);
        _players[session] = player;
        Players.Add(player);
    }

    private void OnSessionRemoved(SmtcSession session)
    {
        if (!_players.Remove(session, out var player))
            return;
        Players.Remove(player);
        player.Destroy();
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
            .Add(StandardModules.EnumType("MediaLoopState", [new("None", 0), new("Track", 1), new("Playlist", 2)], module.Name));
    }
}

/// <summary>
/// The <c>MediaSession</c>, backed by a Windows media session. Windows sessions have no volume, fullscreen,
/// quit or open-uri controls, so those report as unsupported.
/// </summary>
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

    [Qml] public bool CanControl => true;

    [Qml] public bool CanPlay { get { Track(); return _session.CanPlay; } }

    [Qml] public bool CanPause { get { Track(); return _session.CanPause; } }

    [Qml]
    public bool CanTogglePlaying
    {
        get { Track(); return IsPlayingNow ? _session.CanPause : _session.CanPlay; }
    }

    [Qml] public bool CanSeek { get { Track(); return _session.CanSeek; } }

    [Qml] public bool CanGoNext { get { Track(); return _session.CanGoNext; } }

    [Qml] public bool CanGoPrevious { get { Track(); return _session.CanGoPrevious; } }

    /// <summary>Windows cannot ask an app to quit.</summary>
    [Qml] public bool CanQuit => false;

    /// <summary>True for Store apps, which Windows can bring forward, and programs with a window.</summary>
    [Qml] public bool CanRaise => _session.AppId.Contains('!') || MainWindow() != 0;

    /// <summary>Windows media sessions have no fullscreen control.</summary>
    [Qml] public bool CanSetFullscreen => false;

    [Qml] public string Identity => _session.Identity;

    /// <summary>The app's id: the lowercased program name, such as <c>spotify</c>, or a Store app's AppUserModelID.</summary>
    [Qml] public string DesktopEntry => _session.DesktopEntry;

    /// <summary>There is no DBus on Windows; this is a name made from the app in the same form.</summary>
    [Qml] public string DbusName => _session.DbusName;

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

    /// <summary>Windows media sessions have no volume of their own.</summary>
    [Qml]
    public double Volume
    {
        get => 1;
        set => Warn("Cannot set volume of {0} because volume is not supported.");
    }

    [Qml] public bool VolumeSupported => false;

    [Qml]
    public IReadOnlyDictionary<string, object?> Metadata
    {
        get
        {
            Track();
            var metadata = new Dictionary<string, object?>
            {
                ["mpris:trackid"] = $"/org/winshell/track/{_session.UniqueId}",
                ["xesam:title"] = _session.Title,
                ["xesam:artist"] = _session.Artist.Length > 0 ? new List<object?> { _session.Artist } : new List<object?>(),
                ["xesam:album"] = _session.Album,
                ["xesam:albumArtist"] = _session.AlbumArtist.Length > 0 ? new List<object?> { _session.AlbumArtist } : new List<object?>(),
            };
            if (_session.ArtUrl.Length > 0)
                metadata["mpris:artUrl"] = _session.ArtUrl;
            if (_session.LengthSupported)
                metadata["mpris:length"] = (long)(_session.Length * 1_000_000);
            if (_session.TrackNumber > 0)
                metadata["xesam:trackNumber"] = _session.TrackNumber;
            if (_session.Genres.Count > 0)
                metadata["xesam:genre"] = _session.Genres.Cast<object?>().ToList();
            return metadata;
        }
    }

    [Qml] public uint UniqueId { get { Track(); return _session.UniqueId; } }

    [Qml] public string TrackTitle { get { Track(); return _session.Title; } }

    [Qml] public string TrackArtist { get { Track(); return _session.Artist; } }

    [Qml] public string TrackArtists { get { Track(); return _session.Artist; } }

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
    public bool Fullscreen
    {
        get => false;
        set => Warn("Cannot set fullscreen for {0} because canSetFullscreen is false.");
    }

    [Qml] public IReadOnlyList<string> SupportedUriSchemes => [];

    [Qml] public IReadOnlyList<string> SupportedMimeTypes => [];

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

    [Qml]
    public void Raise()
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
            Warn("Cannot call raise() on {0} because canRaise is false.");
        }
    }

    [Qml]
    public void Quit() => Warn("Cannot call quit() on {0} because canQuit is false.");

    /// <summary>Windows media sessions cannot open uris; like many MPRIS players, this ignores the call.</summary>
    [Qml]
    public void OpenUri(string uri) => _ = uri;

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
        (Engine?.Log ?? Log.Default).Warn("media", string.Format(System.Globalization.CultureInfo.InvariantCulture, message, Identity));

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
