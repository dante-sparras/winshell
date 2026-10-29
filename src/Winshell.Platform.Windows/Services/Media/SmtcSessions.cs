using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;
using Windows.Media;
using Windows.Media.Control;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Media;

/// <summary>
/// The media sessions Windows shows in its media flyout (the Global System Media Transport Controls), one per
/// app that plays media. One list serves the whole process, so a reload does not ask Windows again. It lives on
/// the UI thread; Windows' events arrive on other threads and are posted to it.
/// </summary>
internal sealed class SmtcSessions
{
    private static SmtcSessions? _instance;

    private readonly IEventLoop _loop;
    private readonly List<SmtcSession> _sessions = [];
    private readonly AppNames _names;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private global::Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs>? _sessionsChanged;
    private Timer? _refresh;
    private int _syncPosted;
    private global::Windows.Foundation.IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager>? _pending;
    private int _pendingSince;
    private int _pendingThread = -1;
    private int _nextId = 1;

    private SmtcSessions(IEventLoop loop, Log log)
    {
        _loop = loop;
        Log = log;
        _names = new AppNames(loop, log, SmtcSession.DescribeApp, Sync);
        // Old art is older than a day, so cleaning it does not have to finish before the first read.
        _ = Task.Run(ArtCache.CleanOld);
        Post(Begin);
    }

    /// <summary>The session list, made on first use. Call on the UI thread.</summary>
    public static SmtcSessions Get(IEventLoop loop, Log log) => _instance ??= new SmtcSessions(loop, log);

    public IReadOnlyList<SmtcSession> Sessions => _sessions;

    public Log Log { get; }

    public event Action<SmtcSession>? SessionAdded;

    public event Action<SmtcSession>? SessionRemoved;

    internal void Post(Action action) => _loop.Post(action);

