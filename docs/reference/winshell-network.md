# Winshell.Network

Adapters, Wi-Fi networks, connection profiles and connectivity. Reach them from `Shell.Current.Networking`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## AirplaneModeState

Whether airplane mode is on.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows has no airplane mode setting, or it could not be read. |
| `Off` | 1 | Airplane mode is off. |
| `On` | 2 | Airplane mode is on. |

## ConnectionFailReason

Why connecting to a Wi-Fi network failed. Network.Error holds the value's name after a failed Network.Connect or WifiNetwork.ConnectWithPassword.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | A failure that is none of the others. |
| `InvalidPassword` | 1 | The network refused the password or the saved credentials. |
| `Timeout` | 2 | The network did not answer in time. |
| `NetworkUnavailable` | 3 | The network is not in the last scan, or Windows has not given access to the adapter. |
| `UnsupportedAuthentication` | 4 | The network signs in with a method Windows can't use here. |

## ConnectionState

How far an adapter, or a network on it, is in connecting.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows reported a state that is none of the others. |
| `Connecting` | 1 | The adapter is finding, joining or signing in to the network. |
| `Connected` | 2 | The adapter is connected. |
| `Disconnecting` | 3 | The adapter is leaving the network. |
| `Disconnected` | 4 | The adapter is not connected. |

## DeviceType

The kind of a network adapter.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 | Neither Wi-Fi nor Ethernet. No listed device has this type. |
| `Wifi` | 1 | A Wi-Fi adapter. |
| `Wired` | 2 | An Ethernet adapter. |

## Network

