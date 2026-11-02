# Winshell.Bluetooth

The Bluetooth adapter, paired and nearby devices, pairing and connecting. Reach them from `Shell.Current.Bluetooth`.

## BluetoothAdapter

The default Windows Bluetooth adapter. Setting Enabled asks Windows to change its radio. Setting Discoverable or Pairable asks Windows to change discovery or incoming connections.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Address` | string, read-only |  |
| `Discoverable` | bool |  |
| `Enabled` | bool |  |
| `Error` | string, read-only |  |
| `Id` | string, read-only |  |
| `Name` | string, read-only |  |
| `Pairable` | bool |  |
| `State` | [BluetoothAdapterState](#bluetoothadapterstate), read-only |  |
| `OperationFailed` | Action of string |  |

## BluetoothAdapterState

| Value | Number | Description |
| --- | --- | --- |
| `Unknown` | 0 |  |
| `Off` | 1 |  |
| `On` | 2 |  |
| `Disabled` | 3 |  |

## BluetoothDevice

A paired or nearby device. Endpoints that Windows groups in one device container are one device.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Adapter` | [BluetoothAdapter](#bluetoothadapter), read-only |  |
| `Address` | string, read-only |  |
| `Battery` | double, read-only | The battery level, from 0 to 1. 0 when BatteryAvailable is false. |
| `BatteryAvailable` | bool, read-only |  |
| `Busy` | bool, read-only | An action is running. Other actions are ignored until it ends. |
| `CanPair` | bool, read-only |  |
| `Connected` | bool, read-only |  |
| `Error` | string, read-only |  |
| `Icon` | string, read-only | A BlueZ device-type icon name. Empty when Windows reports no class, or a class BlueZ does not name. |
| `Id` | string, read-only |  |
| `Name` | string, read-only |  |
| `Operation` | [BluetoothOperation](#bluetoothoperation), read-only |  |
| `Paired` | bool, read-only |  |
| `Pairing` | bool, read-only |  |
| `Present` | bool, read-only |  |
| `State` | [BluetoothDeviceState](#bluetoothdevicestate), read-only |  |
| `OperationFailed` | Action of string |  |
| `CancelPairing()` | void | Cancels a running Pair. Its Error becomes `Cancelled`. |
| `Connect()` | void | Asks Windows to connect a paired audio device. Fails for other device types, which reconnect through Windows. |
| `Disconnect()` | void |  |
| `Pair()` | void | Pairs the device with basic Windows pairing, which may show consent or PIN UI. |
| `Unpair()` | void | Unpairs every paired endpoint of the device, stopping at the first failure. |

## BluetoothDeviceState

| Value | Number | Description |
| --- | --- | --- |
| `Disconnected` | 0 |  |
| `Connected` | 1 |  |

## BluetoothOperation

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 |  |
| `Connecting` | 1 |  |
| `Disconnecting` | 2 |  |
| `Pairing` | 3 |  |
| `Unpairing` | 4 |  |

## BluetoothService

The default adapter and the paired and nearby devices. Reading it never pairs, connects or changes a radio. While monitoring is on, state is read again every three seconds.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Adapters` | ObservableCollection of [BluetoothAdapter](#bluetoothadapter), read-only |  |
| `DefaultAdapter` | [BluetoothAdapter](#bluetoothadapter)?, read-only | The default adapter Windows returns, or null when there is none. |
| `Devices` | ObservableCollection of [BluetoothDevice](#bluetoothdevice), read-only |  |
| `Discovering` | bool, read-only |  |
| `Error` | string, read-only |  |
| `NearbyDevices` | ObservableCollection of [BluetoothDevice](#bluetoothdevice), read-only | The present unpaired devices found during discovery. |
| `PairedDevices` | ObservableCollection of [BluetoothDevice](#bluetoothdevice), read-only |  |
| `Ready` | bool, read-only | A read has completed. A completed read can have no adapter. |
| `OperationFailed` | Action of string |  |
| `Dispose()` | void |  |
| `Refresh()` | void | Reads the state now, rather than at the next three-second read. |
| `StartDiscovery()` | void | Starts looking for nearby devices. Needs an adapter that is on. |
| `StartMonitoring()` | void | Reads adapter and device state until StopMonitoring. The service starts this when it is created. |
| `StopDiscovery()` | void | Stops looking, and clears the unpaired devices it found. |
| `StopMonitoring()` | void | Stops the three-second read. A later StartMonitoring starts it again. |

