# Winshell.Bluetooth

The default Windows Bluetooth adapter, paired devices, and devices found nearby. Reading the module never pairs, connects or changes a radio. Setting `BluetoothAdapter.enabled` asks Windows to change that adapter's radio. Setting `discoverable` or `pairable` asks Windows to change whether it can be found or accepts incoming connections. While monitoring is on, state is read again every three seconds. [Windows Bluetooth](../bluetooth.md) explains discovery, pairing, battery, the device icon, discoverable, pairable and what connect can do.

`import Winshell.Bluetooth`

## Bluetooth

The singleton for the whole shell. It holds the default adapter and the devices, and starts and stops discovery.

| Member | Type | Description |
| --- | --- | --- |
| `defaultAdapter` | [BluetoothAdapter](#bluetoothadapter), read-only | The default adapter Windows returns, or null when there is none. |
| `adapters` | ObjectModel of [BluetoothAdapter](#bluetoothadapter), read-only | The default adapter, or nothing. |
| `devices` | ObjectModel of [BluetoothDevice](#bluetoothdevice), read-only | The paired devices and the nearby ones. |
| `pairedDevices` | ObjectModel of [BluetoothDevice](#bluetoothdevice), read-only | The paired devices, including absent ones. |
| `nearbyDevices` | ObjectModel of [BluetoothDevice](#bluetoothdevice), read-only | The present unpaired devices found during discovery. |
| `ready` | bool, read-only | Whether a read has completed. A completed read can have no adapter. |
| `discovering` | bool, read-only | Whether discovery is running. |
| `error` | string, read-only | Why the last read or discovery request failed. Empty when it succeeded. |
| `refresh()` | method | Reads the state now, rather than at the next three-second read. |
| `startMonitoring()` | method | Starts the three-second read. The service starts it when it is created. |
| `stopMonitoring()` | method | Stops that read until `startMonitoring()`. |
| `startDiscovery()` | method | Starts looking for nearby devices. Needs an adapter that is `On`. |
| `stopDiscovery()` | method | Stops looking, and clears the unpaired devices it found. |
| `operationFailed(message)` | signal | Emitted when starting or stopping discovery fails. |

## BluetoothAdapter

The default Windows Bluetooth adapter, reached from `Bluetooth.defaultAdapter`. Setting `enabled` asks Windows to turn its radio on or off. Setting `discoverable` or `pairable` asks Windows to change discovery or incoming connections. [Windows Bluetooth](../bluetooth.md) is the longer account, including what Windows does when the process exits.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | The Windows device id. |
| `name` | string, read-only | The adapter's name. |
| `address` | string, read-only | The MAC address. |
| `state` | [BluetoothAdapterState](#bluetoothadapterstate), read-only | The radio's state. |
| `enabled` | bool | Whether `state` is `On`. Setting it asks Windows to turn this adapter's radio on or off. The value stays what Windows last reported until a later read shows the radio changed. |
| `discoverable` | bool | Whether Windows reports a local radio as discoverable. Setting it asks Windows to change that. The value stays what the last read reported until a later read shows a change. Turning it on also turns on incoming connections when `pairable` is false. |
| `pairable` | bool | Whether Windows reports a local radio as accepting incoming connections. On Windows this is wider than pairing: a device that already knows this PC can connect. Setting it asks Windows to change that. Turning it off also turns discovery off when `discoverable` is true. The value stays what the last read reported until a later read shows a change. |
| `error` | string, read-only | Why the last radio, discoverable or incoming-connection change failed. Empty when it succeeded, or when a later read reports a different `state`, `discoverable` or `pairable`. |
| `operationFailed(message)` | signal | Emitted when a radio, discoverable or incoming-connection change fails, with the same text as `error`. |

## BluetoothDevice

A paired or nearby device, reached from the `Bluetooth` device lists. Endpoints that Windows groups in one device container are one device, and the object stays the same while that grouping does.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | The Windows association-endpoint id. It can change while the object stays the same. |
| `adapter` | [BluetoothAdapter](#bluetoothadapter), read-only | The adapter the device was found on. |
| `address` | string, read-only | The Bluetooth address. |
| `name` | string, read-only | The device's name. |
| `icon` | string, read-only | A BlueZ device-type name, such as `audio-headset` or `input-mouse`. Empty when Windows reports no class, or a class BlueZ does not name. `Shell.iconPath` draws only names Winshell already knows. See [Windows Bluetooth](../bluetooth.md). |
| `paired` | bool, read-only | Whether the device is paired. |
| `present` | bool, read-only | Whether Windows' cached report says the device is in range. It does not guarantee the device can be reached. |
| `canPair` | bool, read-only | Whether the device is unpaired and Windows says it can be paired. |
| `connected` | bool, read-only | Whether the device is connected. |
| `state` | [BluetoothDeviceState](#bluetoothdevicestate), read-only | `Connected` or `Disconnected`, matching `connected`. |
| `batteryAvailable` | bool, read-only | Whether Windows reports a battery level for this connected device. |
| `battery` | real, read-only | The battery level, from 0 to 1. 0 when `batteryAvailable` is false. |
| `operation` | [BluetoothOperation](#bluetoothoperation), read-only | The action running on the device. |
| `busy` | bool, read-only | Whether `operation` is not `None`. Other actions are ignored until it ends. |
| `pairing` | bool, read-only | Whether `operation` is `Pairing`. |
| `error` | string, read-only | Why the last action failed. Empty when it succeeded. |
| `connect()` | method | Asks Windows to connect a paired audio device. Fails for other device types, which reconnect through Windows. |
| `disconnect()` | method | Asks Windows to disconnect a paired audio device. Fails for other device types. |
| `pair()` | method | Pairs the device with basic Windows pairing, which may show Windows consent or PIN UI. |
| `cancelPairing()` | method | Cancels a running `pair()`. Its `error` becomes `Cancelled`. |
| `unpair()` | method | Unpairs every paired endpoint of the device, stopping at the first failure. |
| `operationFailed(message)` | signal | Emitted when an action fails, with the same text as `error`. |

## BluetoothAdapterState

The values of `BluetoothAdapter.state`. `Unknown` means Windows did not report the radio's state.

Values: `Unknown`, `Off`, `On`, `Disabled`

## BluetoothDeviceState

The values of `BluetoothDevice.state`.

Values: `Disconnected`, `Connected`

## BluetoothOperation

The values of `BluetoothDevice.operation`.

Values: `None`, `Connecting`, `Disconnecting`, `Pairing`, `Unpairing`
