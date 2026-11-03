# Winshell.Bluetooth

The Bluetooth adapter, paired and nearby devices, pairing and connecting. Reach them from `Shell.Current.Bluetooth`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## BluetoothAdapter

The default Windows Bluetooth adapter. Setting Enabled asks Windows to change its radio. Setting Discoverable or Pairable asks Windows to change discovery or incoming connections.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Address` | string, read-only | The adapter's Bluetooth address, such as `00:1A:7D:DA:71:13`. |
| `Discoverable` | bool | Whether other devices can find this PC. Setting it to true also turns on Pairable. The value changes when a later read sees Windows did. |
| `Enabled` | bool | Whether the radio is on: State is BluetoothAdapterState.On. Setting it asks Windows to turn the radio on or off; the value changes when a later read sees the radio did. |
| `Error` | string, read-only | Why the last radio, discoverable or incoming-connection change failed. Empty after one succeeds, and when the adapter's state changes. |
| `Id` | string, read-only | The adapter's Windows device id. |
| `Name` | string, read-only | The adapter's name as Windows gives it, or `Bluetooth` when it gives none. |
| `Pairable` | bool | Whether Windows accepts incoming connections. Setting it to false also turns off Discoverable. The value changes when a later read sees Windows did. |
| `State` | [BluetoothAdapterState](#bluetoothadapterstate), read-only | The state of the adapter's radio. |
| `OperationFailed` | Action of string | A radio, discoverable or incoming-connection change failed, with the same text as Error. |

## BluetoothAdapterState

The state of a BluetoothAdapter's radio.

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 | Windows does not say, or the radio could not be read. |
| `Off` | 1 | The radio is off, and can be turned on. |
| `On` | 2 | The radio is on. |
| `Disabled` | 3 | The radio is off, and a hardware switch or the device's firmware keeps it off. |

## BluetoothDevice

A paired or nearby device. Endpoints that Windows groups in one device container are one device.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Adapter` | [BluetoothAdapter](#bluetoothadapter), read-only | The adapter that found the device. |
| `Address` | string, read-only | The device's Bluetooth address, in capital letters. Empty when Windows gives none. |
| `Battery` | double, read-only | The battery level, from 0 to 1. 0 when BatteryAvailable is false. |
| `BatteryAvailable` | bool, read-only | Whether Windows reports the device's battery level. Only a connected device can have one. |
| `Busy` | bool, read-only | An action is running. Other actions are ignored until it ends. |
| `CanPair` | bool, read-only | Whether the device is not paired and Windows says it can be, so Pair can work. |
| `Connected` | bool, read-only | Whether the device is connected. |
| `Error` | string, read-only | Why the last action failed, or `Cancelled` after CancelPairing. Empty while an action runs and after one succeeds. |
| `Icon` | string, read-only | A device-type name, such as `audio-headset` or `input-keyboard`. Empty when Windows reports no class, or a class that has no name. |
| `Id` | string, read-only | The Windows id of the device's endpoint, a paired one when there is one. |
| `Name` | string, read-only | The device's name, or its Address when Windows gives no name. |
| `Operation` | [BluetoothOperation](#bluetoothoperation), read-only | The action running on the device, or BluetoothOperation.None. |
| `Paired` | bool, read-only | Whether the device is paired with this PC. |
| `Pairing` | bool, read-only | Whether Pair is running. CancelPairing stops it. |
| `Present` | bool, read-only | Whether Windows sees the device now. A paired device that is off or out of range is not. |
| `State` | [BluetoothDeviceState](#bluetoothdevicestate), read-only | Connected, as a state. |
| `OperationFailed` | Action of string | An action failed, with the same text as Error. |
| `CancelPairing()` | void | Cancels a running Pair. Its Error becomes `Cancelled`. |
| `Connect()` | void | Asks Windows to connect a paired audio device. Fails for other device types, which reconnect through Windows. |
| `Disconnect()` | void | Asks Windows to disconnect a paired audio device. Fails for other device types. |
| `Pair()` | void | Pairs the device with basic Windows pairing, which may show consent or PIN UI. |
| `Unpair()` | void | Unpairs every paired endpoint of the device, stopping at the first failure. |

## BluetoothDeviceState

Whether a BluetoothDevice is connected.

| Value | Number | Description |
| --- | --- | --- |
| `Disconnected` | 0 | The device is not connected. |
| `Connected` | 1 | The device is connected. |

## BluetoothOperation

The action running on a BluetoothDevice.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 | No action is running. |
| `Connecting` | 1 | BluetoothDevice.Connect is running. |
| `Disconnecting` | 2 | BluetoothDevice.Disconnect is running. |
| `Pairing` | 3 | BluetoothDevice.Pair is running. |
| `Unpairing` | 4 | BluetoothDevice.Unpair is running. |

## BluetoothService

The default adapter and the paired and nearby devices. Reading it never pairs, connects or changes a radio. While monitoring is on, state is read again every three seconds.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Adapters` | ObservableCollection of [BluetoothAdapter](#bluetoothadapter), read-only | Only the DefaultAdapter, or empty when there is none. |
| `DefaultAdapter` | [BluetoothAdapter](#bluetoothadapter)?, read-only | The default adapter Windows returns, or null when there is none. |
| `Devices` | ObservableCollection of [BluetoothDevice](#bluetoothdevice), read-only | The paired devices and the nearby ones found during discovery: connected first, then paired, then by name. Empty when there is no adapter. |
| `Discovering` | bool, read-only | Whether Windows is looking for nearby devices, after StartDiscovery. |
| `Error` | string, read-only | Why the last read, or starting or stopping discovery, failed. Empty when it succeeded. |
| `NearbyDevices` | ObservableCollection of [BluetoothDevice](#bluetoothdevice), read-only | The present unpaired devices found during discovery. |
| `PairedDevices` | ObservableCollection of [BluetoothDevice](#bluetoothdevice), read-only | The paired devices, in the order of Devices. |
| `Ready` | bool, read-only | A read has completed. A completed read can have no adapter. |
| `OperationFailed` | Action of string | Starting or stopping discovery failed. |
| `Dispose()` | void | Stops monitoring and discovery, and cancels a running pairing. |
| `Refresh()` | void | Reads the state now, rather than at the next three-second read. |
| `StartDiscovery()` | void | Starts looking for nearby devices. Needs an adapter that is on. |
| `StartMonitoring()` | void | Reads adapter and device state until StopMonitoring. Nothing is read before this. |
| `StopDiscovery()` | void | Stops looking, and clears the unpaired devices it found. |
| `StopMonitoring()` | void | Stops the three-second read. A later StartMonitoring starts it again. |
