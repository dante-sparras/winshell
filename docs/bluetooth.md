# Windows Bluetooth

`Winshell.Bluetooth` exposes the `Bluetooth` singleton. Reading it enumerates the default Windows Bluetooth adapter and paired classic/LE association endpoints. Reading never requests radio-control access, pairs, connects, opens a remote device/GATT handle, or changes a radio. Setting `defaultAdapter.enabled` asks Windows to change that adapter's radio. The service starts monitoring when it is created, and while monitoring is on, state refreshes every three seconds. `stopMonitoring()` stops that read and `startMonitoring()` starts it again. `refresh()` requests an earlier read. The default shell stops monitoring until Quick settings is open. Each shell generation owns its backend and stops its discovery watchers when destroyed.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Bluetooth

// Paired devices, and nearby ones while discovering. Each call runs only when the user clicks.
Column {
    Button {
        text: Bluetooth.discovering ? "Stop looking" : "Look for devices"
        enabled: Bluetooth.defaultAdapter?.enabled ?? false
        onClicked: Bluetooth.discovering ? Bluetooth.stopDiscovery() : Bluetooth.startDiscovery()
    }
    Repeater {
        model: Bluetooth.devices
        Button {
            required property var modelData
            text: modelData.name + (modelData.connected ? " (connected)" : "")
            enabled: !modelData.busy
            // Windows shows its own consent or PIN UI for pair(). unpair() and cancelPairing() also exist.
            // connect() and disconnect() work for paired audio devices only.
            onClicked: !modelData.paired ? modelData.pair()
                : modelData.connected ? modelData.disconnect() : modelData.connect()
        }
    }
}
```

`ready` distinguishes the first/unavailable read from a completed enumeration. A completed read can have no adapter. `error` describes a failed read or discovery request; `operationFailed(message)` reports discovery failures. Failure to enumerate clears the models; a later successful read recovers. `defaultAdapter` and `adapters` represent the default adapter returned by Windows, rather than promising simultaneous support for multiple radios.

The adapter has a Windows device `id`, `name`, MAC `address`, and read-only `state`: `BluetoothAdapterState.Unknown`, `Off`, `On`, or `Disabled`. `enabled` is true only for On. Unknown means Windows did not expose radio state; Winshell does not guess that an enumerated adapter is enabled. Setting `enabled` uses the same radio request as Wi-Fi: `Radio.RequestAccessAsync`, then `Radio.SetStateAsync` for each Bluetooth radio that is not already in the requested state. The property stays what this adapter's own radio last reported. If that adapter has no radio, the request fails and no other Bluetooth radio is changed. A refusal sets the adapter's `error` and emits its `operationFailed`. Discovery failures stay on the `Bluetooth` singleton. Quickshell's BlueZ setter writes the local value first. Windows does not: the property changes only when a later read reports the new state. Setting the value it already has does nothing, and importing the module does not ask for radio control. The first real change can show Windows' consent UI. A packaged app declares the `radios` device capability as well as `bluetooth`.

The device models share object identity. `pairedDevices` contains paired devices, including absent ones; `nearbyDevices` contains present unpaired devices found during explicit discovery. Nearby membership is Windows' cached `System.Devices.Aep.IsPresent` report, not a guarantee that a device can be reached. `devices` contains both sets. `discovering` is read-only; `startDiscovery()` and `stopDiscovery()` own classic/LE DeviceWatchers. Starting requires an On adapter. Adapter removal/replacement or an Off/Disabled radio stops discovery. Stopping clears unpaired discovery results; paired devices remain.

A device exposes its Windows association-endpoint `id`, `adapter`, `name`, `icon`, `address`, `paired`, `present`, `canPair`, `connected`, `state`, `batteryAvailable` and `battery`. Endpoints sharing a Windows container (or an address when there is no container) are one device. Identity is retained while that grouping key remains stable, even if the preferred endpoint ID changes. Adapter replacement, removal, or a disappearing device destroys the old QML objects. `unpair()` explicitly unpairs all paired endpoints in the merged device, stopping and reporting the first failure.

`icon` is a device-type name in BlueZ's vocabulary, so a configuration ported from Quickshell can branch on the same string. It comes from the same association-endpoint enumeration as the rest of the device. Winshell does not open a Bluetooth device or a GATT handle to read it. The properties are `System.Devices.Aep.Bluetooth.Cod.Major`, `System.Devices.Aep.Bluetooth.Cod.Minor` and `System.Devices.Aep.Bluetooth.Le.Appearance`. Each one is a UInt16. When the raw appearance is missing, Winshell uses `System.Devices.Aep.Bluetooth.Le.Appearance.Category` and `.Subcategory`, which are also UInt16. A missing property leaves that part out. A property that is not a UInt16 does not fail the enumeration. An unreadable class stays empty and does not fall through to the appearance.

A classic class is preferred when a class and an appearance are both present. That includes a class BlueZ does not name: the icon stays empty and does not fall through to the appearance. An appearance is used only when Windows reports no classic class. A major and minor of zero are no class. An appearance of zero is an empty icon. The names are the ones BlueZ derives, such as `audio-headset`, `audio-headphones`, `audio-card`, `input-keyboard`, `input-mouse`, `input-gaming`, `input-tablet`, `phone`, `computer`, `printer`, `camera-photo`, `camera-video`, `video-display` and `network-wireless`. BlueZ also uses `modem`, `multimedia-player`, `scanner` and `unknown`. Winshell does not invent a name for a class BlueZ has none for.

`Shell.iconPath` and `IconImage` draw a name only when Winshell already has a glyph for it. `audio-headset` does. `camera-video`, `input-tablet`, `modem`, `scanner` and `unknown` do not. There is no icon theme to add. A configuration uses the string to pick its own icon:

```qml
import QtQuick
import Winshell
import Winshell.Bluetooth
import Winshell.Widgets

