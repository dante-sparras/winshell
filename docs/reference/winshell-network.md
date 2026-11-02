# Winshell.Network

Adapters, Wi-Fi networks, connection profiles and connectivity. Reach them from `Shell.Current.Networking`.

## AirplaneModeState

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Off` | 1 |  |
| `On` | 2 |  |

## ConnectionFailReason

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `InvalidPassword` | 1 |  |
| `Timeout` | 2 |  |
| `NetworkUnavailable` | 3 |  |
| `UnsupportedAuthentication` | 4 |  |

## ConnectionState

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Connecting` | 1 |  |
| `Connected` | 2 |  |
| `Disconnecting` | 3 |  |
| `Disconnected` | 4 |  |

## DeviceType

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 |  |
| `Wifi` | 1 |  |
| `Wired` | 2 |  |

## Network

A network of an adapter. Wi-Fi networks are WifiNetwork objects.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Busy` | bool, read-only | An action is running. Other calls are ignored until it ends. |
| `Connected` | bool, read-only |  |
| `Device` | [NetworkDevice](#networkdevice), read-only |  |
| `Error` | string, read-only |  |
| `Name` | string, read-only | The SSID of a Wi-Fi network. For a wired network, the connection profile's name, or the adapter's. |
| `Saved` | bool, read-only | A saved profile matches this network. True for a wired network. |
| `SavedProfiles` | ObservableCollection of [WifiProfile](#wifiprofile), read-only |  |
| `State` | [ConnectionState](#connectionstate), read-only |  |
| `OperationFailed` | Action of string |  |
| `Connect()` | void | Connects to this Wi-Fi network with saved credentials. Fails for a wired network. |
| `ConnectWithProfile([WifiProfile](#wifiprofile)? profile)` | void | Connects with a saved profile of this network. The next snapshot reports whether the adapter associated. |
| `Disconnect()` | void |  |
| `Forget()` | void | Deletes the saved Wi-Fi profile whose name is this SSID. |

## NetworkAccessStatus

| Value | Number | Description |
| --- | --- | --- |
| `Unspecified` | 0 |  |
| `Allowed` | 1 |  |
| `DeniedByUser` | 2 |  |
| `DeniedBySystem` | 3 |  |

## NetworkConnectivity

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `None` | 1 |  |
| `CaptivePortal` | 2 |  |
| `LocalAccess` | 3 |  |
| `InternetAccess` | 4 |  |

## NetworkCost

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Unrestricted` | 1 |  |
| `Fixed` | 2 |  |
| `Variable` | 3 |  |

## NetworkDevice

One network adapter. Each one is a WifiDevice or a WiredDevice.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Address` | string, read-only |  |
| `Connected` | bool, read-only |  |
| `Id` | string, read-only |  |
| `Name` | string, read-only |  |
| `Networks` | ObservableCollection of [Network](#network), read-only | For Wi-Fi, the networks of the last scan and the connected network, connected first, then strongest first. |
| `State` | [ConnectionState](#connectionstate), read-only |  |
| `Type` | [DeviceType](#devicetype), read-only |  |

## NetworkProfile

A Windows connection profile reported by NetworkInformation, not a saved WLAN credential.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AdapterId` | string, read-only |  |
| `Connectivity` | [NetworkConnectivity](#networkconnectivity), read-only |  |
| `Cost` | [NetworkCost](#networkcost), read-only |  |
| `Id` | string, read-only |  |
| `Metered` | bool, read-only | True for a fixed or variable cost, roaming, or an exceeded data limit. |
| `Name` | string, read-only |  |
| `OverDataLimit` | bool, read-only |  |
| `Roaming` | bool, read-only |  |
| `SignalAvailable` | bool, read-only |  |
| `SignalStrength` | double, read-only | The signal strength, from 0 to 1. 0 when SignalAvailable is false. |

## NetworkingService

The PC's adapters, connection profiles, radios and connectivity. Reading it never asks for permission and never changes a connection or a radio.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AccessStatus` | [NetworkAccessStatus](#networkaccessstatus), read-only |  |
| `AirplaneMode` | [AirplaneModeState](#airplanemodestate), read-only | Airplane mode, read from the registry on a best-effort basis. |
| `Connectivity` | [NetworkConnectivity](#networkconnectivity), read-only |  |
| `Devices` | ObservableCollection of [NetworkDevice](#networkdevice), read-only |  |
| `Error` | string, read-only |  |
| `Profiles` | ObservableCollection of [NetworkProfile](#networkprofile), read-only |  |
| `Ready` | bool, read-only |  |
| `RequestingAccess` | bool, read-only |  |
| `WifiEnabled` | bool | Whether at least one Wi-Fi radio is on. Setting it asks Windows to turn every Wi-Fi radio on or off, and the value stays what Windows last reported until a later read shows the radios changed. |
| `WifiHardwareEnabled` | bool, read-only |  |
| `WifiState` | [WifiRadioState](#wifiradiostate), read-only |  |
| `OperationFailed` | Action of string |  |
| `Dispose()` | void |  |
| `Refresh()` | void |  |
| `RequestAccess()` | void | Asks Windows for Wi-Fi access, which may show consent UI. Call it from a user action. |

## WifiDevice

A Wi-Fi adapter.

Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
| `Error` | string, read-only |  |
| `Mode` | [WifiDeviceMode](#wifidevicemode), read-only |  |
| `Scanning` | bool, read-only |  |
| `OperationFailed` | Action of string |  |
| `Scan()` | void | Scans once and updates Networks. Needs RequestAccess first. |

## WifiDeviceMode

The 802.11 mode of a WifiDevice. Windows never reports Mesh or AdHoc.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `AccessPoint` | 1 |  |
| `Mesh` | 2 |  |
| `Station` | 3 |  |
| `AdHoc` | 4 |  |

## WifiNetwork

A Wi-Fi network in range.

Has the members of [Network](#network).

| Member | Type | Description |
| --- | --- | --- |
| `Security` | [WifiSecurityType](#wifisecuritytype), read-only |  |
| `SignalStrength` | double, read-only | The signal strength, from 0 to 1. |
| `ConnectWithPassword(string password)` | void | Connects with a password, for a secured network that isn't saved. The password is not kept. |

## WifiProfile

A saved WLAN profile for one network. It is not a Windows connection profile, and it never holds a key.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AutoConnect` | bool | Whether the connection mode is automatic. Setting it changes only that element, and the value stays what the last read reported until a later read shows the change. |
| `Error` | string, read-only |  |
| `HasPassword` | bool, read-only | Whether the profile has a key element. The key text is not read. |
| `Id` | string, read-only |  |
| `Loaded` | bool, read-only |  |
| `Name` | string, read-only |  |
| `Security` | [WifiSecurityType](#wifisecuritytype), read-only |  |
| `Ssid` | string, read-only |  |
| `OperationFailed` | Action of string |  |
| `ClearPassword()` | void | Removes every key element and writes the profile back. The key text is not read. |
| `Forget()` | void | Deletes this saved profile by its profile name. |
| `Read()` | Dictionary of string, object | The profile's fields. There is no key in the result. |

## WifiRadioState

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `On` | 1 |  |
| `Off` | 2 |  |
| `Disabled` | 3 |  |

## WifiSecurityType

| Value | Number | Description |
| --- | --- | --- |
| `Wpa3SuiteB192` | 0 |  |
| `Sae` | 1 |  |
| `Wpa2Eap` | 2 |  |
| `Wpa2Psk` | 3 |  |
| `WpaEap` | 4 |  |
| `WpaPsk` | 5 |  |
| `StaticWep` | 6 |  |
| `Owe` | 7 |  |
| `Open` | 8 |  |
| `Unknown` | 9 |  |

## WiredDevice

An Ethernet adapter. Windows manages wired connections itself.

Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
| `HasLink` | bool, read-only |  |
| `LinkSpeed` | int, read-only | The link speed in Mbps. 0 without a link. |

