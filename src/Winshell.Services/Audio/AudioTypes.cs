namespace Winshell.Audio;

/// <summary>What an <see cref="AudioNode"/> is.</summary>
internal enum AudioNodeType
{
    PlaybackDevice,
    RecordingDevice,
    PlaybackSession,
    RecordingSession,
}

/// <summary>A speaker position, in the order of the Windows speaker mask. Without a mask, one channel is Mono, two are the front pair, and more are Unknown.</summary>
public enum AudioChannel
{
    Unknown,
    Mono,
    FrontLeft,
    FrontRight,
    FrontCenter,
    LowFrequency,
    BackLeft,
    BackRight,
    FrontLeftOfCenter,
    FrontRightOfCenter,
    BackCenter,
    SideLeft,
    SideRight,
    TopCenter,
    TopFrontLeft,
    TopFrontCenter,
    TopFrontRight,
    TopBackLeft,
    TopBackCenter,
    TopBackRight,
}
