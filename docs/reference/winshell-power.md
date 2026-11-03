# Winshell.Power

The battery, the power mode, monitor brightness, idle time and keeping the PC awake. Reach them from `Shell.Current.Power`, `Shell.Current.PowerMode` and `Shell.Current.Brightness`. An idle watch is `Shell.NewIdleMonitor()`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## BrightnessConnection

| Value | Number | Description |
| --- | --- | --- |
| `Internal` | 0 |  |
| `External` | 1 |  |

## BrightnessMonitor

One monitor's brightness. The setter shows the new value at once and writes the panel in the background; the next read after the writes end replaces it.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Brightness` | int |  |
| `Connection` | [BrightnessConnection](#brightnessconnection), read-only |  |
| `DeviceName` | string, read-only |  |
| `Maximum` | int, read-only |  |
| `Minimum` | int, read-only |  |
| `Name` | string, read-only |  |

## BrightnessService

The brightness of the connected monitors that report one.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Monitors` | ObservableCollection of [BrightnessMonitor](#brightnessmonitor), read-only |  |
| `Ready` | bool, read-only |  |
| `Dispose()` | void |  |

## EffectivePowerMode

The power mode Windows is applying, including energy saver and game mode.

| Value | Number | Description |
| --- | --- | --- |
| `EnergySaver` | 0 |  |
| `BetterBattery` | 1 |  |
| `Balanced` | 2 |  |
| `HighPerformance` | 3 |  |
| `MaxPerformance` | 4 |  |
| `GameMode` | 5 |  |
| `MixedReality` | 6 |  |

## IdleMonitor

Tells when there has been no input for a while.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Enabled` | bool |  |
| `IsIdle` | bool, read-only |  |
| `RespectKeepAwake` | bool | When a KeepAwake is on, this monitor does not report idle. |
| `Timeout` | double | The seconds without input after which IsIdle turns on. |
| `Dispose()` | void |  |

## KeepAwake

Keeps the screen on and the PC awake while enabled. Dispose it to let go.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `KeepAwake()` | constructor |  |
| `Enabled` | bool |  |
| `Dispose()` | void |  |

## PowerDevice

A battery, or the aggregate display device.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ChargeRate` | double, read-only | Charge rate, in watts. Negative while discharging. |
| `DeviceId` | string, read-only |  |
| `FullChargeCapacity` | double, read-only | Full-charge capacity, in watt-hours. |
| `HealthPercentage` | double, read-only | Full charge capacity over design capacity, from 0 to 100. |
| `HealthSupported` | bool, read-only |  |
| `IsPresent` | bool, read-only |  |
| `IsSystemBattery` | bool, read-only |  |
| `Model` | string, read-only |  |
| `Percentage` | double, read-only | The charge, from 0 to 1. |
| `PowersTheSystem` | bool, read-only |  |
| `Ready` | bool, read-only |  |
| `RemainingCapacity` | double, read-only | Remaining capacity, in watt-hours. |
| `State` | [PowerDeviceState](#powerdevicestate), read-only |  |
| `TimeToEmpty` | double, read-only |  |
| `TimeToFull` | double, read-only |  |
| `Type` | [PowerDeviceType](#powerdevicetype), read-only |  |

## PowerDeviceState

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Charging` | 1 |  |
| `Discharging` | 2 |  |
| `Empty` | 3 |  |
| `FullyCharged` | 4 |  |
| `NotCharging` | 5 |  |

## PowerDeviceType

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Battery` | 2 |  |

## PowerModeLevel

The three positions of the Windows power mode slider.

| Value | Number | Description |
| --- | --- | --- |
| `BestEfficiency` | 0 |  |
| `Balanced` | 1 |  |
| `BestPerformance` | 2 |  |

## PowerModeService

The Windows power mode. Setting Mode changes the slider for the supply in use.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Available` | bool, read-only | The public power-mode API answered. When this is false, Mode stays Balanced. |
| `Effective` | [EffectivePowerMode](#effectivepowermode), read-only |  |
| `Mode` | [PowerModeLevel](#powermodelevel) |  |
| `Ready` | bool, read-only |  |
| `Dispose()` | void |  |

## PowerService

The batteries of this PC, as Windows reports them.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Devices` | ObservableCollection of [PowerDevice](#powerdevice), read-only |  |
| `DisplayDevice` | [PowerDevice](#powerdevice), read-only |  |
| `OnBattery` | bool, read-only |  |
| `Dispose()` | void |  |

