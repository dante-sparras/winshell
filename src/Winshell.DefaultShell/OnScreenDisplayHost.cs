using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;
using Winshell.Audio;
using Winshell.Media;
using Winshell.Power;

namespace Winshell.DefaultShell;

/// <summary>
/// Binds the on-screen display to the audio, brightness and media services. It only reads levels and mute.
/// Transport buttons are the one write, and they go to the media session that is playing, or else one that is paused.
/// </summary>
internal sealed class OnScreenDisplayHost
{
    private readonly AudioService _audio;
    private readonly MediaService _media;
    private readonly IReadOnlyList<ScreenInfo> _screens;
    private readonly HashSet<BrightnessMonitor> _baselined = [];
    private AudioNode? _playback;
    private AudioNode? _recording;
    private MediaSession? _session;

    internal OnScreenDisplayHost(Shell shell, Preferences preferences, TimeProvider time)
        : this(new OnScreenDisplayModel(time), shell.Audio, shell.Brightness, shell.Media, shell.Screens)
    {
        var window = new PanelWindow
        {
            Width = OnScreenDisplayModel.PanelWidth,
            Height = Model.PanelHeight,
            Anchors = Edges.Bottom,
            Margins = new Thickness(0, 0, 0, OnScreenDisplayModel.BottomGap),
            AboveWindows = true,
            Focusable = false,
            Corners = WindowCorners.Round,
            Backdrop = preferences.Material,
            Content = new OnScreenDisplayView(Model),
        };
        var ui = new UISettings();
        Model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OnScreenDisplayModel.Open))
            {
                Model.AnimationsEnabled = ui.AnimationsEnabled;
                window.Visible = Model.Open;
            }
            else if (e.PropertyName == nameof(OnScreenDisplayModel.PanelHeight))
                window.Height = Model.PanelHeight;
            else if (e.PropertyName == nameof(OnScreenDisplayModel.Screen))
                window.Screen = Model.Screen;
        };
        window.Screen = Model.Screen;
        window.Height = Model.PanelHeight;
        preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Preferences.Backdrop))
                window.Backdrop = preferences.Material;
        };
    }

    internal OnScreenDisplayHost(OnScreenDisplayModel model, AudioService audio, BrightnessService brightness, MediaService media, ObservableCollection<ScreenInfo> screens)
    {
        Model = model;
        _audio = audio;
        _media = media;
        _screens = screens;
        screens.CollectionChanged += (_, _) => Model.SetScreens(screens);
        Model.SetScreens(screens);
        audio.PropertyChanged += OnAudio;
        FollowPlayback();
        FollowRecording();
        brightness.Monitors.CollectionChanged += OnMonitors;
        foreach (var monitor in brightness.Monitors)
            Follow(monitor);
        media.Sessions.CollectionChanged += OnSessions;
        foreach (var session in media.Sessions)
            session.PropertyChanged += OnSession;
        ApplySession();
        Model.TogglePlaybackRequested += () =>
        {
            if (_session is { CanTogglePlaying: true })
                _session.TogglePlaying();
        };
        Model.NextRequested += () =>
        {
            if (_session is { CanGoNext: true })
                _session.Next();
        };
        Model.PreviousRequested += () =>
        {
            if (_session is { CanGoPrevious: true })
                _session.Previous();
        };
    }

    public OnScreenDisplayModel Model { get; }

    private void OnAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioService.DefaultPlaybackDevice))
            FollowPlayback();
        else if (e.PropertyName == nameof(AudioService.DefaultRecordingDevice))
            FollowRecording();
    }

    private void FollowPlayback()
    {
        var device = _audio.DefaultPlaybackDevice;
        if (!ReferenceEquals(device, _playback))
        {
            if (_playback is not null)
                _playback.PropertyChanged -= OnPlayback;
            _playback = device;
            if (_playback is not null)
                _playback.PropertyChanged += OnPlayback;
        }
        if (_playback is { } playback)
        {
            Model.Volume = playback.Volume;
            Model.PlaybackMuted = playback.Muted;
            Model.VolumeKnown = true;
        }
        else
        {
            Model.Volume = 0;
            Model.PlaybackMuted = false;
            Model.VolumeKnown = false;
        }
    }

    private void OnPlayback(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AudioNode audio)
            return;
        if (e.PropertyName == nameof(AudioNode.Volume))
            Model.Volume = audio.Volume;
        else if (e.PropertyName == nameof(AudioNode.Muted))
            Model.PlaybackMuted = audio.Muted;
    }

    private void FollowRecording()
    {
        var device = _audio.DefaultRecordingDevice;
        if (!ReferenceEquals(device, _recording))
        {
            if (_recording is not null)
                _recording.PropertyChanged -= OnRecording;
            _recording = device;
            if (_recording is not null)
                _recording.PropertyChanged += OnRecording;
        }
        if (_recording is { } recording)
        {
            Model.MicrophoneMuted = recording.Muted;
            Model.MicrophoneKnown = true;
        }
        else
        {
            Model.MicrophoneMuted = false;
            Model.MicrophoneKnown = false;
        }
    }

    private void OnRecording(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Muted) && sender is AudioNode audio)
            Model.MicrophoneMuted = audio.Muted;
    }

    private void OnMonitors(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BrightnessMonitor monitor in e.OldItems)
            {
                monitor.PropertyChanged -= OnMonitor;
                _baselined.Remove(monitor);
            }
        }
        if (e.NewItems is not null)
        {
            foreach (BrightnessMonitor monitor in e.NewItems)
                Follow(monitor);
        }
    }

    // A monitor's first reading is its baseline. The binding fills the level in before this runs, and that is not a change.
    private void Follow(BrightnessMonitor monitor)
    {
        monitor.PropertyChanged += OnMonitor;
        Model.PublishBrightness(ScreenFor(monitor), monitor.Minimum, monitor.Maximum, monitor.Brightness, false);
        _baselined.Add(monitor);
    }

    private void OnMonitor(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrightnessMonitor.Brightness) || sender is not BrightnessMonitor monitor || !_baselined.Contains(monitor))
            return;
        Model.PublishBrightness(ScreenFor(monitor), monitor.Minimum, monitor.Maximum, monitor.Brightness, true);
    }

    private ScreenInfo? ScreenFor(BrightnessMonitor monitor) =>
        _screens.FirstOrDefault(screen =>
            monitor.DeviceName == screen.Name
            || monitor.DeviceName == "\\\\.\\" + screen.Name
            || (monitor.Name.Length > 0 && monitor.Name == screen.Model));

    private void OnSessions(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MediaSession session in e.OldItems)
                session.PropertyChanged -= OnSession;
        }
        if (e.NewItems is not null)
        {
            foreach (MediaSession session in e.NewItems)
                session.PropertyChanged += OnSession;
        }
        ApplySession();
    }

    private void OnSession(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaSession.PlaybackState) or nameof(MediaSession.IsPlaying)
            or nameof(MediaSession.TrackTitle) or nameof(MediaSession.TrackArtist) or nameof(MediaSession.TrackArtUrl)
            or nameof(MediaSession.CanPlay) or nameof(MediaSession.CanPause) or nameof(MediaSession.CanGoNext)
            or nameof(MediaSession.CanGoPrevious) or nameof(MediaSession.CanTogglePlaying))
            ApplySession();
    }

    private void ApplySession()
    {
        var session = _media.Sessions.FirstOrDefault(item => item.PlaybackState == MediaPlaybackState.Playing)
            ?? _media.Sessions.FirstOrDefault(item => item.PlaybackState == MediaPlaybackState.Paused);
        _session = session;
        if (session is null)
        {
            // Known goes false before the title is cleared, so an empty title does not show.
            Model.MediaKnown = false;
            Model.MediaTitle = "";
            Model.MediaArtist = "";
            Model.MediaArtUrl = "";
            Model.MediaPlaying = false;
            Model.CanTogglePlayback = false;
            Model.CanGoNext = false;
            Model.CanGoPrevious = false;
            return;
        }
        Model.MediaTitle = session.TrackTitle;
        Model.MediaArtist = session.TrackArtist;
        Model.MediaArtUrl = session.TrackArtUrl;
        Model.MediaPlaying = session.IsPlaying;
        Model.CanTogglePlayback = session.CanTogglePlaying;
        Model.CanGoNext = session.CanGoNext;
        Model.CanGoPrevious = session.CanGoPrevious;
        Model.MediaKnown = true;
    }
}
