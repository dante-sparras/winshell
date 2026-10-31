# Winshell.Network

The PC's wired and Wi-Fi adapters, the Wi-Fi networks in range, Windows connection profiles, and connectivity. Loading the module reads state and follows Windows network notifications; it never asks for permission, changes a connection, or controls a radio. [Windows networking](../network.md) explains permissions, scanning and airplane mode.

`import Winshell.Network`

## Networking

The singleton for the whole shell. It lists the adapters and connection profiles and reports the radio, access and connectivity state.

| Member | Type | Description |
| --- | --- | --- |
| `devices` | ObjectModel of [NetworkDevice](#networkdevice), read-only | The physical wired and Wi-Fi adapters, each a [WifiDevice](#wifidevice) or a [WiredDevice](#wireddevice). Virtual, VPN and Bluetooth adapters are left out. |
| `profiles` | ObjectModel of [NetworkProfile](#networkprofile), read-only | The Windows connection profiles, including those on virtual adapters. |
| `ready` | bool, read-only | Whether the first read of Windows state has completed. |
| `wifiEnabled` | bool, read-only | Whether at least one Wi-Fi radio is on. False when radio state is unavailable. |
| `wifiState` | [WifiRadioState](#wifiradiostate), read-only | The state of the Wi-Fi radios together. |
| `wifiHardwareEnabled` | bool, read-only | Whether at least one Wi-Fi radio is on or off, rather than disabled. False when radio state is unavailable. |
| `accessStatus` | [NetworkAccessStatus](#networkaccessstatus), read-only | Whether Windows lets the shell scan and connect Wi-Fi. |
| `airplaneMode` | [AirplaneModeState](#airplanemodestate), read-only | Airplane mode, read from the registry on a best-effort basis. Not a guaranteed real-time state. |
| `connectivity` | [NetworkConnectivity](#networkconnectivity), read-only | The connectivity of the profile Windows uses for the internet. |
| `error` | string, read-only | Why the last read or access request failed. Empty when it succeeded. |
| `requestingAccess` | bool, read-only | True while `requestAccess()` is running. |
| `refresh()` | method | Reads the network state again, including airplane mode. |
| `requestAccess()` | method | Asks Windows for Wi-Fi access, which may show Windows consent UI. Call it from a user action. Repeat it after an adapter is inserted. |
| `operationFailed(message)` | signal | Emitted when an access request fails. |

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
| `saved` | bool, read-only | Whether a saved Wi-Fi profile has this SSID as its name, including a connected network the last scan did not report. True for a wired network. |
| `state` | [ConnectionState](#connectionstate), read-only | The network's connection state. A Wi-Fi network the adapter is not on is `Disconnected`. |
| `busy` | bool, read-only | True while `connect()`, `disconnect()` or `forget()` runs. Other calls are ignored until it ends. |
| `error` | string, read-only | Why the last action failed. Empty when it succeeded. |
| `connect()` | method | Connects to this Wi-Fi network with saved credentials, and lets Windows reconnect to it automatically. Needs Wi-Fi access and the network in the last scan. Fails for a wired network. |
| `disconnect()` | method | Disconnects the Wi-Fi adapter. Needs Wi-Fi access. Fails for a wired network. |
| `forget()` | method | Deletes the saved Wi-Fi profile with this SSID as its name. Fails for a wired network. |
| `operationFailed(message)` | signal | Emitted when an action fails, with the same text as `error`. |

## WifiNetwork

A Wi-Fi network in range, reached from a [WifiDevice](#wifidevice)'s `networks`. Has the members of [Network](#network).

| Member | Type | Description |
| --- | --- | --- |
| `signalStrength` | real, read-only | The signal strength, from 0 to 1. |
| `security` | [WifiSecurityType](#wifisecuritytype), read-only | How the network authenticates. |
| `connectWithPassword(password)` | method | Connects with a password, for a secured network that isn't saved. Lets Windows reconnect to it automatically. |

## WifiDevice

A Wi-Fi adapter, reached from `Networking.devices`. Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
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

## WifiRadioState

The values of `Networking.wifiState`: `On` when any Wi-Fi radio is on, `Disabled` when all are disabled, `Unknown` when Windows reports no radio.

Values: `Unknown`, `On`, `Off`, `Disabled`

## WifiSecurityType

The values of `WifiNetwork.security`. `Sae` is WPA3 Personal; `Owe` is Enhanced Open.

Values: `Wpa3SuiteB192`, `Sae`, `Wpa2Eap`, `Wpa2Psk`, `WpaEap`, `WpaPsk`, `StaticWep`, `Owe`, `Open`, `Unknown`
