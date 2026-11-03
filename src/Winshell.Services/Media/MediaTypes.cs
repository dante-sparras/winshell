namespace Winshell.Media;

/// <summary>Whether a <see cref="MediaSession"/> is playing.</summary>
public enum MediaPlaybackState
{
    /// <summary>Nothing is playing or paused, or Windows reports the session closed or only opened.</summary>
    Stopped = 0,
    /// <summary>The app is playing.</summary>
    Playing = 1,
    /// <summary>The app is paused.</summary>
    Paused = 2,
}

/// <summary>What a <see cref="MediaSession"/> repeats.</summary>
public enum MediaLoopState
{
    /// <summary>Nothing repeats.</summary>
    None = 0,
    /// <summary>The track repeats.</summary>
    Track = 1,
    /// <summary>The whole list repeats.</summary>
    List = 2,
}
