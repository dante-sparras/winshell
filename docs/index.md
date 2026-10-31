# Winshell

Winshell is a desktop shell toolkit for Windows 11. A configuration written in QML describes the shell: its panels, popups and windows, and what they show. Winshell draws them with Windows 11's materials and controls, and reads Windows itself for apps, windows, audio, media, networks, Bluetooth, power, notifications and the notification area.

Winshell ships with a default shell that looks like Windows 11: a taskbar on every screen, Start, quick settings, a notification center with a calendar, an on-screen display for volume and brightness, and Winshell Settings. It runs next to Explorer. It doesn't hide or replace Explorer's taskbar unless `hideExplorerTaskbar` is set in `preferences.toml`, and that key is off until then.

Until 1.0 is tagged, a release can still break a configuration. The [changelog](https://github.com/dante-sparras/winshell/blob/main/CHANGELOG.md) lists every change. The [release page](https://github.com/dante-sparras/winshell/releases) has the current build.

## Where to go next

- **[Getting started](guide/getting-started.md)**: install Winshell, run the default shell, and find your configuration.
- **[Configuration guide](guide/configuration.md)**: write a shell of your own, from one panel up.
- **[Troubleshooting](guide/troubleshooting.md)**: when something doesn't start, show or work.
- **[Migrating from Quickshell](migrating-from-quickshell.md)**: Quickshell's modules and types, and their Winshell names.
- **[The taskbar](taskbar.md)** and **[Winshell Settings](settings.md)**: the default shell.
- **[Reference](reference/index.md)**: every module, type and member a configuration can use.
- **Windows services**: [notifications](notifications.md), [networking](network.md), [Bluetooth](bluetooth.md), [capture](capture.md), [hotkeys](hotkeys.md) and the [notification area](tray.md).
- **[Decisions](adr/0001-own-qml-parser-and-jint-runtime.md)**: why Winshell is built the way it is.
- **[Development](development.md)**: build Winshell from source and run its checks.

Found a bug, or have an idea? [Open an issue](https://github.com/dante-sparras/winshell/issues/new/choose).
