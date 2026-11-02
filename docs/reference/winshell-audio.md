# Winshell.Audio

Playback and recording devices, sessions, volume, mute and levels. Reach them from `Shell.Current.Audio`.

## AudioChannel

A speaker position. When Windows gives none, one channel is Mono, two are the front pair, and more count up from AuxRangeStart.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `NA` | 1 |  |
| `Mono` | 2 |  |
| `FrontLeft` | 3 |  |
| `FrontRight` | 4 |  |
| `FrontCenter` | 5 |  |
| `LowFrequencyEffects` | 6 |  |
| `SideLeft` | 7 |  |
| `SideRight` | 8 |  |
| `FrontLeftCenter` | 9 |  |
| `FrontRightCenter` | 10 |  |
| `RearCenter` | 11 |  |
| `RearLeft` | 12 |  |
| `RearRight` | 13 |  |
| `TopCenter` | 14 |  |
| `TopFrontLeft` | 15 |  |
| `TopFrontCenter` | 16 |  |
| `TopFrontRight` | 17 |  |
| `TopRearLeft` | 18 |  |
| `TopRearCenter` | 19 |  |
| `TopRearRight` | 20 |  |
| `RearLeftCenter` | 21 |  |
| `RearRightCenter` | 22 |  |
| `FrontLeftWide` | 23 |  |
| `FrontRightWide` | 24 |  |
| `LowFrequencyEffects2` | 25 |  |
| `FrontLeftHigh` | 26 |  |
| `FrontCenterHigh` | 27 |  |
| `FrontRightHigh` | 28 |  |
| `TopFrontLeftCenter` | 29 |  |
| `TopFrontRightCenter` | 30 |  |
| `TopSideLeft` | 31 |  |
| `TopSideRight` | 32 |  |
| `LowFrequencyEffectsLeft` | 33 |  |
| `LowFrequencyEffectsRight` | 34 |  |
| `BottomCenter` | 35 |  |
| `BottomLeftCenter` | 36 |  |
| `BottomRightCenter` | 37 |  |
| `AuxRangeStart` | 4096 |  |
| `AuxRangeEnd` | 8191 |  |
| `CustomRangeStart` | 65536 |  |

## AudioLink

