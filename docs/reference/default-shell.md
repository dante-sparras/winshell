# The default shell

Winshell runs the default shell when you have no configuration of your own ([getting started](../guide/getting-started.md#where-your-configuration-lives)). It is plain QML in `%LOCALAPPDATA%\winshell\default-shell`, built only from the modules in this reference, and every surface follows the choices made in [Winshell Settings](../settings.md) as they change.

| File | Surface |
| --- | --- |
| `shell.qml` | The root: a taskbar per screen, the hotkeys, Winshell Settings and the on-screen display |
| `Taskbar.qml` | The [taskbar](#taskbar) on one screen |
| `Launcher.qml`, `LauncherView.qml`, `launcher.js` | [Start](#start) |
| `QuickSettings.qml`, `QuickSettingsContent.qml`, `QuickSettingsTile.qml` | [Quick settings](#quick-settings) |
| `CalendarFlyout.qml`, `NotificationCenter.qml`, `CalendarView.qml` | The [notification center and calendar](#notification-center-and-calendar) |
| `OnScreenDisplayHost.qml`, `OnScreenDisplay.qml` | The [on-screen display](#on-screen-display) |
| `SettingsWindow.qml` and `Settings*.qml` | [Winshell Settings](#winshell-settings) |
| `Preferences.qml` | The singleton that keeps Winshell Settings' choices in `preferences.toml` |
| `TrayArea.qml`, `TrayIcon.qml`, `QuickSettingsButton.qml`, `TaskbarClock.qml` | The right side of the taskbar |

To change a surface, [copy the default shell](../guide/getting-started.md#make-the-default-shell-your-own) and edit your copy.

## Hotkeys

| Hotkey | What it does |
| --- | --- |
| `Win` | Opens or closes Start. Change it on Winshell Settings' Hotkeys page. |
| None until set | Opens Winshell Settings. Set it on the Hotkeys page. |
| `Win+T` | Puts the keyboard on the first app of the main taskbar. While Explorer runs, Windows keeps `Win+T` and the hotkey is `taken`. |
| `Win+S` | Opens Search on the main taskbar. While Explorer runs, Windows keeps `Win+S` and the hotkey is `taken`. |
| `Win+A` | Opens or closes quick settings on the main taskbar. While Explorer runs, Windows keeps `Win+A` and the hotkey is `taken`. |
| `Win+N` | Opens or closes the notification center on the main taskbar. While Explorer runs, Windows keeps `Win+N` and the hotkey is `taken`. |
| `Win+1` to `Win+9`, `Win+0` | Does what a click does on that app of the main taskbar. `Win+0` is the tenth. `Win+Shift` with the number opens a new window. While Explorer runs, Windows keeps these and the hotkey is `taken`. |

A hotkey another app or Windows already holds isn't taken from it; Winshell Settings shows that it is taken. See [hotkeys](../hotkeys.md).

## Taskbar

A Windows 11 taskbar along the bottom of each screen, or of the main screen only: Start, Search and the apps in the middle or on the left, then the hidden icons chevron, the tray icons, the network, volume and battery icons, and the clock. It reserves the bottom edge, so maximized windows stop above it, unless it hides itself. Explorer's taskbar stays, and the two stack, unless `hideExplorerTaskbar` is true. That key is false until `preferences.toml` sets it. Winshell Settings has no switch for it. Winshell puts Explorer's taskbar and its edge back when it exits.

[The taskbar](../taskbar.md) explains its settings (`centered`, `autoHide`, `appsOn`), pinned apps, previews, the keyboard and Narrator, and the hooks other surfaces plug into: `startRequested`, `searchRequested`, `trayArea` and `clockArea`. Pins are in `taskbar.json` in the shell's data folder.

Right-click an empty part of the taskbar for **Taskbar settings**. Search opens Start and focuses its search box. While a Winshell update is ready to install, an icon in the notification area opens the updates card in Winshell Settings; see [Winshell updates](../updates.md).

## Start

A popup window over the taskbar, opened by the Start button or the Windows key, on the material chosen in Winshell Settings. It has a search box, the pinned apps, and up to six recommended apps: the ones used most recently that aren't pinned. **All apps** lists every app. Typing searches apps first ([`Apps.search`](winshell-apps.md#apps)), then the Windows Settings pages this shell already opens, then recent documents ([`RecentDocuments.search`](winshell-apps.md#recentdocuments)). The box still says Search apps. While you are typing, Start says that web results aren't included. The arrow keys move through those results, Enter opens the selected app, settings page, or document, and Escape or the Windows key closes Start. Recommended stays apps.

Pins are in `launcher.toml` next to the shell's `shell.qml`: for the default shell, `%LOCALAPPDATA%\winshell\default-shell\launcher.toml`. Until you pin or unpin something, Start shows its default pins. Winshell Settings' Start page turns recommended apps off and moves or unpins pins.

## Quick settings

The popup window the network, volume and battery icons open, as on Windows 11. It has:

- **The media playing**, with play, pause, previous and next for that [media session](winshell-media.md).
- **Tiles**: Wi-Fi, Bluetooth, Airplane mode, Energy saver, Power mode, Night light and Accessibility. A tile shows the setting's state where Winshell can read it. Power mode opens a page that changes the [power mode](winshell-power.md#powermode). The other tiles open their page in Windows Settings, because Winshell doesn't change radios or Windows settings. A tile for hardware the PC doesn't have is hidden.
- **Sliders**: the volume of the default playback device, with a page to pick the output. **App volume** on that page sets the volume and mute of each audio session. The brightness slider is a monitor that reports one.
- **The battery**, on a PC that has one.

**Edit quick settings** edits the tiles: unpin, add back and reorder them, with the mouse or the keyboard. They are kept in `quick-settings.toml` in the shell's state folder.

## Notification center and calendar

The clock on the taskbar opens one popup window with the notifications above a month calendar. Its accessible name includes how many of those notifications are showing, and the time and date it draws stay as they are. Notifications are grouped by app. Each shows the time it came in, in the regional short time format the taskbar clock uses; one from another day has the abbreviated weekday in front, such as `Tue 14:05`. Dismiss, Clear and Clear all remove them from Windows. Winshell reads notifications only after you allow access with **Allow notification access**. Toast banners stay Windows' own.

[Windows notifications](../notifications.md#default-notification-center-and-calendar) explains access, grouping, the calendar's regional formats and the keyboard.

## On-screen display

When the volume, a monitor's brightness, the microphone mute or the media playing changes, a short panel appears above the taskbar, centered on the screen, as on Windows 11. Volume, brightness and mute stay for 3 seconds and media for 5; the pointer over it keeps it open. It only shows levels and never changes them, except that its media buttons control the session that is playing. It doesn't reserve space.

## Winshell Settings

A window laid out like Windows 11 Settings, for Winshell's own choices: light or dark, the accent color, the material, the taskbar, Start, quick settings, notifications, hotkeys, running at sign-in, and Winshell's own updates. Open it from the taskbar's right-click menu, its hotkey, or `winshell ipc call settings open <page>`.

[Winshell Settings](../settings.md) lists its pages and the keys of `preferences.toml`.

## Files

| File | Where | What |
| --- | --- | --- |
| `preferences.toml` | Next to `shell.qml` for a config in `%APPDATA%\winshell`; `%APPDATA%\winshell` itself for the default shell | Winshell Settings' choices |
| `launcher.toml` | Next to `shell.qml`: `%LOCALAPPDATA%\winshell\default-shell` for the default shell | Start pins |
| `taskbar.json` | The shell's data folder, `%LOCALAPPDATA%\winshell\data\by-shell\winshell-default` | Taskbar pins |
| `quick-settings.toml` | The shell's state folder, `%LOCALAPPDATA%\winshell\state\by-shell\winshell-default` | Quick settings tiles and their order |

The data and state folders follow the shell id, which the default shell sets with `//@ pragma ShellId winshell-default`, so a copy that keeps that line keeps `taskbar.json` and `quick-settings.toml`. `launcher.toml` doesn't follow the shell id: it is beside each copy's own `shell.qml`, and copying the folder copies it.
