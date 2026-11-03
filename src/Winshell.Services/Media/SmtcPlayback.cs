using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>What Windows reports about a session's playback.</summary>
internal sealed record SmtcPlayback(GlobalSystemMediaTransportControlsSessionPlaybackStatus Status)
{
    public double Rate { get; init; } = 1;

    public bool CanPlay { get; init; }

    public bool CanPause { get; init; }

    public bool CanStop { get; init; }

    public bool CanGoNext { get; init; }

    public bool CanGoPrevious { get; init; }

    public bool CanSeek { get; init; }

    public bool ShuffleSupported { get; init; }

    public bool LoopSupported { get; init; }

    public bool RateSupported { get; init; }

    public bool Shuffle { get; init; }

    /// <summary>The <c>MediaLoopState</c>.</summary>
    public int LoopState { get; init; }
}
