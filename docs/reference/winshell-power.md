# Winshell.Power

The battery, the power mode, monitor brightness, idle time and keeping the PC awake. Reach them from `Shell.Current.Power`, `Shell.Current.PowerMode` and `Shell.Current.Brightness`. An idle watch is `Shell.NewIdleMonitor()`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## BrightnessConnection

How Winshell reaches a monitor to read and set its brightness.

| Value | Number | Description |
| --- | --- | --- |
| `Internal` | 0 | A built-in panel, such as a laptop screen, set through WMI. |
| `External` | 1 | An external monitor, set over DDC/CI. |

## BrightnessMonitor

One monitor's brightness. The setter shows the new value at once and writes the panel in the background; the next read after the writes end replaces it.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Brightness` | int | The brightness, from Minimum to Maximum. A value set outside that range is clamped to it. A write Windows refuses is logged, and the next read shows the brightness the monitor has. |
| `Connection` | [BrightnessConnection](#brightnessconnection), read-only | Whether the monitor is a built-in panel or an external monitor. |
| `DeviceName` | string, read-only | The name Windows knows the monitor by: a WMI instance name for a built-in panel, a display name like `\\.\DISPLAY1` for an external monitor. |
| `Maximum` | int, read-only | The highest brightness the monitor accepts, often 100. |
| `Minimum` | int, read-only | The lowest brightness the monitor accepts. |
| `Name` | string, read-only | The monitor's model name, or its Windows device name when the monitor reports no model. |

## BrightnessService

The brightness of the connected monitors that report one.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Monitors` | ObservableCollection of [BrightnessMonitor](#brightnessmonitor), read-only | The connected monitors whose brightness Windows can read. Empty until Ready. |
| `Ready` | bool, read-only | Whether the first read of the monitors has finished. |
| `Dispose()` | void | Stops following brightness changes. |

## EffectivePowerMode

The power mode Windows is applying, including energy saver and game mode.

| Value | Number | Description |
| --- | --- | --- |
| `EnergySaver` | 0 | Energy saver is on. |
| `BetterBattery` | 1 | Favors battery life over performance. |
| `Balanced` | 2 | Balances performance and power use. |
| `HighPerformance` | 3 | Favors performance over power use. |
| `MaxPerformance` | 4 | Favors performance the most. |
| `GameMode` | 5 | Game mode is on. |
| `MixedReality` | 6 | Windows Mixed Reality is running. |

## IdleMonitor

Tells when there has been no input for a while.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Enabled` | bool | Whether the monitor watches for idle. While false, IsIdle is false. True by default. |
| `IsIdle` | bool, read-only | Whether there has been no input for Timeout seconds. False while Timeout is 0. |
| `RespectKeepAwake` | bool | When a KeepAwake is on, this monitor does not report idle. |
| `Timeout` | double | The seconds without input after which IsIdle turns on. |
| `Dispose()` | void | Stops checking for idle. |

## KeepAwake

Keeps the screen on and the PC awake while enabled. Dispose it to let go.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `KeepAwake()` | constructor | Creates a keep-awake that is off until Enabled is set. |
| `Enabled` | bool | Whether this keep-awake holds the screen on and the PC awake. False by default. |
| `Dispose()` | void | Turns this keep-awake off, so it no longer holds the PC awake. |

## PowerDevice

A battery, or the aggregate display device.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ChargeRate` | double, read-only | Charge rate, in watts. Negative while discharging. |
| `DeviceId` | string, read-only | The Windows device ID of the battery. Empty for PowerService.DisplayDevice. |
| `FullChargeCapacity` | double, read-only | Full-charge capacity, in watt-hours. |
| `HealthPercentage` | double, read-only | Full charge capacity over design capacity, from 0 to 100. |
| `HealthSupported` | bool, read-only | Whether Windows reports the capacities HealthPercentage needs. |
| `IsPresent` | bool, read-only | Whether a battery is present. False on a PC with no battery. |
| `IsSystemBattery` | bool, read-only | Whether the device is a battery that powers the PC. |
| `Model` | string, read-only | The battery's name, as Windows lists the device. Empty for PowerService.DisplayDevice. |
| `Percentage` | double, read-only | The charge, from 0 to 1. |
| `PowersTheSystem` | bool, read-only | Whether the battery powers the PC, rather than a connected device. |
| `Ready` | bool, read-only | Whether the first read of the batteries has finished. |
| `RemainingCapacity` | double, read-only | Remaining capacity, in watt-hours. |
| `State` | [PowerDeviceState](#powerdevicestate), read-only | Whether the battery is charging, discharging, full or empty. |
| `TimeToEmpty` | double, read-only | The seconds left until the battery is empty. 0 when it is not discharging or Windows does not know. |
| `TimeToFull` | double, read-only | The seconds left until the battery is full. 0 when it is not charging or Windows does not know. |
| `Type` | [PowerDeviceType](#powerdevicetype), read-only | Whether the device is a battery. PowerDeviceType.Unknown on a PC with no battery. |

## PowerDeviceState

What a battery is doing.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows does not say, or there is no battery. |
| `Charging` | 1 | The battery is charging. |
| `Discharging` | 2 | The battery is discharging. |
| `Empty` | 3 | The battery is discharging and has no charge left. |
| `FullyCharged` | 4 | The PC is on line power and the battery is full. |
| `NotCharging` | 5 | The PC is on line power and the battery is not charging, though it is not full. |

## PowerDeviceType

The kind of a PowerDevice.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | No battery: the PC has none, or none is present. |
| `Battery` | 2 | A battery. |

## PowerModeLevel

The three positions of the Windows power mode slider.

| Value | Number | Description |
| --- | --- | --- |
| `BestEfficiency` | 0 | Best power efficiency. |
| `Balanced` | 1 | Balanced. |
| `BestPerformance` | 2 | Best performance. |

## PowerModeService

The Windows power mode. Setting Mode changes the slider for the supply in use.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Available` | bool, read-only | The public power-mode API answered. When this is false, Mode stays Balanced. |
| `Effective` | [EffectivePowerMode](#effectivepowermode), read-only | The power mode Windows is applying now, which can differ from Mode while energy saver or game mode is on. |
| `Mode` | [PowerModeLevel](#powermodelevel) | The position of the power mode slider for the supply in use, battery or plugged in. Setting it moves that slider; a value Windows refuses is logged. |
| `Ready` | bool, read-only | Whether the first read of the power mode has finished. |
| `Dispose()` | void | Stops following power mode changes. |

## PowerService

The batteries of this PC, as Windows reports them.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Devices` | ObservableCollection of [PowerDevice](#powerdevice), read-only | Each battery Windows lists, as it comes and goes. Empty on a PC with no battery. |
| `DisplayDevice` | [PowerDevice](#powerdevice), read-only | All the batteries as one, as Windows reports its aggregate battery. |
| `OnBattery` | bool, read-only | Whether the PC has a battery and runs on it, with no power supply plugged in. |
| `Dispose()` | void | Stops following battery changes. |