    private void Begin()
    {
        // SessionsChanged is not delivered for every session a later manager already lists. A manager requested
        // on another thread can keep returning an empty list, so each read asks again on the pumping thread.
        _refresh ??= new Timer(_ => RequestSync(), null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
        RequestSync();
    }

    /// <summary>Posts one list read. A loop nobody is pumping must not grow a queue of them.</summary>
    private void RequestSync()
    {
        if (Interlocked.Exchange(ref _syncPosted, 1) == 1)
            return;
        Post(() =>
        {
            Interlocked.Exchange(ref _syncPosted, 0);
            Sync();
        });
    }

    /// <summary>
    /// Asks Windows for a manager on this thread and reads it here. The completion callback is not delivered
    /// on a thread that is not pumping messages, so the status is polled instead, and a request that stays
    /// open is replaced.
    /// </summary>
    private void Sync()
    {
        if (_pending is { } pending && _pendingThread == Environment.CurrentManagedThreadId)
        {
            if (pending.Status == global::Windows.Foundation.AsyncStatus.Completed)
            {
                _pending = null;
                Apply(pending);
                return;
            }
            if (pending.Status == global::Windows.Foundation.AsyncStatus.Started
                && unchecked(Environment.TickCount - _pendingSince) < 1000)
                return;
        }
        _pending = null;

        global::Windows.Foundation.IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager> operation;
        // AsTask captures the test synchronization context. The test thread is blocked in its wait, so that
        // completion never runs and the list stays empty. Ask with no context, then read the result here.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            operation = GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            Log.Warn("media", $"Windows media sessions are not available: {e.Message}");
            return;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var started = Environment.TickCount;
        while (operation.Status == global::Windows.Foundation.AsyncStatus.Started
            && unchecked(Environment.TickCount - started) < 50)
            Thread.Yield();

        if (operation.Status == global::Windows.Foundation.AsyncStatus.Completed)
        {
            Apply(operation);
            return;
        }
        if (operation.Status == global::Windows.Foundation.AsyncStatus.Error)
        {
            LogUnavailable(operation);
            return;
        }
        _pending = operation;
        _pendingSince = Environment.TickCount;
        _pendingThread = Environment.CurrentManagedThreadId;
    }

    private void Apply(global::Windows.Foundation.IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager> operation)
    {
        try
        {
            var manager = operation.GetResults();
            Subscribe(manager);
            Read(manager.GetSessions());
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("media", $"Cannot list media sessions: {e.Message}");
        }
    }

    private void LogUnavailable(global::Windows.Foundation.IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager> operation)
    {
        try
        {
            Log.Warn("media", $"Windows media sessions are not available: {operation.ErrorCode.Message}");
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>The projection does not root the delegate. A collected handler never hears a later session.</summary>
    private void Subscribe(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (ReferenceEquals(_manager, manager))
            return;
        if (_manager is not null && _sessionsChanged is not null)
            _manager.SessionsChanged -= _sessionsChanged;
        _manager = manager;
        _sessionsChanged ??= (_, _) => RequestSync();
        manager.SessionsChanged += _sessionsChanged;
    }

    private void Read(IReadOnlyList<GlobalSystemMediaTransportControlsSession> current)
    {
        // GetSessions returns a new object for a session that is already held, so reference equality
        // drops it and starts again. That cancels the read of its title and art. Match the app id instead.
        var incoming = current.ToList();
        var held = _sessions.ToList();
        var ids = incoming.Select(SmtcSession.IdOf).ToList();
        var pair = Pair(held.Select(s => s.AppId).ToList(), ids);
        var kept = new bool[held.Count];
        for (var i = 0; i < incoming.Count; i++)
        {
            var existing = pair[i];
            if (existing >= 0)
            {
                kept[existing] = true;
                held[existing].Retarget(incoming[i]);
                // The shell name arrives after the session. The UI thread does not wait for that lookup.
                if (_names.Get(ids[i]) is { } name)
                    held[existing].Rename(name);
                continue;
            }
            var session = new SmtcSession(this, incoming[i], _nextId++, _names.Get(ids[i]) ?? SmtcSession.FallbackName(ids[i]));
            _sessions.Add(session);
            SessionAdded?.Invoke(session);
            session.Start();
        }
        for (var i = 0; i < held.Count; i++)
        {
            if (kept[i])
                continue;
            _sessions.Remove(held[i]);
            held[i].Release();
            SessionRemoved?.Invoke(held[i]);
        }
    }

    /// <summary>
    /// Incoming index to held index, or -1 when that incoming session is new. The same app id pairs in
    /// list order, because one app can have more than one session and Windows does not give them an id.
    /// </summary>
    internal static int[] Pair(IReadOnlyList<string> heldAppIds, IReadOnlyList<string> incomingAppIds)
    {
        var pair = new int[incomingAppIds.Count];
        var used = new bool[heldAppIds.Count];
        for (var i = 0; i < incomingAppIds.Count; i++)
        {
            pair[i] = -1;
            for (var h = 0; h < heldAppIds.Count; h++)
            {
                if (used[h] || heldAppIds[h] != incomingAppIds[i])
                    continue;
                used[h] = true;
                pair[i] = h;
                break;
            }
        }
        return pair;
    }
}

/// <summary>What one media session says about itself, kept up to date from the system media controls.</summary>
internal sealed class SmtcSession
{
    private readonly SmtcSessions _owner;
    private int _mediaRequest;
    private int _mediaApplied;
    private int _artRequest;
    private int _artApplied;
    private bool _artMissLogged;
    private bool _released;
    private global::Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? _mediaChanged;
    private global::Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? _playbackChanged;
    private global::Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? _timelineChanged;
    private DateTimeOffset _positionTime = DateTimeOffset.UtcNow;
    private double _position;

    public SmtcSession(SmtcSessions owner, GlobalSystemMediaTransportControlsSession session, int id, string appName)
    {
        _owner = owner;
        Session = session;
        Id = id;
        AppId = IdOf(session);
        AppName = appName;
    }

    public GlobalSystemMediaTransportControlsSession Session { get; private set; }

    /// <summary>The app id Windows reports for a session. The same session gets a new object on every list.</summary>
    internal static string IdOf(GlobalSystemMediaTransportControlsSession session) => SafeAppId(session);

    public int Id { get; }

    /// <summary>The app's AppUserModelID, such as <c>Spotify.exe</c> or a Store app's id.</summary>
    public string AppId { get; }

    /// <summary>The name Windows shows for the app.</summary>
    public string AppName { get; private set; }

    /// <summary>The exe or app id, before the shell lookup replaces it.</summary>
    internal static string FallbackName(string appId) =>
        appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appId[..^4] : appId;

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

    public string ArtUrl { get; private set; } = "";

    public uint UniqueId { get; private set; }

    /// <summary>The <c>MediaPlaybackState</c>.</summary>
    public int PlaybackState { get; private set; }

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
    /// Values changed: the QML names of the changed properties, and whether the track changed, in which case
    /// <c>trackChanged</c> comes before the values and <c>postTrackChanged</c> after.
    /// </summary>
    public event Action<IReadOnlyList<string>, bool>? Changed;

    /// <summary>The position jumped: a seek, a new track, or a pause or play.</summary>
    public event Action? Seeked;

    /// <summary>The position now, in seconds, counting forward from the app's last report while playing.</summary>
    public double PositionNow()
    {
        if (!PositionSupported || PlaybackState == MediaPlaybackStates.Stopped)
            return 0;
        var position = _position;
        if (PlaybackState == MediaPlaybackStates.Playing)
            position += (DateTimeOffset.UtcNow - _positionTime).TotalSeconds * Rate;
        return Length > 0 ? Math.Clamp(position, 0, Length) : Math.Max(0, position);
    }

    public void Start()
    {
        Hook();
        Refresh();
    }

    /// <summary>Follows the new object Windows returned for this same session, without dropping the read in progress.</summary>
    public void Retarget(GlobalSystemMediaTransportControlsSession session)
    {
        if (ReferenceEquals(Session, session))
            return;
        Unhook();
        Session = session;
        Hook();
        OnPlaybackInfoChanged(Session, null);
        OnTimelinePropertiesChanged(Session, null);
        // A metadata or art read is already running. Starting another drops its result.
        if (_mediaApplied == _mediaRequest && _artApplied == _artRequest)
            OnMediaPropertiesChanged(Session, null);
    }

    public void Release()
    {
        _released = true;
        Unhook();
        ArtCache.ReleaseLater(ArtUrl);
    }

    private void Refresh()
    {
        OnPlaybackInfoChanged(Session, null);
        OnTimelinePropertiesChanged(Session, null);
        OnMediaPropertiesChanged(Session, null);
    }

    private void Hook()
    {
        // Kept on the session. The WinRT projection does not root an event delegate, so a collected
        // one stops title, art, and timeline updates with no error.
        _mediaChanged ??= OnMediaPropertiesChanged;
        _playbackChanged ??= OnPlaybackInfoChanged;
        _timelineChanged ??= OnTimelinePropertiesChanged;
        Session.MediaPropertiesChanged += _mediaChanged;
        Session.PlaybackInfoChanged += _playbackChanged;
        Session.TimelinePropertiesChanged += _timelineChanged;
    }

    private void Unhook()
    {
        if (_mediaChanged is not null)
            Session.MediaPropertiesChanged -= _mediaChanged;
        if (_playbackChanged is not null)
            Session.PlaybackInfoChanged -= _playbackChanged;
        if (_timelineChanged is not null)
            Session.TimelinePropertiesChanged -= _timelineChanged;
    }

    // Commands. Each is asynchronous; Windows' events bring the result.

    public void Play() => Run(Session.TryPlayAsync(), "play");

    public void Pause() => Run(Session.TryPauseAsync(), "pause");

    public void Stop() => Run(Session.TryStopAsync(), "stop");

    public void Next() => Run(Session.TrySkipNextAsync(), "next");

    public void Previous() => Run(Session.TrySkipPreviousAsync(), "previous");

    public void SetShuffle(bool shuffle) => Run(Session.TryChangeShuffleActiveAsync(shuffle), "set shuffle");

    public void SetLoopState(int state) => Run(Session.TryChangeAutoRepeatModeAsync((MediaPlaybackAutoRepeatMode)state), "set loopState");

    public void SetRate(double rate) => Run(Session.TryChangePlaybackRateAsync(rate), "set rate");

    public void SetPosition(double seconds)
    {
        seconds = Length > 0 ? Math.Clamp(seconds, 0, Length) : Math.Max(0, seconds);
        Run(Session.TryChangePlaybackPositionAsync((StartTime + TimeSpan.FromSeconds(seconds)).Ticks), "set position");
        // Show the new position now, as the app may report it late or not at all.
        _position = seconds;
        _positionTime = DateTimeOffset.UtcNow;
        Seeked?.Invoke();
    }

    private void Run(global::Windows.Foundation.IAsyncOperation<bool> operation, string what)
    {
        _ = Await();

        async Task Await()
        {
            try
            {
                if (!await operation.AsTask().ConfigureAwait(false))
                    _owner.Post(() => _owner.Log.Warn("media", $"{AppName} did not accept {what}"));
            }
            catch (COMException e)
            {
                _owner.Post(() => _owner.Log.Warn("media", $"Cannot {what} on {AppName}: {e.Message}"));
            }
        }
    }

    // Updates from Windows

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs? args)
    {
        var request = Interlocked.Increment(ref _mediaRequest);
        _ = Read();

        async Task Read()
        {
            try
            {
                var properties = await sender.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
                if (properties is not null)
                    _owner.Post(() => ApplyMedia(properties, request));
            }
            catch (COMException)
            {
                // The session closed while it was being read. Allow a later poll to try again.
                _owner.Post(() =>
                {
                    if (!_released && request == _mediaRequest)
                        _mediaApplied = request;
                });
            }
        }
    }

    private void ApplyMedia(GlobalSystemMediaTransportControlsSessionMediaProperties properties, int request)
    {
        if (_released || request != _mediaRequest)
            return;
        _mediaApplied = request;
        var title = properties.Title ?? "";
        var artist = properties.Artist ?? "";
        var album = properties.AlbumTitle ?? "";
        var albumArtist = properties.AlbumArtist ?? "";
        if (title == Title && artist == Artist && album == Album && albumArtist == AlbumArtist
            && TrackNumber == properties.TrackNumber && SameGenres(properties.Genres))
        {
            // The title can land before the thumbnail. A later read with the same title still has to fetch art.
            if (ArtUrl.Length == 0 && properties.Thumbnail is not null && _artApplied == _artRequest)
                LoadArt(properties.Thumbnail);
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
        Genres = properties.Genres?.ToArray() ?? [];
        TrackNumber = properties.TrackNumber;
        changed.Add("metadata");
        Changed?.Invoke(changed, trackChanged);
        if (trackChanged)
            Seeked?.Invoke();
        LoadArt(properties.Thumbnail);
    }

    private void LoadArt(global::Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        var request = Interlocked.Increment(ref _artRequest);
        if (thumbnail is null)
        {
            SetArt("", request);
            return;
        }
        _ = Load();

        async Task Load()
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync().AsTask().ConfigureAwait(false);
                using var input = stream.AsStreamForRead();
                var url = await ArtCache.StoreAsync(input, stream.ContentType).ConfigureAwait(false);
                if (url is null)
                    Skip("it is not a picture under the size limit");
                else
                    _owner.Post(() => SetArt(url, request));
            }
            catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
            {
                Skip(e.Message);
            }
        }

        // A failed read is not "this track has no art". Clearing the url here blanks art that already arrived.
        void Skip(string reason) => _owner.Post(() =>
        {
            if (request == _artRequest)
                _artApplied = request;
            if (_artMissLogged)
                return;
            _artMissLogged = true;
            _owner.Log.Warn("media", $"Cannot read art from {AppName}: {reason}");
        });
    }

    private void SetArt(string url, int request)
    {
        if (_released || request != _artRequest)
        {
            ArtCache.ReleaseLater(url);
            return;
        }
        _artApplied = request;
        if (url.Length > 0)
            _artMissLogged = false;
        if (url == ArtUrl)
        {
            ArtCache.ReleaseLater(url);
            return;
        }
        ArtCache.ReleaseLater(ArtUrl);
        ArtUrl = url;
        Changed?.Invoke(["trackArtUrl", "metadata"], false);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs? args)
    {
        try
        {
            var info = sender.GetPlaybackInfo();
            _owner.Post(() => ApplyPlayback(info));
        }
        catch (COMException)
        {
        }
    }

    private void ApplyPlayback(GlobalSystemMediaTransportControlsSessionPlaybackInfo info)
    {
        if (_released)
            return;
        var changed = new List<string>();
        var state = info.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStates.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStates.Paused,
            // "Changing" is a moment between tracks: keep the state it had, so it does not flicker.
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState,
            _ => MediaPlaybackStates.Stopped,
        };
        var rate = info.PlaybackRate ?? 1;
        var seeked = false;
        if (state != PlaybackState || rate != Rate)
        {
            // Count the position up to now with the old state, then from now with the new one.
            _position = PositionNow();
            _positionTime = DateTimeOffset.UtcNow;
            seeked = true;
        }
        Set(state, PlaybackState, v => PlaybackState = v, "playbackState", changed);
        if (changed.Contains("playbackState"))
            changed.Add("isPlaying");
        var controls = info.Controls;
        Set(controls.IsPlayEnabled, CanPlay, v => CanPlay = v, "canPlay", changed);
        Set(controls.IsPauseEnabled, CanPause, v => CanPause = v, "canPause", changed);
        Set(controls.IsStopEnabled, CanStop, v => CanStop = v, "canStop", changed);
        Set(controls.IsNextEnabled, CanGoNext, v => CanGoNext = v, "canGoNext", changed);
        Set(controls.IsPreviousEnabled, CanGoPrevious, v => CanGoPrevious = v, "canGoPrevious", changed);
        Set(controls.IsPlaybackPositionEnabled, CanSeek, v => CanSeek = v, "canSeek", changed);
        Set(controls.IsShuffleEnabled, ShuffleSupported, v => ShuffleSupported = v, "shuffleSupported", changed);
        Set(controls.IsRepeatEnabled, LoopSupported, v => LoopSupported = v, "loopSupported", changed);
        Set(controls.IsPlaybackRateEnabled, RateSupported, v => RateSupported = v, "minRate", changed);
        if (changed.Contains("minRate"))
            changed.Add("maxRate");
        Set(ShuffleSupported && (info.IsShuffleActive ?? false), Shuffle, v => Shuffle = v, "shuffle", changed);
        Set(LoopSupported ? (int)(info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None) : 0, LoopState, v => LoopState = v, "loopState", changed);
        Set(rate, Rate, v => Rate = v, "rate", changed);
        if (changed.Count == 0)
            return;
        changed.Add("canTogglePlaying");
        Changed?.Invoke(changed, false);
        if (seeked)
            Seeked?.Invoke();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs? args)
    {
        try
        {
            var timeline = sender.GetTimelineProperties();
            var received = DateTimeOffset.UtcNow;
            _owner.Post(() => ApplyTimeline(timeline, received));
        }
        catch (COMException)
        {
        }
    }

