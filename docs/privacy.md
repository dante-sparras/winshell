# Privacy

Winshell collects no personal data and sends none to its developers or to anyone else. It has no telemetry, analytics or advertising.

What it reads stays on your PC:

- **Notifications.** The notification center reads the notifications Windows shows, once you allow it in Windows. The sending app's logo is a temporary file while one of that app's notifications is showing, and it is deleted when they are gone.
- **Wi-Fi, Bluetooth and location.** Quick settings lists networks and devices. Windows asks for location access before it lets an app list Wi-Fi networks; Winshell uses that permission for nothing else. The default shell does not turn radios on or off, and it does not change whether the PC can be found or accepts incoming Bluetooth connections. A configuration that sets `Networking.wifiEnabled` or `BluetoothAdapter.enabled` can ask Windows for radio control the first time, and Windows may show its own consent. A configuration that sets `BluetoothAdapter.discoverable` or `BluetoothAdapter.pairable` asks Windows to change discovery or incoming connections. Starting Winshell does not ask.
- **Saved Wi-Fi profiles.** A configuration can list the saved profiles for a network in range: the name, the SSID, the security type, whether a password is stored, and whether Windows connects automatically. Winshell does not read the password. It changes a profile only when that configuration sets `autoConnect` or calls `clearPassword()` or `forget()`. The default shell's quick settings do not list or change saved profiles.
- **Apps and windows.** The taskbar and Start read your installed apps, running windows and recently used apps from Windows.
- **Files Winshell writes.** Settings, pins, logs and crash reports stay in `%APPDATA%\winshell` and `%LOCALAPPDATA%\winshell`. Crash reports leave out passwords, tokens and keys that Winshell recognizes, and are never uploaded.

The one connection Winshell makes on its own is the update check of a portable Winshell, the zip from a GitHub release. winget, once the package is accepted, installs that same build and updates it. Winshell does not also check for updates for a winget install, so that copy makes no such connection. The check asks GitHub for the latest release and downloads it, so GitHub sees your IP address, as described in the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). Turn it off in Winshell Settings > About. The Microsoft Store package is not published yet. Once it is, it makes no such connection: the Store updates it.

A shell you write yourself in QML can do more than this, such as make network requests. What it does is up to its author.

Questions: [open an issue](https://github.com/dante-sparras/winshell/issues).
