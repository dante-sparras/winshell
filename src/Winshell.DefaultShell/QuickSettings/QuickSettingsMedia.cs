using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Media;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The media card: the playing session, or the first one. The card hides when there is none.</summary>
public sealed class QuickSettingsMedia : ObservableObject
{
    private readonly MediaService _media;
    private readonly List<MediaSession> _sessions = [];
    private MediaSession? _session;

    internal QuickSettingsMedia(MediaService media)
    {
        _media = media;
        media.Sessions.CollectionChanged += OnSessions;
        Watch();
    }

    /// <summary>Whether the card has a session to show.</summary>
    public bool HasSession { get; private set => Set(ref field, value); }

    public string App { get; private set => Set(ref field, value); } = "";

    public string Title { get; private set => Set(ref field, value); } = "";

    public string Artist { get; private set => Set(ref field, value); } = "";

    /// <summary>The track art url, or empty when the session has none.</summary>
    public string Art { get; private set => Set(ref field, value); } = "";

    public bool HasArt { get; private set => Set(ref field, value); }

    public bool CanPrevious { get; private set => Set(ref field, value); }

    public bool CanNext { get; private set => Set(ref field, value); }

    public bool CanToggle { get; private set => Set(ref field, value); }

    public bool Playing { get; private set => Set(ref field, value); }

    public string PlayGlyph { get; private set => Set(ref field, value); } = "";

    public string PlayLabel { get; private set => Set(ref field, value); } = "Play";

    public void Previous() => _session?.Previous();

    public void Next() => _session?.Next();

    public void Toggle() => _session?.TogglePlaying();

    private void OnSessions(object? sender, NotifyCollectionChangedEventArgs e) => Watch();

    private void OnSession(object? sender, PropertyChangedEventArgs e) => Publish();

    private void Watch()
    {
        foreach (var session in _sessions)
            session.PropertyChanged -= OnSession;
        _sessions.Clear();
        _sessions.AddRange(_media.Sessions);
        foreach (var session in _sessions)
            session.PropertyChanged += OnSession;
        Publish();
    }

    private void Publish()
    {
        _session = _media.Sessions.FirstOrDefault(session => session.IsPlaying) ?? _media.Sessions.FirstOrDefault();
        HasSession = _session is not null;
        App = _session?.AppName ?? "";
        Title = _session?.TrackTitle ?? "";
        Artist = _session?.TrackArtist ?? "";
        Art = _session?.TrackArtUrl ?? "";
        HasArt = Art.Length > 0;
        CanPrevious = _session?.CanGoPrevious == true;
        CanNext = _session?.CanGoNext == true;
        CanToggle = _session?.CanTogglePlaying == true;
        Playing = _session?.IsPlaying == true;
        PlayGlyph = Playing ? "" : "";
        PlayLabel = Playing ? "Pause" : "Play";
    }
}
