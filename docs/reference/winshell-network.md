# Winshell.Network

The PC's wired and Wi-Fi adapters, the Wi-Fi networks in range, Windows connection profiles, saved Wi-Fi profiles, and connectivity. Loading the module reads state and follows Windows network notifications. It never asks for permission or changes a connection or a radio. It also reads saved Wi-Fi profiles and does not change them. A configuration writes one only by setting `WifiProfile.autoConnect` or calling `clearPassword()`, and deletes one only by calling `forget()`. The password stays in Windows and is never read. Setting `wifiEnabled` asks Windows to change the Wi-Fi radios, and that request can show consent UI. [Windows networking](../network.md) explains permissions, scanning and airplane mode.

`import Winshell.Network`

## Networking

The singleton for the whole shell. It lists the adapters and connection profiles and reports the radio, access and connectivity state.

| Member | Type | Description |
| --- | --- | --- |
| `devices` | ObjectModel of [NetworkDevice](#networkdevice), read-only | The physical wired and Wi-Fi adapters, each a [WifiDevice](#wifidevice) or a [WiredDevice](#wireddevice). Virtual, VPN and Bluetooth adapters are left out. |
| `profiles` | ObjectModel of [NetworkProfile](#networkprofile), read-only | The Windows connection profiles, including those on virtual adapters. |
| `ready` | bool, read-only | Whether the first read of Windows state has completed. |
| `wifiEnabled` | bool | Whether at least one Wi-Fi radio is on. False when radio state is unavailable. Setting it asks Windows to turn every Wi-Fi radio on or off. The value stays what Windows last reported until a later read shows the radios changed. |
| `wifiState` | [WifiRadioState](#wifiradiostate), read-only | The state of the Wi-Fi radios together. |
| `wifiHardwareEnabled` | bool, read-only | Whether at least one Wi-Fi radio is on or off, rather than disabled. False when radio state is unavailable. |
| `accessStatus` | [NetworkAccessStatus](#networkaccessstatus), read-only | Whether Windows lets the shell scan and connect Wi-Fi. |
| `airplaneMode` | [AirplaneModeState](#airplanemodestate), read-only | Airplane mode, read from the registry on a best-effort basis. Not a guaranteed real-time state. |
| `connectivity` | [NetworkConnectivity](#networkconnectivity), read-only | The connectivity of the profile Windows uses for the internet. |
| `error` | string, read-only | Why the last read, access request or radio change failed. Empty when it succeeded. |
| `requestingAccess` | bool, read-only | True while `requestAccess()` is running. |
| `refresh()` | method | Reads the network state again, including airplane mode. |
| `requestAccess()` | method | Asks Windows for Wi-Fi access, which may show Windows consent UI. Call it from a user action. Repeat it after an adapter is inserted. |
| `operationFailed(message)` | signal | Emitted when an access request or a radio change fails. |

## NetworkDevice

One network adapter, reached from `Networking.devices`. Each one is a [WifiDevice](#wifidevice) or a [WiredDevice](#wireddevice); check `type` to tell them apart.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | The Windows adapter id. |
| `type` | [DeviceType](#devicetype), read-only | `DeviceType.Wifi` or `DeviceType.Wired`. |
| `name` | string, read-only | The adapter's name, as Windows shows it. |
| `networks` | ObjectModel of [Network](#network), read-only | For Wi-Fi, the networks of the last scan and the connected network, connected first, then strongest first. For a wired adapter, one network while the cable has a link. |
| `address` | string, read-only | The MAC address. |
| `connected` | bool, read-only | Whether `state` is `Connected`. |
| `state` | [ConnectionState](#connectionstate), read-only | The adapter's connection state. A wired adapter is `Connected` while it is up. |

## Network

A network of an adapter, reached from `NetworkDevice.networks`. Wi-Fi networks are [WifiNetwork](#wifinetwork) objects. A network keeps its object while its adapter reports the same name.

| Member | Type | Description |
| --- | --- | --- |
| `name` | string, read-only | The SSID of a Wi-Fi network. For a wired network, the connection profile's name, or the adapter's. |
| `device` | [NetworkDevice](#networkdevice), read-only | The adapter that sees this network. |
| `connected` | bool, read-only | Whether `state` is `Connected`. |
| `saved` | bool, read-only | Whether a loaded saved profile's SSID matches this network, including a connected network the last scan did not report. A profile that could not be read matches by its profile name. True for a wired network. |
| `state` | [ConnectionState](#connectionstate), read-only | The network's connection state. A Wi-Fi network the adapter is not on is `Disconnected`. |
| `busy` | bool, read-only | True while `connect()`, `connectWithProfile()`, `disconnect()` or `forget()` runs. Other calls are ignored until it ends. |
| `error` | string, read-only | Why the last action failed. Empty when it succeeded. |
| `savedProfiles` | ObjectModel of [WifiProfile](#wifiprofile), read-only | The saved profiles for this network. A loaded profile matches by its SSID. One that could not be read matches by its profile name. A profile keeps its object while its id stays. A saved SSID that is not in range has no network object. |
| `connect()` | method | Connects to this Wi-Fi network with saved credentials, and lets Windows reconnect to it automatically. Needs Wi-Fi access and the network in the last scan. Fails for a wired network. |
| `connectWithProfile(profile)` | method | Connects with that saved profile. The profile has to belong to this network. The call finishes when Windows accepts the request, and the next snapshot reports whether the adapter associated. A profile marked hidden is connected as a hidden network. Uses the same `busy`, `error` and `operationFailed` as `connect()`. Fails for a wired network. |
| `disconnect()` | method | Disconnects the Wi-Fi adapter. Needs Wi-Fi access. Fails for a wired network. |
| `forget()` | method | Deletes the saved Wi-Fi profile whose name is this SSID. Fails for a wired network. A profile whose name differs from the SSID is removed with `WifiProfile.forget()`. |
| `operationFailed(message)` | signal | Emitted when an action fails, with the same text as `error`. |

## WifiNetwork

A Wi-Fi network in range, reached from a [WifiDevice](#wifidevice)'s `networks`. Has the members of [Network](#network).

| Member | Type | Description |
| --- | --- | --- |
| `signalStrength` | real, read-only | The signal strength, from 0 to 1. |
| `security` | [WifiSecurityType](#wifisecuritytype), read-only | How the network authenticates. |
| `connectWithPassword(password)` | method | Connects with a password, for a secured network that isn't saved. Lets Windows reconnect to it automatically. |

## WifiProfile

A saved WLAN profile for one network, reached from `Network.savedProfiles`. It is not a Windows connection profile, and QML cannot create one. It never holds a password. More than one profile can share an SSID.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | The adapter id, a colon, and the profile name. |
| `name` | string, read-only | The WLAN profile name. `forget()` and `connectWithProfile()` use it. It can differ from `ssid`. |
| `ssid` | string, read-only | The SSID stored in the profile. Empty when the profile could not be read, or when the SSID text was not valid. |
| `autoConnect` | bool | Whether the profile's connection mode is automatic. Setting it changes only that element, to `auto` or `manual`, and keeps every other element Windows stored. The value stays what the last read reported until a later read shows the change. A failure sets `error` and emits `operationFailed`, and the value does not change. Windows can refuse a change to an all-users profile when the process is not an administrator. That refusal is reported. |
| `security` | [WifiSecurityType](#wifisecuritytype), read-only | How the profile authenticates. |
| `hasPassword` | bool, read-only | Whether the profile has a key element. The key text is not read. |
| `loaded` | bool, read-only | Whether the profile XML was read. False when it could not be. `error` then says why. A failed read does not emit `operationFailed`. |
| `error` | string, read-only | Why the last action failed, or why the profile could not be read. Empty when both succeeded. |
| `read()` | method | Returns `id`, `name`, `ssid`, `autoConnect`, `security`, `hasPassword` and `loaded`. `security` is the value's name. There is no key. |
| `clearPassword()` | method | Removes every key element and writes the profile back. Other elements stay, including ones this module does not interpret. When there is no key element, nothing is written. The key text is not read. A failure sets `error` and emits `operationFailed`. |
| `forget()` | method | Deletes this saved profile by its profile name. A failure sets `error` and emits `operationFailed`. |
| `operationFailed(message)` | signal | Emitted when setting `autoConnect`, `clearPassword()` or `forget()` fails, with the same text as `error`. |

## WifiDevice

A Wi-Fi adapter, reached from `Networking.devices`. Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
| `mode` | [WifiDeviceMode](#wifidevicemode), read-only | The 802.11 mode the adapter is in. |
| `scanning` | bool, read-only | True while `scan()` runs. |
| `error` | string, read-only | Why the last scan failed. Empty when it succeeded. |
| `scan()` | method | Scans once and updates `networks`. Needs `Networking.requestAccess()` first. No scan runs in the background. |
| `operationFailed(message)` | signal | Emitted when a scan fails. |

## WiredDevice

An Ethernet adapter, reached from `Networking.devices`. Windows manages wired connections itself. Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
| `linkSpeed` | int, read-only | The link speed in Mbps. 0 without a link. |
| `hasLink` | bool, read-only | Whether the cable has a physical link. |

## NetworkProfile

A Windows connection profile, reached from `Networking.profiles`. It is not a saved Wi-Fi credential.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | Identifies the profile by its adapter id and name. |
| `name` | string, read-only | The profile's name. |
| `adapterId` | string, read-only | The id of the adapter the profile is on. |
| `connectivity` | [NetworkConnectivity](#networkconnectivity), read-only | The profile's connectivity. |
| `cost` | [NetworkCost](#networkcost), read-only | How the connection is billed. |
| `metered` | bool, read-only | True for a fixed or variable cost, roaming, or an exceeded data limit. Check `cost` to tell an unknown cost from an unrestricted one. |
| `roaming` | bool, read-only | Whether the connection is roaming. |
| `overDataLimit` | bool, read-only | Whether the data limit is exceeded. |
| `signalAvailable` | bool, read-only | Whether Windows reports a signal strength for the profile. |
| `signalStrength` | real, read-only | The signal strength, from 0 to 1. 0 when `signalAvailable` is false. |

## AirplaneModeState

The values of `Networking.airplaneMode`. `Unknown` when the registry value is missing, unexpected or unreadable.

Values: `Unknown`, `Off`, `On`

## ConnectionFailReason

Why a Wi-Fi connection failed. No property has this type: a failed `connect()` or `connectWithPassword()` puts the value's name, such as `InvalidPassword`, in the network's `error`.

Values: `Unknown`, `InvalidPassword`, `Timeout`, `NetworkUnavailable`, `UnsupportedAuthentication`

## ConnectionState

The values of `NetworkDevice.state` and `Network.state`.

Values: `Unknown`, `Connecting`, `Connected`, `Disconnecting`, `Disconnected`

## DeviceType

The values of `NetworkDevice.type`. Devices in `Networking.devices` are never `None`.

Values: `None`, `Wifi`, `Wired`

## NetworkAccessStatus

The values of `Networking.accessStatus`.

Values: `Unspecified`, `Allowed`, `DeniedByUser`, `DeniedBySystem`

## NetworkConnectivity

The values of `connectivity`, following Windows' network connectivity check. `CaptivePortal` is constrained internet access.

Values: `Unknown`, `None`, `CaptivePortal`, `LocalAccess`, `InternetAccess`

## NetworkCost

The values of `NetworkProfile.cost`.

Values: `Unknown`, `Unrestricted`, `Fixed`, `Variable`

## WifiDeviceMode

The values of `WifiDevice.mode`. `Station` is an extensible station, the usual client. `AccessPoint` is an extensible access point, a local hotspot. Windows has no 802.11s mesh mode, so `Mesh` is never reported. Windows 11 does not report an ad-hoc BSS, so `AdHoc` is never reported. Any other operation mode, and a query Windows does not answer, is `Unknown`. `WifiDeviceMode.toString(mode)` returns the value's name.

Values: `Unknown`, `AccessPoint`, `Mesh`, `Station`, `AdHoc`

## WifiRadioState

The values of `Networking.wifiState`: `On` when any Wi-Fi radio is on, `Disabled` when all are disabled, `Unknown` when Windows reports no radio.

Values: `Unknown`, `On`, `Off`, `Disabled`

## WifiSecurityType

The values of `WifiNetwork.security`. `Sae` is WPA3 Personal; `Owe` is Enhanced Open.

Values: `Wpa3SuiteB192`, `Sae`, `Wpa2Eap`, `Wpa2Psk`, `WpaEap`, `WpaPsk`, `StaticWep`, `Owe`, `Open`, `Unknown`
