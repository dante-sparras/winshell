# Winshell.Audio

`import Winshell.Audio`. The Windows playback and recording devices, and each app's audio session on them, with their volume and mute. A shell uses it for a volume slider, a volume mixer, an output picker, or a microphone level. Writing a volume, a mute or a default device changes it in Windows, the same as the Windows volume flyout does.

`Audio` is the singleton. Every device and every audio session is an [AudioNode](#audionode) in `Audio.nodes`, and each node's volume is its [AudioVolume](#audiovolume) in `audio`. `Audio.defaultPlaybackDevice.audio.volume` is the volume the Windows flyout shows.

[Migrating from Quickshell](../migrating-from-quickshell.md#audio) maps the Pipewire names onto these.

## Audio

The `Audio` singleton: every playback device, recording device, and audio session Windows has. Reach it as `Audio`, for example `Audio.defaultPlaybackDevice`.

| Member | Type | Description |
| --- | --- | --- |
| `nodes` | ObjectModel of [AudioNode](#audionode), read-only | Every node: the active playback and recording devices, and the audio sessions on them. A device or session is added and removed as Windows adds and removes it, and a session leaves when its app exits. |
| `links` | ObjectModel of [AudioLink](#audiolink), read-only | Every link: one per audio session, from a playback session to its device, or from a recording device to its session. |
| `linkGroups` | ObjectModel of [AudioLinkGroup](#audiolinkgroup), read-only | Every link group. Windows has one connection per session, so there is one group for each entry of `links`. |
| `defaultPlaybackDevice` | [AudioNode](#audionode) | The default playback device, or null when there is none. Setting it makes that device the Windows default. |
| `defaultRecordingDevice` | [AudioNode](#audionode) | The default recording device, or null when there is none. Setting it makes that device the Windows default. |
| `ready` | bool, read-only | Always true. The devices are read the first time `Audio` is used, before any binding sees it. When Windows audio cannot be opened, `ready` is still true and `nodes` is empty. |

Setting either default to a node that is not a device of that kind changes nothing and writes a warning to the log. Windows has one System Sounds session on every playback device; only the one on the default playback device is in `nodes`, named `system-sounds`.

## AudioNode

A playback device, a recording device, or an app's audio session on one of them. You get nodes from `Audio.nodes`, `Audio.defaultPlaybackDevice`, `Audio.defaultRecordingDevice`, and from links; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `id` | int, read-only | A number that tells nodes apart while the shell runs. It is not stored by Windows and changes when the shell restarts. |
| `name` | string, read-only | For a device, the Windows endpoint id. For a session, the program's file name without `.exe`, or `system-sounds` for System Sounds. |
| `description` | string, read-only | The name to show. For a device, its Windows name, such as `Speakers (Realtek(R) Audio)`. For a session, the app's name, or `System Sounds`. |
| `interfaceName` | string, read-only | For a device, the description Windows gives the endpoint, such as `Speakers` or `Headphones`. Empty for a session. The adapter is `properties["adapter"]`. |
| `isPlayback` | bool, read-only | True for a playback device and for a session that plays to one. |
| `isSession` | bool, read-only | True for an app's audio session, false for a device. |
| `type` | [AudioNodeType](#audionodetype) flags, read-only | What the node is, such as `AudioNodeType.PlaybackDevice` or `AudioNodeType.RecordingSession`. |
| `properties` | var, read-only | More about the node, as strings. A device has `endpointId`, `friendlyName`, `interfaceName`, `flow` (`playback` or `recording`), `adapter`, `icon`, and `formFactor`. A session has `sessionId`, `friendlyName`, `flow`, `appName`, `icon` (the program's path), `processId`, and `processName`. |
| `audio` | [AudioVolume](#audiovolume), read-only | The node's volume and mute. Every node has one. |
| `ready` | bool, read-only | Always true: Windows nodes are fully known as soon as they are listed. |

A device's `properties["icon"]` is `audio-speakers`, `audio-headphones`, `audio-headset`, `audio-input-microphone` or `audio-card`. `formFactor` is `speaker`, `headphone`, `headset`, `handset`, `microphone`, `line`, `spdif`, `hdmi`, `network` or `unknown`.

## AudioVolume

A node's volume, per channel, and its mute. Reach it as a node's `audio`, for example `Audio.defaultPlaybackDevice.audio`; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `muted` | bool | Whether the node is muted. Setting it mutes or unmutes it in Windows. |
| `volume` | real | The average of the channels, from 0 to 1: the slider position the Windows volume flyout shows. Setting it changes the Windows volume and keeps the balance between channels. Windows cannot go above 1. |
| `volumes` | list of real | The volume of each channel, from 0 to 1. Setting it changes each channel's Windows volume. A list of a different length sets every channel to its first entry. |
| `channels` | list of [AudioChannel](#audiochannel), read-only | The speaker position of each entry of `volumes`. |

An audio session has one volume in Windows. Every entry of its `volumes` shows that volume, and setting `volumes` sets it to their average.

## AudioLink

The connection between an audio session and its device: from a playback session to the device it plays to, or from a recording device to the session that records from it. Links are in `Audio.links`; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `id` | int, read-only | A number that tells links apart while the shell runs. |
| `source` | [AudioNode](#audionode), read-only | Where the sound comes from: a playback session, or a recording device. |
| `target` | [AudioNode](#audionode), read-only | Where the sound goes: a playback device, or a recording session. |
| `state` | [AudioLinkState](#audiolinkstate), read-only | `AudioLinkState.Active` while the app plays or records, `Inactive` while its session is idle. |

## AudioLinkGroup

The connections between one source and one target. Windows has one connection per session, so a group is the same pair as one [AudioLink](#audiolink). Groups are in `Audio.linkGroups` and in an [AudioLinkTracker](#audiolinktracker); QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `source` | [AudioNode](#audionode), read-only | Where the sound comes from: a playback session, or a recording device. |
| `target` | [AudioNode](#audionode), read-only | Where the sound goes: a playback device, or a recording session. |
| `state` | [AudioLinkState](#audiolinkstate), read-only | `AudioLinkState.Active` while the app plays or records, `Inactive` while its session is idle. |

## AudioLinkTracker

The link groups connected to one device: for a volume mixer, the apps playing to a speaker. Create one with `AudioLinkTracker { node: Audio.defaultPlaybackDevice }`.

| Member | Type | Description |
| --- | --- | --- |
| `node` | [AudioNode](#audionode) | The node to track. It becomes null when the node is removed. |
| `linkGroups` | list of [AudioLinkGroup](#audiolinkgroup), read-only | For a playback device, the groups that play into it. For a recording device, the groups that record from it. The list updates as sessions come and go. |

On this checkout, `linkGroups` is empty when `node` is an audio session, because a session is matched as if it were a device.

## AudioPeakMonitor

The peak level of a node's channels, read from the Windows peak meter about 30 times a second while enabled. Create one with `AudioPeakMonitor { node: Audio.defaultRecordingDevice }`.

| Member | Type | Description |
| --- | --- | --- |
| `node` | [AudioNode](#audionode) | The device or session to meter. |
| `enabled` | bool | Whether the monitor reads the meter. Defaults to true. While it is false, `peak` is 0 and the lists are empty. |
| `peak` | real, read-only | The highest of `peaks`, from 0 to 1. |
| `peaks` | list of real, read-only | The peak of each channel since the last read, from 0 to 1. |
| `channels` | list of [AudioChannel](#audiochannel), read-only | The speaker position of each entry of `peaks`. |

Windows meters a device after its volume is applied. A recording device only meters while something records from it, so the monitor keeps a recording stream open on it while it is enabled. Windows shows its microphone privacy indicator while that stream is open, so enable the monitor only while the level is on screen. The stream is Winshell's own session on the device, and `Audio.nodes` does not list it.

## AudioObjectTracker

Keeps a list of audio objects, for configurations that come from Quickshell, where an object must be tracked before all its properties are valid. Windows objects always are, so this only holds the list. Create one with `AudioObjectTracker { objects: [Audio.defaultPlaybackDevice] }`.

| Member | Type | Description |
| --- | --- | --- |
| `objects` | list | The objects to keep. Setting it has no other effect. |

## AudioNodeType

The flags in `AudioNode.type`. A node's type combines `Audio` with `Playback` or `Recording`, and with `Session` for an app's audio session. Test a flag with `(node.type & AudioNodeType.Session) !== 0`.

| Value | Meaning |
| --- | --- |
| `Untracked` | No flags. |
| `Audio` | The node carries audio. Every node has it. |
| `Session` | An app's audio session. |
| `Recording` | A recording device, or a session on one. |
| `RecordingDevice` | A recording device: `Audio` and `Recording`. |
| `RecordingSession` | An app recording from a device: `Audio`, `Session` and `Recording`. |
| `Playback` | A playback device, or a session on one. |
| `PlaybackDevice` | A playback device: `Audio` and `Playback`. |
| `PlaybackSession` | An app playing to a device: `Audio`, `Session` and `Playback`. |

## AudioLinkState

Whether an [AudioLink](#audiolink) is carrying sound. `Active` is 1 and `Inactive` is 0.

| Value | Meaning |
| --- | --- |
| `Inactive` | The app's session is open but idle. |
| `Active` | The app is playing or recording. |

## AudioChannel

A speaker position, for each entry of `AudioVolume.volumes` and `AudioPeakMonitor.peaks`. The positions come from the device's speaker layout. When Windows gives none, one channel is `Mono`, two are `FrontLeft` and `FrontRight`, and more are numbered from `AuxRangeStart` up.

Values: `Unknown`, `NA`, `Mono`, `FrontLeft`, `FrontRight`, `FrontCenter`, `LowFrequencyEffects`, `SideLeft`, `SideRight`, `FrontLeftCenter`, `FrontRightCenter`, `RearCenter`, `RearLeft`, `RearRight`, `TopCenter`, `TopFrontLeft`, `TopFrontCenter`, `TopFrontRight`, `TopRearLeft`, `TopRearCenter`, `TopRearRight`, `RearLeftCenter`, `RearRightCenter`, `FrontLeftWide`, `FrontRightWide`, `LowFrequencyEffects2`, `FrontLeftHigh`, `FrontCenterHigh`, `FrontRightHigh`, `TopFrontLeftCenter`, `TopFrontRightCenter`, `TopSideLeft`, `TopSideRight`, `LowFrequencyEffectsLeft`, `LowFrequencyEffectsRight`, `BottomCenter`, `BottomLeftCenter`, `BottomRightCenter`, `AuxRangeStart`, `AuxRangeEnd`, `CustomRangeStart`

## Examples

A volume slider and a mute button for the default playback device. Moving the slider changes the Windows volume.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Audio

Row {
    readonly property var device: Audio.defaultPlaybackDevice

    Slider {
        from: 0
        to: 1
        enabled: device !== null
        value: device ? device.audio.volume : 0
        onMoved: device.audio.volume = value
    }
    Button {
        text: device && device.audio.muted ? "Unmute" : "Mute"
        enabled: device !== null
        onClicked: device.audio.muted = !device.audio.muted
    }
}
```

A volume mixer: a slider for each app playing sound.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Audio

Column {
    Repeater {
        model: Audio.nodes
        Row {
            required property var modelData
            visible: modelData.isSession && modelData.isPlayback
            Label { width: 160; text: modelData.description }
            Slider {
                value: modelData.audio.volume
                onMoved: modelData.audio.volume = value
            }
        }
    }
}
```

A microphone level bar.

```qml
import QtQuick
import Winshell.Audio

Rectangle {
    width: 200
    height: 6
    color: "#33000000"

    AudioPeakMonitor {
        id: meter
        node: Audio.defaultRecordingDevice
    }
    Rectangle {
        width: parent.width * meter.peak
        height: parent.height
        color: "#0078d4"
    }
}
```
