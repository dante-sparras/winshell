# Winshell.Audio

Playback and recording devices, sessions, volume, mute and levels. Reach them from `Shell.Current.Audio`.

## AudioChannel

A speaker position, in the order of the Windows speaker mask. Without a mask, one channel is Mono, two are the front pair, and more are Unknown.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Mono` | 1 |  |
| `FrontLeft` | 2 |  |
| `FrontRight` | 3 |  |
| `FrontCenter` | 4 |  |
| `LowFrequency` | 5 |  |
| `BackLeft` | 6 |  |
| `BackRight` | 7 |  |
| `FrontLeftOfCenter` | 8 |  |
| `FrontRightOfCenter` | 9 |  |
| `BackCenter` | 10 |  |
| `SideLeft` | 11 |  |
| `SideRight` | 12 |  |
| `TopCenter` | 13 |  |
| `TopFrontLeft` | 14 |  |
| `TopFrontCenter` | 15 |  |
| `TopFrontRight` | 16 |  |
| `TopBackLeft` | 17 |  |
| `TopBackCenter` | 18 |  |
| `TopBackRight` | 19 |  |

## AudioNode

A playback or recording device, or an app's audio session on one.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Active` | bool, read-only | True while a session plays or records, false while it is open but idle. A device stays false. |
| `Channels` | IReadOnlyList of [AudioChannel](#audiochannel), read-only | The speaker position of each entry of Volumes. |
| `Description` | string, read-only | The name to show. |
| `Device` | [AudioNode](#audionode)?, read-only | The device a session plays to or records from. Null for a device. |
| `Id` | int, read-only | A number that tells nodes apart while the shell runs. Windows does not store it. |
| `InterfaceName` | string, read-only | For a device, the description Windows gives the endpoint, such as `Speakers`. Empty for a session. |
| `IsPlayback` | bool, read-only | True for a playback device and for a session that plays to one. |
| `IsSession` | bool, read-only | True for an app's audio session, false for a device. |
| `Muted` | bool |  |
| `Name` | string, read-only | For a device, the Windows endpoint id. For a session, the program's file name without `.exe`, or `system-sounds`. |
| `Properties` | IReadOnlyDictionary of string, string, read-only | More about the node. A device has `endpointId`, `friendlyName`, `interfaceName`, `flow`, `adapter`, `icon` and `formFactor`. A session has `sessionId`, `friendlyName`, `flow`, `appName`, `icon`, `processId` and `processName`. |
| `Volume` | double | The average of the channels, from 0 to 1: the slider position the Windows volume flyout shows. Setting it keeps the balance between channels. Windows cannot go above 1. |
| `Volumes` | IReadOnlyList of double | The volume of each channel, from 0 to 1. A list of a different length sets every channel to its first entry. A session has one volume in Windows, so every entry shows it, and setting this sets it to their average. |

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
| `Nodes` | ObservableCollection of [AudioNode](#audionode), read-only | Every node: the active devices and the audio sessions on them. |
| `Ready` | bool, read-only | False when Windows audio cannot be opened, and then Nodes is empty. |
| `Dispose()` | void |  |
