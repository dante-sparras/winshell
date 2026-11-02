namespace Winshell.Media;

public enum MediaPlaybackState
{
    Stopped = MediaPlaybackStates.Stopped,
    Playing = MediaPlaybackStates.Playing,
    Paused = MediaPlaybackStates.Paused,
}

public enum MediaLoopState
{
    None = 0,
    Track = 1,
    List = 2,
}
