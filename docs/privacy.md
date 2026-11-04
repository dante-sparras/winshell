# Privacy

Winshell collects no personal data and sends none to its developers or to anyone else. It has no telemetry, analytics or advertising.

What it reads stays on your PC:

- **Notifications.** The notification center reads the notifications Windows shows, once you allow it in Windows.
- **Wi-Fi, Bluetooth and location.** Quick settings lists networks and devices. Windows asks for location access before it lets an app list Wi-Fi networks; Winshell uses that permission for nothing else. The default shell does not turn radios on or off. A shell you write can, and can list saved Wi-Fi profiles (never their passwords) or change them.
- **Apps and windows.** The taskbar and Start read your installed apps, running windows and recently used apps from Windows.
- **Files Winshell writes.** Settings, pins, logs and crash reports stay in `%APPDATA%\winshell` and `%LOCALAPPDATA%\winshell`. A crash report holds the error and the last 40 log lines, with the passwords, tokens, keys and URL queries Winshell recognizes taken out. It is never uploaded.

The one connection Winshell makes on its own is the update check of a portable build, the zip from a GitHub release. It asks GitHub for the latest release and downloads it, so GitHub sees your IP address, as described in the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). It runs 30 seconds after the shell starts and then daily. Turn it off with **Get Winshell updates automatically** in Winshell Settings > About; **Check for updates** there still asks GitHub when you press it. A winget install, the Microsoft Store package and a build from source make no such connection.

A shell you write is a full-trust program. It can do more than this, such as make network requests, and building it runs its code, and `dotnet build` restores packages from NuGet. What it does is up to its author.

Questions: [open an issue](https://github.com/enpaze/winshell/issues).