    private void ApplyTimeline(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline, DateTimeOffset received)
    {
        if (_released)
            return;
        var length = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        var supported = length > 0 || timeline.Position > TimeSpan.Zero;
        var position = (timeline.Position - timeline.StartTime).TotalSeconds;
        // A poll repeats the last report. Applying it again restarts interpolation and looks like a seek.
        if (StartTime == timeline.StartTime && Length == length && PositionSupported == supported
            && Math.Abs(_position - position) < 0.001)
            return;
        var changed = new List<string>();
        StartTime = timeline.StartTime;
        Set(supported, PositionSupported, v => PositionSupported = v, "positionSupported", changed);
        Set(length, Length, v => Length = v, "length", changed);
        if (changed.Contains("length"))
            changed.Add("lengthSupported");
        // The app says when it took the position; trust that unless the time is clearly wrong.
        var updated = timeline.LastUpdatedTime;
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

    // The app behind a session

    private static string SafeAppId(GlobalSystemMediaTransportControlsSession session)
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

    /// <summary>
    /// The name the Start menu shows for an AppUserModelID, or the program name when it has none. It asks the
    /// shell and can scan processes, so call it off the UI thread.
    /// </summary>
    internal static string DescribeApp(string appId)
    {
        var isProgram = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var name = StartMenuName(appId);
        if (string.IsNullOrWhiteSpace(name) && isProgram)
            name = ProgramDescription(appId[..^4]);
        return string.IsNullOrWhiteSpace(name) ? (isProgram ? appId[..^4] : appId) : name;
    }

    /// <summary>The name the Start menu shows for an AppUserModelID.</summary>
    private static string? StartMenuName(string appId)
    {
        if (appId.Length == 0)
            return null;
        var iid = typeof(IShellItem).GUID;
        if (SHCreateItemFromParsingName($"shell:AppsFolder\\{appId}", 0, ref iid, out var item) != 0)
            return null;
        try
        {
            return item.GetDisplayName(0, out var name) == 0 ? name : null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out IShellItem item);

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();

        void GetParent();

        [PreserveSig]
        int GetDisplayName(int sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }

    private static string? ProgramDescription(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                        return description.Trim();
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
        }
        return null;
    }
}

/// <summary>
/// The names of the apps behind media sessions, looked up on the thread pool and kept for the life of the
/// process. Lives on the UI thread.
/// </summary>
internal sealed class AppNames(IEventLoop loop, Log log, Func<string, string> describe, Action named)
{
    private readonly Dictionary<string, string?> _names = [];

