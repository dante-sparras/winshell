using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>Discovery and incoming connections of every local Bluetooth radio. Each call can block.</summary>
internal static class BluetoothRadios
{
    /// <summary>Whether a radio is discoverable.</summary>
    internal static bool IsDiscoverable() => PInvoke.BluetoothIsDiscoverable(HANDLE.Null);

    /// <summary>Whether a radio accepts incoming connections.</summary>
    internal static bool IsConnectable() => PInvoke.BluetoothIsConnectable(HANDLE.Null);

    /// <summary>Whether Windows changed discovery on a radio.</summary>
    internal static bool EnableDiscovery(bool enabled) => PInvoke.BluetoothEnableDiscovery(HANDLE.Null, enabled);

    /// <summary>Whether Windows changed incoming connections on a radio.</summary>
    internal static bool EnableIncomingConnections(bool enabled) => PInvoke.BluetoothEnableIncomingConnections(HANDLE.Null, enabled);
}
