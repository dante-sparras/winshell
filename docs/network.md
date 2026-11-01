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

`devices` contains physical wired and Wi-Fi adapters, with Windows adapter `id`, `name`, MAC `address`, `state`, and `networks`. `WifiDevice.scan()` performs one scan, exposes `scanning` while pending, and updates the networks model. No periodic scan runs in the background. Network objects retain identity by adapter and SSID; removed networks and adapters are destroyed. Wi-Fi `signalStrength` is normalized to 0–1; `security` describes authentication. `saved` is true when a loaded profile's SSID matches the network, including a connected network the last scan did not report. A profile that could not be read matches by its profile name instead, so a renamed profile matches its SSID once the XML can be read. `Network.forget()` deletes the saved profile whose name is that SSID, and only when called. Wired `hasLink` and `linkSpeed` (Mbps) describe the physical link; Windows manages wired connections automatically.

`savedProfiles` lists every saved profile for that SSID. More than one can share an SSID. A saved SSID that is not in range has no network object. `WifiProfile.name` is the profile name Windows stored. `forget()` and `connectWithProfile()` use that name, and it can differ from `ssid`. `autoConnect` is the profile's connection mode. It stays at the last reported value until a later read shows the change. Setting it rewrites only that element. `clearPassword()` removes the key element and leaves every other element, including ones Winshell does not interpret. Neither call reads the password. `hasPassword` is true when a key element is present. `read()` returns those fields and never a key. `connectWithProfile()` asks Windows to connect with that profile, including the hidden-network flag when the profile says `nonBroadcast`. The call finishes when Windows accepts the request. The following snapshot reports whether the adapter associated. These writes run off the UI thread, one at a time. Loading the module or reading a snapshot does not write a profile. Windows refuses a non-admin change to an all-users profile, and that failure is reported.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Network

Column {
    required property WifiNetwork network

    Repeater {
        model: network.savedProfiles
        Button {
            required property var modelData
            text: modelData.name
            enabled: !network.busy
            onClicked: network.connectWithProfile(modelData)
        }
    }
}
```

`profiles` contains Windows `NetworkInformation.GetConnectionProfiles()` entries, including profiles on virtual adapters. These are connection profiles, not the saved WLAN profiles in `savedProfiles`. A profile exposes `id`, `name`, `adapterId`, `connectivity`, `cost`, `metered`, `roaming`, `overDataLimit`, `signalAvailable`, and `signalStrength`. Cost is `NetworkCost.Unknown`, `Unrestricted`, `Fixed`, or `Variable`. `metered` is true for fixed/variable cost, roaming, or an exceeded data limit; inspect `cost` to distinguish unknown cost from unrestricted. The singleton's `connectivity` follows Windows NCSI: `Unknown`, `None`, `LocalAccess`, `CaptivePortal`, or `InternetAccess`.

`wifiState` is `WifiRadioState.Unknown`, `On`, `Off`, or `Disabled`, aggregated across Wi-Fi radios. `wifiEnabled` means at least one reports On; `wifiHardwareEnabled` means at least one reports On or Off. Both are false when radio information is unavailable; inspect `wifiState` to distinguish that case. `wifiState` and `wifiHardwareEnabled` stay read-only. Setting `wifiEnabled` asks Windows to turn every Wi-Fi radio on or off, through `Radio.RequestAccessAsync` and then `Radio.SetStateAsync` for each radio that is not already there. The property keeps the last reported value until Windows reports the new one. A refusal, or a radio that does not reach the requested state, sets `error` and emits `operationFailed`. Turning off leaves a radio that is not on, including a disabled one, as it is. Turning on asks a disabled radio too, and Windows may refuse. If one radio changes and a later one refuses, the earlier change stands. Quickshell sets the local value first and then writes an rfkill software block. Windows does not: an allowed request can still leave the radio where hardware or policy put it. Listing adapters needs no permission to control radios. The first change can show Windows' radio-control consent. Importing the module does not, and neither does setting the value the radios already have. A packaged app declares the `radios` device capability. `requestAccess()` is the separate permission for scanning and connecting.

`airplaneMode` is read-only: `AirplaneModeState.Unknown`, `Off`, or `On`. There is no public aggregate WinRT airplane-mode API. Winshell reads the persisted `HKLM\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState` DWORD as a best-effort Windows implementation detail (0 off, 1 on). Missing, unexpected or inaccessible values produce Unknown. This is not a guaranteed real-time radio state; call `refresh()` to reread it. Never infer airplane mode from Wi-Fi or Bluetooth being off: Windows permits overriding individual radios in airplane mode.

Wi-Fi scan and connect require Windows Wi-Fi/location access. Call `Networking.requestAccess()` from a user action; this may show Windows consent UI. Observe `requestingAccess` and read-only `accessStatus` (`Unspecified`, `Allowed`, `DeniedByUser`, `DeniedBySystem`). Desktop location policy and, for packaged deployments, the `wiFiControl` capability determine availability. Repeat the request after permission changes or adapter insertion to enumerate current WiFiAdapter objects. Importing the module does not request permission. See Microsoft's [Wi-Fi access and location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes).

Actions expose `busy`/`error` on networks, `scanning`/`error` on Wi-Fi adapters, and `error` on the singleton. `operationFailed(message)` reports action failures, including a refused radio change. Overlapping actions on the same object are ignored while busy, and a second `wifiEnabled` change is ignored until the first has finished. Completion only clears the pending flag; the next Windows snapshot supplies actual connection and radio state. Permission revocation, unavailable networks, unsupported authentication, invalid passwords and Windows errors do not imply success. Connect uses Windows automatic reconnection for the explicitly selected network.
