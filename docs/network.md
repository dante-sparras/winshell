# Windows networking

`Winshell.Network` exposes the `Networking` singleton. Loading it reads state and subscribes to Windows network notifications. It never requests permission or changes a connection or a radio on its own. Setting `wifiEnabled` asks Windows to change the Wi-Fi radios.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Network

// The Wi-Fi networks of the first Wi-Fi adapter. Each call runs only when the user clicks.
Column {
    readonly property var wifi: Networking.devices.values.find(d => d.type === DeviceType.Wifi) ?? null

    Button { text: "Allow Wi-Fi access"; onClicked: Networking.requestAccess() }
    Button { text: "Scan"; enabled: wifi !== null && !wifi.scanning; onClicked: wifi.scan() }
    Repeater {
        model: wifi ? wifi.networks : []
        Button {
            required property var modelData
            text: modelData.name + (modelData.connected ? " (connected)" : "")
            enabled: !modelData.busy
            // connectWithPassword(password) for a secured network that isn't saved; forget() removes a saved one.
            onClicked: modelData.connected ? modelData.disconnect() : modelData.connect()
        }
    }
}
```

`devices` contains physical wired and Wi-Fi adapters, with Windows adapter `id`, `name`, MAC `address`, `state`, and `networks`. `WifiDevice.mode` is read with that snapshot. An extensible station is `Station` and an extensible access point is `AccessPoint`. Windows 11 does not report an ad-hoc network, so `AdHoc` is never returned, and Windows has no 802.11s source, so `Mesh` is never returned. Any other operation mode, or a query that fails, is `Unknown` and the rest of the snapshot still arrives. The read does not use the current connection or the BSS list, so it does not need location access. A wired adapter has no `mode`. `WifiDevice.scan()` performs one scan, exposes `scanning` while pending, and updates the networks model. No periodic scan runs in the background. Network objects retain identity by adapter and SSID; removed networks and adapters are destroyed. Wi-Fi `signalStrength` is normalized to 0–1; `security` describes authentication. `saved` matches the SSID against saved WLAN profile names, including a connected network the last scan did not report; a profile renamed separately from its SSID might not match. `forget()` removes that matching saved WLAN profile only when explicitly invoked. Wired `hasLink` and `linkSpeed` (Mbps) describe the physical link; Windows manages wired connections automatically.

`profiles` contains Windows `NetworkInformation.GetConnectionProfiles()` entries, including profiles on virtual adapters. These are connection profiles, not the saved WLAN credentials list. A profile exposes `id`, `name`, `adapterId`, `connectivity`, `cost`, `metered`, `roaming`, `overDataLimit`, `signalAvailable`, and `signalStrength`. Cost is `NetworkCost.Unknown`, `Unrestricted`, `Fixed`, or `Variable`. `metered` is true for fixed/variable cost, roaming, or an exceeded data limit; inspect `cost` to distinguish unknown cost from unrestricted. The singleton's `connectivity` follows Windows NCSI: `Unknown`, `None`, `LocalAccess`, `CaptivePortal`, or `InternetAccess`.

`wifiState` is `WifiRadioState.Unknown`, `On`, `Off`, or `Disabled`, aggregated across Wi-Fi radios. `wifiEnabled` means at least one reports On; `wifiHardwareEnabled` means at least one reports On or Off. Both are false when radio information is unavailable; inspect `wifiState` to distinguish that case. `wifiState` and `wifiHardwareEnabled` stay read-only. Setting `wifiEnabled` asks Windows to turn every Wi-Fi radio on or off, through `Radio.RequestAccessAsync` and then `Radio.SetStateAsync` for each radio that is not already there. The property keeps the last reported value until Windows reports the new one. A refusal, or a radio that does not reach the requested state, sets `error` and emits `operationFailed`. Turning off leaves a radio that is not on, including a disabled one, as it is. Turning on asks a disabled radio too, and Windows may refuse. If one radio changes and a later one refuses, the earlier change stands. Quickshell sets the local value first and then writes an rfkill software block. Windows does not: an allowed request can still leave the radio where hardware or policy put it. Listing adapters needs no permission to control radios. The first change can show Windows' radio-control consent. Importing the module does not, and neither does setting the value the radios already have. A packaged app declares the `radios` device capability. `requestAccess()` is the separate permission for scanning and connecting.

`airplaneMode` is read-only: `AirplaneModeState.Unknown`, `Off`, or `On`. There is no public aggregate WinRT airplane-mode API. Winshell reads the persisted `HKLM\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState` DWORD as a best-effort Windows implementation detail (0 off, 1 on). Missing, unexpected or inaccessible values produce Unknown. This is not a guaranteed real-time radio state; call `refresh()` to reread it. Never infer airplane mode from Wi-Fi or Bluetooth being off: Windows permits overriding individual radios in airplane mode.

Wi-Fi scan and connect require Windows Wi-Fi/location access. Call `Networking.requestAccess()` from a user action; this may show Windows consent UI. Observe `requestingAccess` and read-only `accessStatus` (`Unspecified`, `Allowed`, `DeniedByUser`, `DeniedBySystem`). Desktop location policy and, for packaged deployments, the `wiFiControl` capability determine availability. Repeat the request after permission changes or adapter insertion to enumerate current WiFiAdapter objects. Importing the module does not request permission. See Microsoft's [Wi-Fi access and location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes).

Actions expose `busy`/`error` on networks, `scanning`/`error` on Wi-Fi adapters, and `error` on the singleton. `operationFailed(message)` reports action failures, including a refused radio change. Overlapping actions on the same object are ignored while busy, and a second `wifiEnabled` change is ignored until the first has finished. Completion only clears the pending flag; the next Windows snapshot supplies actual connection and radio state. Permission revocation, unavailable networks, unsupported authentication, invalid passwords and Windows errors do not imply success. Connect uses Windows automatic reconnection for the explicitly selected network.
