namespace Winshell.Bluetooth;

public enum BluetoothAdapterState { Unknown, Off, On, Disabled }

public enum BluetoothDeviceState { Disconnected, Connected }

public enum BluetoothOperation { None, Connecting, Disconnecting, Pairing, Unpairing }
