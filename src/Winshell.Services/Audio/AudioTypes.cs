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
    /// <summary>A channel whose position Windows does not give.</summary>
    Unknown,
    /// <summary>The only channel of a mono stream.</summary>
    Mono,
    /// <summary>Front left.</summary>
    FrontLeft,
    /// <summary>Front right.</summary>
    FrontRight,
    /// <summary>Front center.</summary>
    FrontCenter,
    /// <summary>The subwoofer, for low-frequency effects.</summary>
    LowFrequency,
    /// <summary>Back left.</summary>
    BackLeft,
    /// <summary>Back right.</summary>
    BackRight,
    /// <summary>Front, between left and center.</summary>
    FrontLeftOfCenter,
    /// <summary>Front, between right and center.</summary>
    FrontRightOfCenter,
    /// <summary>Back center.</summary>
    BackCenter,
    /// <summary>Side left.</summary>
    SideLeft,
    /// <summary>Side right.</summary>
    SideRight,
    /// <summary>Above the listener.</summary>
    TopCenter,
    /// <summary>Top front left.</summary>
    TopFrontLeft,
    /// <summary>Top front center.</summary>
    TopFrontCenter,
    /// <summary>Top front right.</summary>
    TopFrontRight,
    /// <summary>Top back left.</summary>
    TopBackLeft,
    /// <summary>Top back center.</summary>
    TopBackCenter,
    /// <summary>Top back right.</summary>
    TopBackRight,
}
