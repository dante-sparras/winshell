namespace Winshell.Network;

/// <summary>What a connection can reach.</summary>
public enum NetworkConnectivity
{
    /// <summary>Not read yet, or the last read failed.</summary>
    Unknown,

    /// <summary>No connection.</summary>
    None,

    /// <summary>Limited internet access, usually until someone signs in on a captive portal page.</summary>
    CaptivePortal,

    /// <summary>The local network only, without the internet.</summary>
    LocalAccess,

    /// <summary>The internet.</summary>
    InternetAccess
}

/// <summary>How a connection's data is charged, as Windows reports it.</summary>
public enum NetworkCost
{
    /// <summary>Windows does not know the cost.</summary>
    Unknown,

    /// <summary>Data is not limited.</summary>
    Unrestricted,

    /// <summary>Data is free up to a limit.</summary>
    Fixed,

    /// <summary>Data is charged by the byte.</summary>
    Variable
}

/// <summary>Whether Windows lets Winshell scan and connect Wi-Fi networks.</summary>
public enum NetworkAccessStatus
{
    /// <summary>Not asked yet, or the last request failed.</summary>
    Unspecified,

    /// <summary>Windows allows scanning and connecting.</summary>
    Allowed,

    /// <summary>The user denied access.</summary>
    DeniedByUser,

    /// <summary>Windows denied access, or refused a scan.</summary>
    DeniedBySystem
}

/// <summary>The state of the PC's Wi-Fi radios, taken together.</summary>
public enum WifiRadioState
{
    /// <summary>No Wi-Fi radio was found, or Windows would not list them.</summary>
    Unknown,

    /// <summary>At least one Wi-Fi radio is on.</summary>
    On,

    /// <summary>No Wi-Fi radio is on, and at least one can be turned on.</summary>
    Off,

    /// <summary>Every Wi-Fi radio is disabled, as by a hardware switch, so software can't turn one on.</summary>
    Disabled
}

/// <summary>Whether airplane mode is on.</summary>
public enum AirplaneModeState
{
    /// <summary>Windows has no airplane mode setting, or it could not be read.</summary>
    Unknown,

    /// <summary>Airplane mode is off.</summary>
    Off,

    /// <summary>Airplane mode is on.</summary>
    On
}

/// <summary>How far an adapter, or a network on it, is in connecting.</summary>
public enum ConnectionState
{
    /// <summary>Windows reported a state that is none of the others.</summary>
    Unknown,

    /// <summary>The adapter is finding, joining or signing in to the network.</summary>
    Connecting,

    /// <summary>The adapter is connected.</summary>
    Connected,

    /// <summary>The adapter is leaving the network.</summary>
    Disconnecting,

    /// <summary>The adapter is not connected.</summary>
    Disconnected
}

/// <summary>
/// Why connecting to a Wi-Fi network failed. <see cref="Network.Error"/> holds the value's name after a failed
/// <see cref="Network.Connect"/> or <see cref="WifiNetwork.ConnectWithPassword"/>.
/// </summary>
public enum ConnectionFailReason
{
    /// <summary>A failure that is none of the others.</summary>
    Unknown,

    /// <summary>The network refused the password or the saved credentials.</summary>
    InvalidPassword,

    /// <summary>The network did not answer in time.</summary>
    Timeout,

    /// <summary>The network is not in the last scan, or Windows has not given access to the adapter.</summary>
    NetworkUnavailable,

    /// <summary>The network signs in with a method Windows can't use here.</summary>
    UnsupportedAuthentication
}

/// <summary>The kind of a network adapter.</summary>
public enum DeviceType
{
    /// <summary>Neither Wi-Fi nor Ethernet. No listed device has this type.</summary>
    None,

    /// <summary>A Wi-Fi adapter.</summary>
    Wifi,

    /// <summary>An Ethernet adapter.</summary>
    Wired
}

/// <summary>How a Wi-Fi network or a saved profile is secured.</summary>
public enum WifiSecurityType
{
    /// <summary>WPA3-Enterprise in 192-bit mode.</summary>
    Wpa3SuiteB192,

    /// <summary>WPA3-Personal, which signs in with SAE.</summary>
    Sae,

    /// <summary>WPA2-Enterprise, or WPA3-Enterprise without 192-bit mode.</summary>
    Wpa2Eap,

    /// <summary>WPA2-Personal, with a password.</summary>
    Wpa2Psk,

    /// <summary>WPA-Enterprise.</summary>
    WpaEap,

    /// <summary>WPA-Personal, with a password.</summary>
    WpaPsk,

    /// <summary>WEP, with a fixed key.</summary>
    StaticWep,

    /// <summary>Enhanced Open: encrypted, with no password.</summary>
    Owe,

    /// <summary>Open: no password and no encryption.</summary>
    Open,

    /// <summary>Windows did not report the security, or it is none of the others.</summary>
    Unknown
}

/// <summary>The 802.11 mode of a <see cref="WifiDevice"/>. Windows never reports <see cref="Mesh"/> or <see cref="AdHoc"/>.</summary>
public enum WifiDeviceMode
{
    /// <summary>Windows reported no mode, or a mode that is none of the others.</summary>
    Unknown,

    /// <summary>The adapter hosts a network, as Mobile hotspot does.</summary>
    AccessPoint,

    /// <summary>An 802.11s mesh. Never reported.</summary>
    Mesh,

    /// <summary>The adapter joins networks as a client.</summary>
    Station,

    /// <summary>A peer-to-peer ad hoc network. Never reported.</summary>
    AdHoc
}
