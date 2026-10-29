using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private int _nextId = 1;

    private SmtcSessions(IEventLoop loop, Log log)
    {
        _loop = loop;
        Log = log;
        ArtCache.CleanOld();
        _ = Start();
    }

    /// <summary>The session list, made on first use. Call on the UI thread.</summary>
    public static SmtcSessions Get(IEventLoop loop, Log log) => _instance ??= new SmtcSessions(loop, log);

    public IReadOnlyList<SmtcSession> Sessions => _sessions;

    public Log Log { get; }

    public event Action<SmtcSession>? SessionAdded;

    public event Action<SmtcSession>? SessionRemoved;

    internal void Post(Action action) => _loop.Post(action);

    private async Task Start()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            Post(() =>
            {
                _manager = manager;
                manager.SessionsChanged += (_, _) => Post(Sync);
                Sync();
            });
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            Log.Warn("media", $"Windows media sessions are not available: {e.Message}");
        }
    }

    private void Sync()
    {
        if (_manager is null)
            return;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> current;
        try
        {
            current = _manager.GetSessions();
        }
        catch (COMException e)
        {
            Log.Warn("media", $"Cannot list media sessions: {e.Message}");
            return;
        }
        // GetSessions returns a new object for a session that is already held, so reference equality
        // drops it and starts again. That cancels the read of its title and art. Match the app id instead.
        var incoming = current.ToList();
        var held = _sessions.ToList();
        var pair = Pair(held.Select(s => s.AppId).ToList(), incoming.Select(SmtcSession.IdOf).ToList());
        var kept = new bool[held.Count];
        for (var i = 0; i < incoming.Count; i++)
        {
            var existing = pair[i];
            if (existing >= 0)
            {
                kept[existing] = true;
                held[existing].Retarget(incoming[i]);
                continue;
            }
            var session = new SmtcSession(this, incoming[i], _nextId++);
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
    private bool _released;
    private DateTimeOffset _positionTime = DateTimeOffset.UtcNow;
    private double _position;

    public SmtcSession(SmtcSessions owner, GlobalSystemMediaTransportControlsSession session, int id)
    {
        _owner = owner;
        Session = session;
        Id = id;
        AppId = IdOf(session);
        AppName = DescribeApp(AppId);
    }

    public GlobalSystemMediaTransportControlsSession Session { get; private set; }

    /// <summary>The app id Windows reports for a session. The same session gets a new object on every list.</summary>
    internal static string IdOf(GlobalSystemMediaTransportControlsSession session) => SafeAppId(session);

    public int Id { get; }

    /// <summary>The app's AppUserModelID, such as <c>Spotify.exe</c> or a Store app's id.</summary>
    public string AppId { get; }

    /// <summary>The name Windows shows for the app.</summary>
    public string AppName { get; }

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
        ArtCache.Release(ArtUrl);
    }

    private void Refresh()
    {
        OnPlaybackInfoChanged(Session, null);
        OnTimelinePropertiesChanged(Session, null);
        OnMediaPropertiesChanged(Session, null);
    }

    private void Hook()
    {
        Session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        Session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        Session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    private void Unhook()
    {
        Session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        Session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        Session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
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
                if (!await operation)
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
                var properties = await sender.TryGetMediaPropertiesAsync();
                if (properties is not null)
                    _owner.Post(() => ApplyMedia(properties, request));
            }
            catch (COMException)
            {
                // The session closed while it was being read.
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
        Set(properties.AlbumArtist ?? "", AlbumArtist, v => AlbumArtist = v, "trackAlbumArtist", changed);
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
                using var stream = await thumbnail.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                using var bytes = new MemoryStream();
                await input.CopyToAsync(bytes);
                var url = ArtCache.Store(bytes.ToArray(), stream.ContentType);
                _owner.Post(() => SetArt(url, request));
            }
            catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
            {
                // A failed read is not "this track has no art". Clearing the url here blanks art that already arrived.
                var message = e.Message;
                _owner.Post(() =>
                {
                    if (request == _artRequest)
                        _artApplied = request;
                    _owner.Log.Warn("media", $"Cannot read art from {AppName}: {message}");
                });
            }
        }
    }

    private void SetArt(string url, int request)
    {
        if (_released || request != _artRequest)
        {
            ArtCache.Release(url);
            return;
        }
        _artApplied = request;
        if (url == ArtUrl)
        {
            ArtCache.Release(url);
            return;
        }
        ArtCache.Release(ArtUrl);
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
        var changed = new List<string>();
        StartTime = timeline.StartTime;
        var length = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        var supported = length > 0 || timeline.Position > TimeSpan.Zero;
        Set(supported, PositionSupported, v => PositionSupported = v, "positionSupported", changed);
        Set(length, Length, v => Length = v, "length", changed);
        if (changed.Contains("length"))
            changed.Add("lengthSupported");
        // The app says when it took the position; trust that unless the time is clearly wrong.
        var updated = timeline.LastUpdatedTime;
        var time = updated.Year > 2000 && updated <= received && received - updated < TimeSpan.FromHours(1) ? updated : received;
        _position = (timeline.Position - timeline.StartTime).TotalSeconds;
        _positionTime = time;
        if (changed.Count > 0)
            Changed?.Invoke(changed, false);
        Seeked?.Invoke();
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

    /// <summary>The name the Start menu shows for an AppUserModelID, or the program name when it has none.</summary>
    private static string DescribeApp(string appId)
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

    /// <summary>Deletes art left behind by earlier runs, and their folders once empty.</summary>
    public static void CleanOld()
    {
        try
        {
            if (!Directory.Exists(Root))
                return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                    File.Delete(file);
            }
            foreach (var dir in Directory.EnumerateDirectories(Root))
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