Repeater {
    model: Bluetooth.devices
    IconImage {
        required property var modelData
        implicitSize: 16
        source: modelData.icon === "camera-video" || modelData.icon === "input-tablet"
            || modelData.icon === "modem" || modelData.icon === "scanner" || modelData.icon === "unknown"
            ? Shell.iconPath("bluetooth")
            : Shell.iconPath(modelData.icon, true)
    }
}
```

Battery is a fraction from 0–1, read only from the cached Windows Bluetooth battery device property, and cached for up to 30 seconds. It is available only for connected devices when a container exposes a valid level. Unknown/missing values stay unavailable rather than looking like an empty battery. Winshell never opens a GATT connection to obtain battery data. Driver support varies, including on devices that expose battery data over GATT but not through Windows' device property.

Device actions expose read-only `busy`, `operation` (`None`, `Connecting`, `Disconnecting`, `Pairing`, `Unpairing`), `pairing` and `error`, plus `operationFailed(message)`. Overlapping actions on one device are ignored. Connection and pairing properties only change when Windows reports the new state; an accepted driver request does not guarantee that a device connected. Errors, cancellations and unsupported actions clear the pending operation. Destroying a device cancels its pairing operation. Reads begun before an action/discovery change are discarded so they cannot overwrite the newer state.

Pairing uses [basic Windows pairing](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/pair-devices), including system consent/PIN UI where supported by the process deployment. Winshell never automatically confirms a PIN match or logs PINs. Windows policy, app capabilities and deployment can reject pairing; the Windows result or exception is exposed through `error`. Packaged apps need the `bluetooth` device capability. No permission is requested on import.

Windows has no universal connect/disconnect call for every Bluetooth device profile. These methods retain support for paired audio devices through the documented Bluetooth audio driver [one-shot reconnect](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect) and disconnect requests, using device enumeration to find the container's audio filters. Other device types reconnect through Windows once paired; the methods report unsupported/failed requests instead of pretending to connect. They do not select a default audio device or change volume.

Native tests only read adapter and paired-device state. Discovery, pairing, unpairing, connecting, disconnecting, cancellation and radio changes are exercised with a fake backend. Those tests never call `Radio.SetStateAsync` or `Radio.RequestAccessAsync`.