    /// <summary>
    /// The app's name, or null while it is looked up. When the lookup ends, <c>named</c> runs on the loop.
    /// </summary>
    public string? Get(string appId)
    {
        if (_names.TryGetValue(appId, out var name))
            return name;
        _names[appId] = null;
        _ = LookUp(appId);
        return null;
    }

    private async Task LookUp(string appId)
    {
        var name = appId;
        try
        {
            name = await Task.Run(() => describe(appId)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed lookup still names the app, by its id.
        catch (Exception e)
#pragma warning restore CA1031
        {
            loop.Post(() => log.Warn("media", $"Cannot name {appId}: {e.Message}"));
        }
        loop.Post(() =>
        {
            _names[appId] = name;
            named();
        });
    }
}

/// <summary>The <c>MediaPlaybackState</c> values.</summary>
internal static class MediaPlaybackStates
{
    public const int Stopped = 0;
    public const int Playing = 1;
    public const int Paused = 2;
}

/// <summary>
/// Track art from Windows comes as image data, not a url. It is written to a file in the temp folder, named by
/// its content, so the same art is one file, and given to QML as a <c>file:///</c> url. Each process has a folder
/// of its own: an instance deletes the art it no longer uses, which must not be art another instance shows.
/// </summary>
internal static class ArtCache
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "winshell", "media-art");
    private static readonly string Folder = Path.Combine(Root, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static readonly Dictionary<string, int> Uses = [];
    private static readonly Lock Gate = new();

    /// <summary>The most art an app can hand over, in bytes.</summary>
    internal const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The most pixels art can decode to.</summary>
    internal const long MaxPixels = 4096 * 4096;

    /// <summary>
    /// Reads art another app hands over and stores it, or returns null when it is not a PNG, JPEG, GIF, BMP or
    /// WebP picture within <see cref="MaxBytes"/> and <see cref="MaxPixels"/>. That leaves out SVG, which the
    /// shell would render at whatever size it asks for.
    /// </summary>
    public static async Task<string?> StoreAsync(Stream input, string? contentType)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > MaxBytes)
                return null;
            bytes.Write(buffer, 0, read);
        }
        var data = bytes.ToArray();
        return data.Length == 0 || IsBitmap(data) ? Store(data, contentType) : null;
    }

    private static bool IsBitmap(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        return codec is not null
            && codec.EncodedFormat is SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Gif or SKEncodedImageFormat.Bmp or SKEncodedImageFormat.Webp
            && (long)codec.Info.Width * codec.Info.Height <= MaxPixels;
    }

    /// <summary>Writes the art and returns its url. Each url returned must be released once.</summary>
    public static string Store(byte[] data, string? contentType)
    {
        if (data.Length == 0)
            return "";
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            _ => ".jpg",
        };
        var path = Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant() + extension);
        lock (Gate)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(path, data);
            }
            var url = new Uri(path).AbsoluteUri;
            Uses[url] = Uses.GetValueOrDefault(url) + 1;
            return url;
        }
    }

    /// <summary>Gives up one use of an art url, and deletes the file when nothing uses it.</summary>
    public static void Release(string url)
    {
        if (url.Length == 0)
            return;
        lock (Gate)
        {
            if (!Uses.TryGetValue(url, out var count))
                return;
            if (count > 1)
            {
                Uses[url] = count - 1;
                return;
            }
            Uses.Remove(url);
            try
            {
                File.Delete(new Uri(url).LocalPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An image loader still has it open; a later start cleans it up.
            }
        }
    }

    /// <summary>
    /// <see cref="Release"/> on the thread pool, for the UI thread: the lock is held while art is written, and
    /// the last release deletes the file.
    /// </summary>
    public static void ReleaseLater(string url)
    {
        if (url.Length > 0)
            _ = Task.Run(() => Release(url));
    }

    /// <summary>Deletes art left behind by earlier runs, and their folders once empty.</summary>
    public static void CleanOld() => CleanOld(Root);

    internal static void CleanOld(string root)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                    File.Delete(file);
            }
            foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                if (!string.Equals(dir, Folder, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
