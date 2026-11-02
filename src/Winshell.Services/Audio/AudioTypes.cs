namespace Winshell.Audio;

/// <summary>What an <see cref="AudioNode"/> is. A node combines <c>Audio</c> with <c>Playback</c> or <c>Recording</c>, and with <c>Session</c> for an app.</summary>
[Flags]
public enum AudioNodeType
{
    Untracked = 0,
    Audio = AudioNodeTypes.Audio,
    Session = AudioNodeTypes.Session,
    Recording = AudioNodeTypes.Recording,
    Playback = AudioNodeTypes.Playback,
    PlaybackDevice = Audio | Playback,
    RecordingDevice = Audio | Recording,
    PlaybackSession = Audio | Session | Playback,
    RecordingSession = Audio | Session | Recording,
}

public enum AudioLinkState
{
    Inactive = 0,
    Active = 1,
}

/// <summary>A speaker position. When Windows gives none, one channel is Mono, two are the front pair, and more count up from AuxRangeStart.</summary>
public enum AudioChannel
{
    Unknown = 0,
    NA = 1,
    Mono = 2,
    FrontLeft = 3,
    FrontRight = 4,
    FrontCenter = 5,
    LowFrequencyEffects = 6,
    SideLeft = 7,
    SideRight = 8,
    FrontLeftCenter = 9,
    FrontRightCenter = 10,
    RearCenter = 11,
    RearLeft = 12,
    RearRight = 13,
    TopCenter = 14,
    TopFrontLeft = 15,
    TopFrontCenter = 16,
    TopFrontRight = 17,
    TopRearLeft = 18,
    TopRearCenter = 19,
    TopRearRight = 20,
    RearLeftCenter = 21,
    RearRightCenter = 22,
    FrontLeftWide = 23,
    FrontRightWide = 24,
    LowFrequencyEffects2 = 25,
    FrontLeftHigh = 26,
    FrontCenterHigh = 27,
    FrontRightHigh = 28,
    TopFrontLeftCenter = 29,
    TopFrontRightCenter = 30,
    TopSideLeft = 31,
    TopSideRight = 32,
    LowFrequencyEffectsLeft = 33,
    LowFrequencyEffectsRight = 34,
    BottomCenter = 35,
    BottomLeftCenter = 36,
    BottomRightCenter = 37,
    AuxRangeStart = 0x1000,
    AuxRangeEnd = 0x1FFF,
    CustomRangeStart = 0x10000,
}
