namespace Winshell.Power;

public enum PowerDeviceState
{
    Unknown = 0,
    Charging = 1,
    Discharging = 2,
    Empty = 3,
    FullyCharged = 4,
    NotCharging = 5,
}

public enum PowerDeviceType
{
    Unknown = 0,
    Battery = 2,
}

/// <summary>The three positions of the Windows power mode slider.</summary>
public enum PowerModeLevel
{
    BestEfficiency = 0,
    Balanced = 1,
    BestPerformance = 2,
}

/// <summary>The power mode Windows is applying, including energy saver and game mode.</summary>
public enum EffectivePowerMode
{
    EnergySaver = 0,
    BetterBattery = 1,
    Balanced = 2,
    HighPerformance = 3,
    MaxPerformance = 4,
    GameMode = 5,
    MixedReality = 6,
}
