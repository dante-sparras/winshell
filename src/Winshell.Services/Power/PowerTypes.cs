namespace Winshell.Power;

/// <summary>What a battery is doing.</summary>
public enum PowerDeviceState
{
    /// <summary>Windows does not say, or there is no battery.</summary>
    Unknown = 0,
    /// <summary>The battery is charging.</summary>
    Charging = 1,
    /// <summary>The battery is discharging.</summary>
    Discharging = 2,
    /// <summary>The battery is discharging and has no charge left.</summary>
    Empty = 3,
    /// <summary>The PC is on line power and the battery is full.</summary>
    FullyCharged = 4,
    /// <summary>The PC is on line power and the battery is not charging, though it is not full.</summary>
    NotCharging = 5,
}

/// <summary>The kind of a <see cref="PowerDevice"/>.</summary>
public enum PowerDeviceType
{
    /// <summary>No battery: the PC has none, or none is present.</summary>
    Unknown = 0,
    /// <summary>A battery.</summary>
    Battery = 2,
}

/// <summary>The three positions of the Windows power mode slider.</summary>
public enum PowerModeLevel
{
    /// <summary>Best power efficiency.</summary>
    BestEfficiency = 0,
    /// <summary>Balanced.</summary>
    Balanced = 1,
    /// <summary>Best performance.</summary>
    BestPerformance = 2,
}

/// <summary>The power mode Windows is applying, including energy saver and game mode.</summary>
public enum EffectivePowerMode
{
    /// <summary>Energy saver is on.</summary>
    EnergySaver = 0,
    /// <summary>Favors battery life over performance.</summary>
    BetterBattery = 1,
    /// <summary>Balances performance and power use.</summary>
    Balanced = 2,
    /// <summary>Favors performance over power use.</summary>
    HighPerformance = 3,
    /// <summary>Favors performance the most.</summary>
    MaxPerformance = 4,
    /// <summary>Game mode is on.</summary>
    GameMode = 5,
    /// <summary>Windows Mixed Reality is running.</summary>
    MixedReality = 6,
}
