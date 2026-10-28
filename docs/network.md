# Windows networking

`Winshell.Network` exposes the `Networking` singleton. Loading it reads state and subscribes to Windows network notifications; it never requests permission, changes a connection, or controls radios.

```qml
import Winshell.Network

// Read models through Networking.devices.values and Networking.profiles.values.
// Call these from explicit user actions:
// Networking.requestAccess()
// wifiDevice.scan()
// wifiNetwork.connect()                    // saved credentials or open network
// wifiNetwork.connectWithPassword(password)
// wifiNetwork.disconnect()
```

`devices` contains physical wired and Wi-Fi adapters, with Windows adapter `id`, `name`, MAC `address`, `state`, and `networks`. `WifiDevice.scan()` performs one scan, exposes `scanning` while pending, and updates the networks model. No periodic scan runs in the background. Network objects retain identity by adapter and SSID; removed networks and adapters are destroyed. Wi-Fi `signalStrength` is normalized to 0–1; `security` describes authentication. `saved` matches the SSID against saved WLAN profile names; a profile renamed separately from its SSID might not match. `forget()` removes that matching saved WLAN profile only when explicitly invoked. Wired `hasLink` and `linkSpeed` (Mbps) describe the physical link; Windows manages wired connections automatically.

`profiles` contains Windows `NetworkInformation.GetConnectionProfiles()` entries, including profiles on virtual adapters. These are connection profiles, not the saved WLAN credentials list. A profile exposes `id`, `name`, `adapterId`, `connectivity`, `cost`, `metered`, `roaming`, `overDataLimit`, `signalAvailable`, and `signalStrength`. Cost is `NetworkCost.Unknown`, `Unrestricted`, `Fixed`, or `Variable`. `metered` is true for fixed/variable cost, roaming, or an exceeded data limit; inspect `cost` to distinguish unknown cost from unrestricted. The singleton's `connectivity` follows Windows NCSI: `Unknown`, `None`, `LocalAccess`, `CaptivePortal`, or `InternetAccess`.

`wifiState` is `WifiRadioState.Unknown`, `On`, `Off`, or `Disabled`, aggregated across Wi-Fi radios. `wifiEnabled` means at least one reports On; `wifiHardwareEnabled` means at least one reports On or Off. Both are false when radio information is unavailable; inspect `wifiState` to distinguish that case. All radio properties are read-only. Adapter enumeration remains available without permission to control radios.

`airplaneMode` is read-only: `AirplaneModeState.Unknown`, `Off`, or `On`. There is no public aggregate WinRT airplane-mode API. Winshell reads the persisted `HKLM\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState` DWORD as a best-effort Windows implementation detail (0 off, 1 on). Missing, unexpected or inaccessible values produce Unknown. This is not a guaranteed real-time radio state; call `refresh()` to reread it. Never infer airplane mode from Wi-Fi or Bluetooth being off: Windows permits overriding individual radios in airplane mode.

Wi-Fi scan and connect require Windows Wi-Fi/location access. Call `Networking.requestAccess()` from a user action; this may show Windows consent UI. Observe `requestingAccess` and read-only `accessStatus` (`Unspecified`, `Allowed`, `DeniedByUser`, `DeniedBySystem`). Desktop location policy and, for packaged deployments, the `wiFiControl` capability determine availability. Repeat the request after permission changes or adapter insertion to enumerate current WiFiAdapter objects. Importing the module does not request permission. See Microsoft's [Wi-Fi access and location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes).

Actions expose `busy`/`error` on networks, `scanning`/`error` on Wi-Fi adapters, and `error` on the singleton. `operationFailed(message)` reports action failures. Overlapping actions on the same object are ignored while busy. Completion only clears the pending flag; the next Windows snapshot supplies actual connection state. Permission revocation, unavailable networks, unsupported authentication, invalid passwords and Windows errors do not imply success. Connect uses Windows automatic reconnection for the explicitly selected network. No radio toggle API is exposed.
