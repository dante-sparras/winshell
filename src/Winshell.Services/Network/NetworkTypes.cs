namespace Winshell.Network;

public enum NetworkConnectivity { Unknown, None, CaptivePortal, LocalAccess, InternetAccess }

public enum NetworkCost { Unknown, Unrestricted, Fixed, Variable }

public enum NetworkAccessStatus { Unspecified, Allowed, DeniedByUser, DeniedBySystem }

public enum WifiRadioState { Unknown, On, Off, Disabled }

public enum AirplaneModeState { Unknown, Off, On }

public enum ConnectionState { Unknown, Connecting, Connected, Disconnecting, Disconnected }

public enum ConnectionFailReason { Unknown, InvalidPassword, Timeout, NetworkUnavailable, UnsupportedAuthentication }

public enum DeviceType { None, Wifi, Wired }

public enum WifiSecurityType { Wpa3SuiteB192, Sae, Wpa2Eap, Wpa2Psk, WpaEap, WpaPsk, StaticWep, Owe, Open, Unknown }

/// <summary>The 802.11 mode of a <see cref="WifiDevice"/>. Windows never reports <see cref="Mesh"/> or <see cref="AdHoc"/>.</summary>
public enum WifiDeviceMode { Unknown, AccessPoint, Mesh, Station, AdHoc }
