# Windows Bluetooth

`Winshell.Bluetooth` exposes the `Bluetooth` singleton. Reading it enumerates the default Windows Bluetooth adapter and paired classic/LE association endpoints. It never requests radio-control access, pairs, connects, opens a remote device/GATT handle, or changes a radio. The service starts monitoring when it is created, and while monitoring is on, state refreshes every three seconds. `stopMonitoring()` stops that read and `startMonitoring()` starts it again. `refresh()` requests an earlier read. The default shell stops monitoring until Quick settings is open. Each shell generation owns its backend and stops its discovery watchers when destroyed.

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

The adapter has a Windows device `id`, `name`, MAC `address`, and read-only `state`: `BluetoothAdapterState.Unknown`, `Off`, `On`, or `Disabled`. `enabled` is true only for On. Unknown means Windows did not expose radio state; Winshell does not guess that an enumerated adapter is enabled. Use Windows Settings to control radios.

The device models share object identity. `pairedDevices` contains paired devices, including absent ones; `nearbyDevices` contains present unpaired devices found during explicit discovery. Nearby membership is Windows' cached `System.Devices.Aep.IsPresent` report, not a guarantee that a device can be reached. `devices` contains both sets. `discovering` is read-only; `startDiscovery()` and `stopDiscovery()` own classic/LE DeviceWatchers. Starting requires an On adapter. Adapter removal/replacement or an Off/Disabled radio stops discovery. Stopping clears unpaired discovery results; paired devices remain.

A device exposes its Windows association-endpoint `id`, `adapter`, `name`, `address`, `paired`, `present`, `canPair`, `connected`, `state`, `batteryAvailable` and `battery`. Endpoints sharing a Windows container (or an address when there is no container) are one device. Identity is retained while that grouping key remains stable, even if the preferred endpoint ID changes. Adapter replacement, removal, or a disappearing device destroys the old QML objects. `unpair()` explicitly unpairs all paired endpoints in the merged device, stopping and reporting the first failure.

Battery is a fraction from 0–1, read only from the cached Windows Bluetooth battery device property, and cached for up to 30 seconds. It is available only for connected devices when a container exposes a valid level. Unknown/missing values stay unavailable rather than looking like an empty battery. Winshell never opens a GATT connection to obtain battery data. Driver support varies, including on devices that expose battery data over GATT but not through Windows' device property.

Device actions expose read-only `busy`, `operation` (`None`, `Connecting`, `Disconnecting`, `Pairing`, `Unpairing`), `pairing` and `error`, plus `operationFailed(message)`. Overlapping actions on one device are ignored. Connection and pairing properties only change when Windows reports the new state; an accepted driver request does not guarantee that a device connected. Errors, cancellations and unsupported actions clear the pending operation. Destroying a device cancels its pairing operation. Reads begun before an action/discovery change are discarded so they cannot overwrite the newer state.

Pairing uses [basic Windows pairing](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/pair-devices), including system consent/PIN UI where supported by the process deployment. Winshell never automatically confirms a PIN match or logs PINs. Windows policy, app capabilities and deployment can reject pairing; the Windows result or exception is exposed through `error`. Packaged apps need the `bluetooth` device capability. No permission is requested on import.

Windows has no universal connect/disconnect call for every Bluetooth device profile. These methods retain support for paired audio devices through the documented Bluetooth audio driver [one-shot reconnect](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect) and disconnect requests, using device enumeration to find the container's audio filters. Other device types reconnect through Windows once paired; the methods report unsupported/failed requests instead of pretending to connect. They do not select a default audio device or change volume.

Native tests only read adapter and paired-device state. Discovery, pairing, unpairing, connecting, disconnecting and cancellation are exercised with a fake backend, without invoking those native actions on the development PC.
