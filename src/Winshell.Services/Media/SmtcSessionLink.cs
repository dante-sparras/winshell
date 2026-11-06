using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>
/// Windows' object behind one <see cref="SmtcSession"/>. Its events arrive on other threads; what they report is read
/// there and posted to the UI thread, where the session takes it in.
/// </summary>
internal sealed class SmtcSessionLink(
    IEventLoop loop,
    Log log,
    SmtcSession target,
    GlobalSystemMediaTransportControlsSession session) : ISmtcLink
{
    private int _mediaRequest;
    private int _mediaApplied;
    private bool _released;

    private TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>?
        _mediaChanged;

    private TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>?
        _playbackChanged;

    private TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>?
        _timelineChanged;

    private GlobalSystemMediaTransportControlsSession _session = session;

    public void Start()
    {
        Hook();
        OnPlaybackInfoChanged(_session, null);
        OnTimelinePropertiesChanged(_session, null);
        OnMediaPropertiesChanged(_session, null);
    }

    /// <summary>Follows the new object Windows returned for this same session, without dropping the read in progress.</summary>
    public void Retarget(GlobalSystemMediaTransportControlsSession session)
    {
        if (ReferenceEquals(_session, session))
            return;
        Unhook();
        _session = session;
        Hook();
        OnPlaybackInfoChanged(_session, null);
        OnTimelinePropertiesChanged(_session, null);
        // A metadata or art read is already running. Starting another drops its result.
        if (_mediaApplied == _mediaRequest && !target.ArtLoading)
            OnMediaPropertiesChanged(_session, null);
    }

    public void Release()
    {
        _released = true;
        Unhook();
    }

    /// <summary>Sends a command. Windows' events bring the result; a refusal is logged.</summary>
    public void Send(string what, Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        _ = Await();

        async Task Await()
        {
            try
            {
                if (!await command(_session).AsTask().ConfigureAwait(false))
                    loop.Post(() => log.Warn("media", $"{target.AppName} did not accept {what}"));
            }
#pragma warning disable CA1031 // A closed session fails with COMException or ObjectDisposedException; nobody awaits this.
            catch (Exception e)
#pragma warning restore CA1031
            {
                loop.Post(() => log.Warn("media", $"Cannot {what} on {target.AppName}: {e.Message}"));
            }
        }
    }

    private void Hook()
    {
        // Kept on the link. The WinRT projection does not root an event delegate, so a collected
        // one stops title, art, and timeline updates with no error.
        _mediaChanged ??= OnMediaPropertiesChanged;
        _playbackChanged ??= OnPlaybackInfoChanged;
        _timelineChanged ??= OnTimelinePropertiesChanged;
        _session.MediaPropertiesChanged += _mediaChanged;
        _session.PlaybackInfoChanged += _playbackChanged;
        _session.TimelinePropertiesChanged += _timelineChanged;
    }

    private void Unhook()
    {
        if (_mediaChanged is not null)
            _session.MediaPropertiesChanged -= _mediaChanged;
        if (_playbackChanged is not null)
            _session.PlaybackInfoChanged -= _playbackChanged;
        if (_timelineChanged is not null)
            _session.TimelinePropertiesChanged -= _timelineChanged;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs? args)
    {
        var request = Interlocked.Increment(ref _mediaRequest);
        _ = Read();

        async Task Read()
        {
            try
            {
                var properties = await sender.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
                if (properties is not null)
                    loop.Post(() => ApplyMedia(properties, request));
            }
#pragma warning disable CA1031 // A closed session fails with COMException or ObjectDisposedException; nobody awaits this.
            catch (Exception e)
#pragma warning restore CA1031
            {
                // The session closed while it was being read. Allow a later poll to try again.
                loop.Post(() =>
                {
                    // A session closing mid-read is ordinary; anything else is worth a line.
                    if (e is not COMException)
                        log.Warn("media", $"Cannot read {target.AppName}'s track: {e.Message}");
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
        target.ApplyTrack(properties.Title ?? "", properties.Artist ?? "", properties.AlbumTitle ?? "",
            properties.AlbumArtist ?? "",
            properties.TrackNumber, properties.Genres, properties.Thumbnail);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs? args)
    {
        try
        {
            var info = sender.GetPlaybackInfo();
            loop.Post(() => ApplyPlayback(info));
        }
        catch (COMException)
        {
        }
    }

    private void ApplyPlayback(GlobalSystemMediaTransportControlsSessionPlaybackInfo info)
    {
        if (_released)
            return;
        var controls = info.Controls;
        target.ApplyPlayback(new SmtcPlayback(info.PlaybackStatus)
        {
            Rate = info.PlaybackRate ?? 1,
            CanPlay = controls.IsPlayEnabled,
            CanPause = controls.IsPauseEnabled,
            CanStop = controls.IsStopEnabled,
            CanGoNext = controls.IsNextEnabled,
            CanGoPrevious = controls.IsPreviousEnabled,
            CanSeek = controls.IsPlaybackPositionEnabled,
            ShuffleSupported = controls.IsShuffleEnabled,
            LoopSupported = controls.IsRepeatEnabled,
            RateSupported = controls.IsPlaybackRateEnabled,
            Shuffle = info.IsShuffleActive ?? false,
            LoopState = (int)(info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None)
        });
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs? args)
    {
        try
        {
            var timeline = sender.GetTimelineProperties();
            var received = TimeProvider.System.GetUtcNow();
            loop.Post(() => ApplyTimeline(timeline, received));
        }
        catch (COMException)
        {
        }
    }

    private void ApplyTimeline(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        DateTimeOffset received)
    {
        if (!_released)
            target.ApplyTimeline(timeline.StartTime, timeline.EndTime, timeline.Position, timeline.LastUpdatedTime,
                received);
    }
}