The connection between an audio session and its device.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Id` | int, read-only | A number that tells links apart while the shell runs. |
| `Source` | [AudioNode](#audionode), read-only | Where the sound comes from: a playback session, or a recording device. |
| `State` | [AudioLinkState](#audiolinkstate), read-only | Active while the app plays or records, Inactive while its session is idle. |
| `Target` | [AudioNode](#audionode), read-only | Where the sound goes: a playback device, or a recording session. |

## AudioLinkGroup

The connections between one source and one target. Windows has one per session.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Source` | [AudioNode](#audionode), read-only | Where the sound comes from: a playback session, or a recording device. |
| `State` | [AudioLinkState](#audiolinkstate), read-only | Active while the app plays or records, Inactive while its session is idle. |
| `Target` | [AudioNode](#audionode), read-only | Where the sound goes: a playback device, or a recording session. |

## AudioLinkState

| Value | Number | Description |
| --- | --- | --- |
| `Inactive` | 0 |  |
| `Active` | 1 |  |

## AudioLinkTracker

The link groups connected to one node: the apps playing to a speaker, or the speaker one app plays to.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AudioLinkTracker()` | constructor |  |
| `LinkGroups` | ObservableCollection of [AudioLinkGroup](#audiolinkgroup), read-only |  |
| `Node` | [AudioNode](#audionode)? | The node to track. It becomes null when the node is removed. |
| `Dispose()` | void |  |

## AudioNode

A playback or recording device, or an app's audio session on one.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Audio` | [AudioVolume](#audiovolume), read-only | The node's volume and mute. Every Windows node carries audio. |
| `Description` | string, read-only | The name to show. |
| `Id` | int, read-only | A number that tells nodes apart while the shell runs. Windows does not store it. |
| `InterfaceName` | string, read-only | For a device, the description Windows gives the endpoint, such as `Speakers`. Empty for a session. |
| `IsPlayback` | bool, read-only | True for a playback device and for a session that plays to one. |
| `IsSession` | bool, read-only | True for an app's audio session, false for a device. |
| `Name` | string, read-only | For a device, the Windows endpoint id. For a session, the program's file name without `.exe`, or `system-sounds`. |
| `Properties` | IReadOnlyDictionary of string, string, read-only | More about the node. A device has `endpointId`, `friendlyName`, `interfaceName`, `flow`, `adapter`, `icon` and `formFactor`. A session has `sessionId`, `friendlyName`, `flow`, `appName`, `icon`, `processId` and `processName`. |
| `Ready` | bool, read-only | Windows nodes are always fully known, so always ready. |
| `Type` | [AudioNodeType](#audionodetype), read-only |  |

## AudioNodeType

What an AudioNode is. A node combines `Audio` with `Playback` or `Recording`, and with `Session` for an app.

These values combine.

| Value | Number | Description |
| --- | --- | --- |
| `Untracked` | 0 |  |
| `Audio` | 1 |  |
| `Session` | 4 |  |
| `Recording` | 8 |  |
| `RecordingDevice` | 9 |  |
| `RecordingSession` | 13 |  |
| `Playback` | 16 |  |
| `PlaybackDevice` | 17 |  |
| `PlaybackSession` | 21 |  |

## AudioObjectTracker

Keeps a list of audio objects, for shells that come from Quickshell, where an object must be tracked before its properties are valid. Windows objects always are, so this only holds the list.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AudioObjectTracker()` | constructor |  |
| `Objects` | IReadOnlyList of object |  |

## AudioPeakMonitor

The peak level of a node's channels, read from the Windows peak meter about 30 times a second while enabled. Windows meters a device after its volume is applied. A recording device only meters while something records from it, so this keeps a recording stream open on it, and Windows shows its microphone privacy indicator meanwhile.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AudioPeakMonitor([IEventLoop](winshell.md#ieventloop) loop)` | constructor |  |
| `Channels` | IReadOnlyList of [AudioChannel](#audiochannel), read-only | The speaker position of each entry of Peaks. |
| `Enabled` | bool | Whether the monitor reads the meter. While it is false, Peak is 0 and the lists are empty. |
| `Node` | [AudioNode](#audionode)? |  |
| `Peak` | double, read-only | The highest of Peaks, from 0 to 1. |
| `Peaks` | IReadOnlyList of double, read-only | The peak of each channel since the last read, from 0 to 1. |
| `Dispose()` | void |  |

## AudioService

Every playback device, recording device and audio session Windows has.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `DefaultPlaybackDevice` | [AudioNode](#audionode)? | The default playback device. Setting it makes that device the Windows default. |
| `DefaultRecordingDevice` | [AudioNode](#audionode)? | The default recording device. Setting it makes that device the Windows default. |
| `LinkGroups` | ObservableCollection of [AudioLinkGroup](#audiolinkgroup), read-only | One group for each entry of Links. |
| `Links` | ObservableCollection of [AudioLink](#audiolink), read-only | One link per audio session. |
| `Nodes` | ObservableCollection of [AudioNode](#audionode), read-only | Every node: the active devices and the audio sessions on them. |
| `Ready` | bool, read-only | False when Windows audio cannot be opened, and then Nodes is empty. |
| `Dispose()` | void |  |

## AudioVolume

A node's volume, per channel, and its mute.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Channels` | IReadOnlyList of [AudioChannel](#audiochannel), read-only | The speaker position of each entry of Volumes. |
| `Muted` | bool |  |
| `Volume` | double | The average of the channels, from 0 to 1: the slider position the Windows volume flyout shows. Setting it keeps the balance between channels. Windows cannot go above 1. |
| `Volumes` | IReadOnlyList of double | The volume of each channel, from 0 to 1. A list of a different length sets every channel to its first entry. A session has one volume in Windows, so every entry shows it, and setting this sets it to their average. |