A network of an adapter. Wi-Fi networks are WifiNetwork objects.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Busy` | bool, read-only | An action is running. Other calls are ignored until it ends. |
| `Connected` | bool, read-only | Whether the adapter is connected to this network. |
| `Device` | [NetworkDevice](#networkdevice), read-only | The adapter this network is on. |
| `Error` | string, read-only | Why the last action failed. Empty while an action runs and after one succeeds. |
| `Name` | string, read-only | The SSID of a Wi-Fi network. For a wired network, the connection profile's name, or the adapter's. |
| `Saved` | bool, read-only | A saved profile matches this network. True for a wired network. |
| `SavedProfiles` | ObservableCollection of [WifiProfile](#wifiprofile), read-only | The saved Wi-Fi profiles for this network: those whose SSID is its name, and those that can't be read whose profile name is. Empty for a wired network. |
| `State` | [ConnectionState](#connectionstate), read-only | The adapter's connection state on this network. ConnectionState.Disconnected for every Wi-Fi network the adapter is not on. |
| `OperationFailed` | Action of string | An action failed, with the same text as Error. |
| `Connect()` | void | Connects to this Wi-Fi network with saved credentials. Fails for a wired network. |
| `ConnectWithProfile([WifiProfile](#wifiprofile)? profile)` | void | Connects with a saved profile of this network. The next snapshot reports whether the adapter associated. |
| `Disconnect()` | void | Disconnects this network's Wi-Fi adapter. Fails for a wired network, and until NetworkingService.RequestAccess is allowed. |
| `Forget()` | void | Deletes the saved Wi-Fi profile whose name is this SSID. |

## NetworkAccessStatus

Whether Windows lets Winshell scan and connect Wi-Fi networks.

| Value | Number | Description |
| --- | --- | --- |
| `Unspecified` | 0 | Not asked yet, or the last request failed. |
| `Allowed` | 1 | Windows allows scanning and connecting. |
| `DeniedByUser` | 2 | The user denied access. |
| `DeniedBySystem` | 3 | Windows denied access, or refused a scan. |

## NetworkConnectivity

What a connection can reach.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Not read yet, or the last read failed. |
| `None` | 1 | No connection. |
| `CaptivePortal` | 2 | Limited internet access, usually until someone signs in on a captive portal page. |
| `LocalAccess` | 3 | The local network only, without the internet. |
| `InternetAccess` | 4 | The internet. |

## NetworkCost

How a connection's data is charged, as Windows reports it.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows does not know the cost. |
| `Unrestricted` | 1 | Data is not limited. |
| `Fixed` | 2 | Data is free up to a limit. |
| `Variable` | 3 | Data is charged by the byte. |

## NetworkDevice

One network adapter. Each one is a WifiDevice or a WiredDevice.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Address` | string, read-only | The MAC address, as six uppercase hex pairs joined by colons. |
| `Connected` | bool, read-only | Whether the adapter is connected. |
| `Id` | string, read-only | The adapter's interface GUID. NetworkProfile.AdapterId matches it. |
| `Name` | string, read-only | The adapter's connection name, such as `Wi-Fi` or `Ethernet 2`. |
| `Networks` | ObservableCollection of [Network](#network), read-only | For Wi-Fi, the networks of the last scan and the connected network, connected first, then strongest first. |
| `State` | [ConnectionState](#connectionstate), read-only | The adapter's connection state. A wired adapter is only ConnectionState.Connected or ConnectionState.Disconnected. |
| `Type` | [DeviceType](#devicetype), read-only | Whether the adapter is Wi-Fi or wired. |

## NetworkProfile

A Windows connection profile reported by NetworkInformation, not a saved WLAN credential.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AdapterId` | string, read-only | The NetworkDevice.Id of the adapter the profile is on. |
| `Connectivity` | [NetworkConnectivity](#networkconnectivity), read-only | What this connection can reach. |
| `Cost` | [NetworkCost](#networkcost), read-only | How this connection's data is charged. |
| `Id` | string, read-only | The adapter's ID and the profile's name, joined by a colon. |
| `Metered` | bool, read-only | True for a fixed or variable cost, roaming, or an exceeded data limit. |
| `Name` | string, read-only | The profile's name, as Windows shows the connection. For Wi-Fi it is usually the SSID. |
| `OverDataLimit` | bool, read-only | Whether the connection has used more data than its plan's limit. |
| `Roaming` | bool, read-only | Whether the connection is roaming, outside its provider's own network. |
| `SignalAvailable` | bool, read-only | Whether Windows reports a signal for this connection. False for a wired one. |
| `SignalStrength` | double, read-only | The signal strength, from 0 to 1. 0 when SignalAvailable is false. |

## NetworkingService

The PC's adapters, connection profiles, radios and connectivity. Reading it never asks for permission and never changes a connection or a radio.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AccessStatus` | [NetworkAccessStatus](#networkaccessstatus), read-only | Whether Windows lets Winshell scan and connect Wi-Fi. NetworkAccessStatus.Unspecified until RequestAccess has an answer. |
| `AirplaneMode` | [AirplaneModeState](#airplanemodestate), read-only | Airplane mode, read from the registry on a best-effort basis. |
| `Connectivity` | [NetworkConnectivity](#networkconnectivity), read-only | What the connection Windows uses for the internet can reach. NetworkConnectivity.None without such a connection. |
| `Devices` | ObservableCollection of [NetworkDevice](#networkdevice), read-only | The PC's Wi-Fi and Ethernet adapters. Virtual adapters, VPNs and Bluetooth are left out. |
| `Error` | string, read-only | Why the last read, access request or Wi-Fi radio change failed. Empty when none did. |
| `Profiles` | ObservableCollection of [NetworkProfile](#networkprofile), read-only | The Windows connection profiles that are on an adapter now. |
| `Ready` | bool, read-only | Whether the first read has finished. False again while the last read failed, and Error says why. |
| `RequestingAccess` | bool, read-only | Whether an access request is waiting for Windows. RequestAccess does nothing until it ends. |
| `WifiEnabled` | bool | Whether at least one Wi-Fi radio is on. Setting it asks Windows to turn on or off every Wi-Fi radio the last read found, and the value stays what Windows last reported until a later read shows the radios changed. |
| `WifiHardwareEnabled` | bool, read-only | Whether at least one Wi-Fi radio is on or off, not disabled, so software can turn it on. |
| `WifiState` | [WifiRadioState](#wifiradiostate), read-only | The state of the Wi-Fi radios, taken together. |
| `OperationFailed` | Action of string | An access request or a radio change failed. |
| `Dispose()` | void | Stops following network changes. Its devices, networks and profiles then ignore actions. |
| `Refresh()` | void | Reads the adapters, profiles, radios and connectivity again soon. |
| `RequestAccess()` | void | Asks Windows for Wi-Fi access, which may show consent UI. Call it from a user action. |

## WifiDevice

A Wi-Fi adapter.

Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
| `Error` | string, read-only | Why the last scan failed. Empty while a scan runs and after one succeeds. |
| `Mode` | [WifiDeviceMode](#wifidevicemode), read-only | The adapter's 802.11 mode. |
| `Scanning` | bool, read-only | Whether a scan is running. Scan does nothing until it ends. |
| `OperationFailed` | Action of string | A scan failed. |
| `Scan()` | void | Scans once and updates NetworkDevice.Networks. Needs NetworkingService.RequestAccess first. |

## WifiDeviceMode

The 802.11 mode of a WifiDevice. Windows never reports Mesh or AdHoc.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows reported no mode, or a mode that is none of the others. |
| `AccessPoint` | 1 | The adapter hosts a network, as Mobile hotspot does. |
| `Mesh` | 2 | An 802.11s mesh. Never reported. |
| `Station` | 3 | The adapter joins networks as a client. |
| `AdHoc` | 4 | A peer-to-peer ad hoc network. Never reported. |

## WifiNetwork

A Wi-Fi network in range.

Has the members of [Network](#network).

| Member | Type | Description |
| --- | --- | --- |
| `Security` | [WifiSecurityType](#wifisecuritytype), read-only | How the network is secured. |
| `SignalStrength` | double, read-only | The signal strength, from 0 to 1. |
| `ConnectWithPassword(string password)` | void | Connects with a password, for a secured network that isn't saved. The password is not kept. |

## WifiProfile

A saved WLAN profile for one network. It is not a Windows connection profile, and it never holds a key.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AutoConnect` | bool | Whether the connection mode is automatic. Setting it changes only that element, and the value stays what the last read reported until a later read shows the change. |
| `Error` | string, read-only | Why the last change failed, or else why the profile can't be read. Empty when neither did. |
| `HasPassword` | bool, read-only | Whether the profile has a key element. The key text is not read. |
| `Id` | string, read-only | The adapter's ID and the profile's name, joined by a colon. |
| `Loaded` | bool, read-only | Whether the profile could be read. When false, only Id and Name are set. |
| `Name` | string, read-only | The profile's name in Windows. Usually the SSID, but it can differ. |
| `Security` | [WifiSecurityType](#wifisecuritytype), read-only | How the profile's network is secured. WifiSecurityType.Unknown when Loaded is false. |
| `Ssid` | string, read-only | The SSID the profile connects to. Empty when Loaded is false. |
| `OperationFailed` | Action of string | Setting AutoConnect, ClearPassword or Forget failed. |
| `ClearPassword()` | void | Removes every key element and writes the profile back. The key text is not read. |
| `Forget()` | void | Deletes this saved profile by its profile name. |
| `Read()` | Dictionary of string, object | The profile's fields. There is no key in the result. |

## WifiRadioState

The state of the PC's Wi-Fi radios, taken together.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | No Wi-Fi radio was found, Windows would not list them, or none reports its state. |
| `On` | 1 | At least one Wi-Fi radio is on. |
| `Off` | 2 | No Wi-Fi radio is on, and at least one can be turned on. |
| `Disabled` | 3 | No Wi-Fi radio is on or off, and at least one is disabled, as by a hardware switch, so software can't turn one on. |

## WifiSecurityType

How a Wi-Fi network or a saved profile is secured.

| Value | Number | Description |
| --- | --- | --- |
| `Wpa3SuiteB192` | 0 | WPA3-Enterprise in 192-bit mode. |
| `Sae` | 1 | WPA3-Personal, which signs in with SAE. |
| `Wpa2Eap` | 2 | WPA2-Enterprise, or WPA3-Enterprise without 192-bit mode. |
| `Wpa2Psk` | 3 | WPA2-Personal, with a password. |
| `WpaEap` | 4 | WPA-Enterprise. |
| `WpaPsk` | 5 | WPA-Personal, with a password. |
| `StaticWep` | 6 | WEP, with a fixed key. |
| `Owe` | 7 | Enhanced Open: encrypted, with no password. |
| `Open` | 8 | Open: no password and no encryption. |
| `Unknown` | 9 | Windows did not report the security, or it is none of the others. |

## WiredDevice

An Ethernet adapter. Windows manages wired connections itself.

Has the members of [NetworkDevice](#networkdevice).

| Member | Type | Description |
| --- | --- | --- |
| `HasLink` | bool, read-only | Whether the adapter has a link, such as a plugged-in cable. |
| `LinkSpeed` | int, read-only | The link speed in Mbps. 0 without a link. |
