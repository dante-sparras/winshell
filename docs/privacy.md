# Privacy

Winshell collects no personal data and sends none to its developers or to anyone else. It has no telemetry, analytics or advertising.

What it reads stays on your PC:

- **Notifications.** The notification center reads the notifications Windows shows, once you allow it in Windows. It keeps nothing after they are gone.
- **Wi-Fi, Bluetooth and location.** Quick settings lists networks and devices. Windows asks for location access before it lets an app list Wi-Fi networks; Winshell uses that permission for nothing else.
- **Apps and windows.** The taskbar and Start read your installed apps, running windows and recently used apps from Windows.
- **Files Winshell writes.** Settings, pins, logs and crash reports stay in `%APPDATA%\winshell` and `%LOCALAPPDATA%\winshell`. Crash reports leave out passwords, tokens and keys that Winshell recognizes, and are never uploaded.

The one connection Winshell makes on its own is the update check of a portable Winshell, downloaded from GitHub Releases or installed with winget. It asks GitHub for the latest release and downloads it, so GitHub sees your IP address, as described in the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). Turn it off in Winshell Settings > About. The Microsoft Store version makes no such connection: the Store updates it.

A shell you write yourself in QML can do more than this, such as make network requests. What it does is up to its author.

Questions: [open an issue](https://github.com/dante-sparras/winshell/issues).
