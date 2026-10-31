# Winshell.Power

`import Winshell.Power`. The battery, the Windows power mode, monitor brightness, and idle time. A shell uses it for a battery icon, the power mode and brightness controls in quick settings, keeping the PC awake, and noticing that the user has stepped away. Setting the power mode or a brightness changes it in Windows.

[Migrating from Quickshell](../migrating-from-quickshell.md#power) maps the UPower, PowerProfiles and idle names onto these.

## Power

The `Power` singleton: the batteries Windows reports. Reach it as `Power`, for example `Power.displayDevice.percentage`.

| Member | Type | Description |
| --- | --- | --- |
| `displayDevice` | [PowerDevice](#powerdevice), read-only | The whole PC's battery, as the taskbar shows it: every battery combined. On a PC without a battery, its `isPresent` is false. It is always the same object. |
| `devices` | ObjectModel of [PowerDevice](#powerdevice), read-only | Each battery Windows lists that is present, kept current as batteries come and go. |
| `onBattery` | bool, read-only | True when the PC has a battery and runs on it, not on a charger. |

## PowerDevice

A battery, or the combined display device. Reach them through `Power.displayDevice` and `Power.devices`; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `type` | [PowerDeviceType](#powerdevicetype), read-only | `Battery` for a battery, `Unknown` for the display device of a PC without one. |
| `powersTheSystem` | bool, read-only | True for a battery, which powers the PC. |
| `remainingCapacity` | real, read-only | Remaining capacity, in watt-hours. 0 when Windows does not report it. |
| `fullChargeCapacity` | real, read-only | Full-charge capacity, in watt-hours. 0 when Windows does not report it. |
| `chargeRate` | real, read-only | Charge rate, in watts. Negative while discharging. |
| `timeToEmpty` | real, read-only | Seconds until the battery is empty, while discharging. 0 otherwise, or when Windows cannot tell. |
| `timeToFull` | real, read-only | Seconds until the battery is full, while charging. 0 otherwise, or when Windows cannot tell. |
| `percentage` | real, read-only | The charge, from 0 to 1. |
| `isPresent` | bool, read-only | Whether there is a battery. False for the display device of a PC without one. |
| `state` | [PowerDeviceState](#powerdevicestate), read-only | Whether the battery is charging, discharging, full, empty, or held below full. |
| `healthPercentage` | real, read-only | Full charge capacity over design capacity, from 0 to 100. 0 when Windows does not report both. |
| `healthSupported` | bool, read-only | Whether `healthPercentage` is known. |
| `iconName` | string, read-only | An icon name for the charge and state: `battery-caution`, `battery-low`, `battery-good` or `battery-full`, with `-charging` added while charging, or `battery-empty` or `battery-missing`. A full battery is `battery-full`. |
| `isSystemBattery` | bool, read-only | True for a battery that powers the PC. |
| `deviceId` | string, read-only | The Windows device id of the battery. Empty for the display device. |
| `model` | string, read-only | The name Windows gives the battery. Empty for the display device. |
| `ready` | bool, read-only | Whether Windows has been read. The display device turns true after the first read; a battery in `devices` is always ready. |

`PowerDeviceState.toString(state)` gives a label for a state, such as `Fully Charged`.

## PowerMode

The `PowerMode` singleton: the Windows power mode slider in Settings > System > Power & battery, and the mode Windows is applying. Reach it as `PowerMode`, for example `PowerMode.mode`.

| Member | Type | Description |
| --- | --- | --- |
| `mode` | [PowerModeLevel](#powermodelevel) | The slider's position for the supply in use: the battery setting while on battery, the plugged-in setting otherwise. Setting it moves that slider in Windows. It changes the power mode, not the power plan. |
| `effective` | [EffectivePowerMode](#effectivepowermode), read-only | The mode Windows is applying, which includes energy saver and game mode. |
| `available` | bool, read-only | Whether Windows answered when the slider was read. When this is false, `mode` stays `Balanced`. |
| `ready` | bool, read-only | True after the first read. |

Setting `mode` to a value that is not a [PowerModeLevel](#powermodelevel), or one Windows refuses, writes a warning to the log.

## Brightness

The `Brightness` singleton: each monitor whose brightness Winshell can read. Reach it as `Brightness`, for example `Brightness.monitors`.

| Member | Type | Description |
| --- | --- | --- |
| `monitors` | ObjectModel of [BrightnessMonitor](#brightnessmonitor), read-only | Each monitor that reports a brightness: a laptop's own panel through Windows, and an external monitor that answers over DDC/CI. |
| `ready` | bool, read-only | True after the first read. On this checkout a change of `ready` is not reported, so a binding on it does not update; bind to `monitors` instead. |

Monitors are read when `Brightness` is first used, when a laptop panel's brightness changes, and after the shell sets a brightness. External monitors are not polled, so a change made with the monitor's own buttons shows at the next read.

## BrightnessMonitor

One monitor's brightness. Monitors are in `Brightness.monitors`; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `deviceName` | string, read-only | The monitor's id: the display name, such as `\\.\DISPLAY1`, for an external monitor, or the Windows instance name of a laptop panel. |
| `name` | string, read-only | The monitor's model name, or its id when Windows gives none. |
| `connection` | [BrightnessConnection](#brightnessconnection), read-only | `Internal` for a laptop panel, `External` for a monitor on a cable. |
| `minimum` | int, read-only | The lowest brightness the monitor takes. |
| `maximum` | int, read-only | The highest brightness the monitor takes. |
| `brightness` | int | The brightness, from `minimum` to `maximum`. Setting it changes the monitor's brightness, clamped to that range. |

Setting `brightness` returns at once and reads back the new value. The monitor is written in the background, one write at a time; a value set while a write runs replaces the one waiting, so a slider drag skips the values in between. The next read after the writes end replaces the value that was set. When the monitor refuses a new brightness, a warning goes to the log and that read shows the brightness the monitor kept.

## IdleMonitor

Tells when there has been no keyboard or mouse input for a while. Create one with `IdleMonitor { timeout: 300 }`.

| Member | Type | Description |
| --- | --- | --- |
| `enabled` | bool | Whether the monitor watches. Defaults to true. While it is false, `isIdle` is false. |
| `timeout` | real | How long without input counts as idle, in seconds. Defaults to 0, which never reports idle. |
| `respectKeepAwake` | bool | When true, the default, this monitor does not report idle while a [KeepAwake](#keepawake) in the shell is enabled. |
| `isIdle` | bool, read-only | True once there has been no input for `timeout` seconds, until the next input. |

Input anywhere in the Windows session counts, not only input to the shell.

## KeepAwake

Keeps the screen on and the PC awake while enabled, like a video player does. Create one with `KeepAwake { enabled: true }`.

| Member | Type | Description |
| --- | --- | --- |
| `enabled` | bool | Whether to keep the PC awake. Defaults to false. While any `KeepAwake` in the shell is enabled, Windows does not turn the screen off or sleep. |

## PowerDeviceState

The values of `PowerDevice.state`.

| Value | Meaning |
| --- | --- |
| `Unknown` | Windows does not say, or there is no battery. |
| `Charging` | The battery is charging. |
| `Discharging` | The PC runs on the battery. |
| `Empty` | The battery is discharging at 0%. |
| `FullyCharged` | Plugged in and full. |
| `NotCharging` | Plugged in, but held below full. |

## PowerDeviceType

The values of `PowerDevice.type`.

| Value | Meaning |
| --- | --- |
| `Unknown` | Not a battery: the display device of a PC without one. |
| `Battery` | A battery. |

## PowerModeLevel

The three positions of the Windows power mode slider, the values of `PowerMode.mode`.

| Value | Meaning |
| --- | --- |
| `BestEfficiency` | Best power efficiency. |
| `Balanced` | Balanced. |
| `BestPerformance` | Best performance. |

## EffectivePowerMode

The power mode Windows is applying, including energy saver and game mode: the values of `PowerMode.effective`.

| Value | Meaning |
| --- | --- |
| `EnergySaver` | Energy saver is on. |
| `BetterBattery` | A mode that favors battery life. |
| `Balanced` | Balanced. |
| `HighPerformance` | A mode that favors performance. |
| `MaxPerformance` | The highest performance. |
| `GameMode` | Game mode. |
| `MixedReality` | The mode Windows uses for mixed reality. |

## BrightnessConnection

The values of `BrightnessMonitor.connection`.

| Value | Meaning |
| --- | --- |
| `Internal` | A laptop's own panel, set through Windows. |
| `External` | A monitor on a cable, set over DDC/CI. |

## Examples

The battery charge, as the taskbar shows it.

```qml
import QtQuick
import Winshell.Power

Text {
    readonly property var battery: Power.displayDevice
    visible: battery.isPresent
    text: Math.round(battery.percentage * 100) + "%"
        + (battery.state === PowerDeviceState.Charging ? ", charging" : "")
}
```

A brightness slider for each monitor. Moving one changes that monitor's brightness.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Power

Column {
    Repeater {
        model: Brightness.monitors
        Slider {
            required property var modelData
            from: modelData.minimum
            to: modelData.maximum
            stepSize: 1
            value: modelData.brightness
            onMoved: modelData.brightness = value
        }
    }
}
```

A switch that keeps the PC awake, and a label that tells when the user has been away for five minutes.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Power

Column {
    KeepAwake { id: awake }
    IdleMonitor { id: idle; timeout: 300; respectKeepAwake: false }

    Switch {
        text: "Keep awake"
        checked: awake.enabled
        onToggled: awake.enabled = checked
    }
    Label { text: idle.isIdle ? "Away" : "Here" }
}
```
