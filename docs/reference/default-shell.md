# The default shell

Winshell runs the default shell when you have no project of your own ([getting started](../guide/getting-started.md#where-a-configuration-lives)). It is built in, shell id `winshell-default`, and every surface follows the choices made in [Winshell Settings](../settings.md) as they change. `dotnet new winshell-default -n MyShell` is that shell as a project you can change. Winshell does not reference Reactor. The default shell is XAML and C#.

## Hotkeys

| Hotkey | What it does |
| --- | --- |
| `Win` | Opens or closes Start. Change it on Winshell Settings' Hotkeys page. |
| None until set | Opens Winshell Settings. Set it on the Hotkeys page. |
| `Win+T` | Puts the keyboard on the first app of the main taskbar. While Explorer runs, Windows keeps `Win+T` and the hotkey is `Taken`. |
| `Win+S` | Opens Search on the main taskbar. While Explorer runs, Windows keeps `Win+S` and the hotkey is `Taken`. |
| `Win+A` | Opens or closes quick settings on the main taskbar. While Explorer runs, Windows keeps `Win+A` and the hotkey is `Taken`. |
| `Win+N` | Opens or closes the notification center on the main taskbar. While Explorer runs, Windows keeps `Win+N` and the hotkey is `Taken`. |
| `Win+1` to `Win+9`, `Win+0` | Does what a click does on that app of the main taskbar. `Win+0` is the tenth. `Win+Shift` with the number opens a new window. While Explorer runs, Windows keeps these and the hotkey is `Taken`. |
| `Alt+Tab`, `Alt+Shift+Tab` | Off until `desktopChords` is true. Opens the window switcher and moves through it. While Explorer runs, Windows may still keep the chord and it is `Taken`. |
| `Win+Tab` | Off until `desktopChords` is true. Opens or closes the same switcher. While Explorer runs, Windows may still keep the chord and it is `Taken`. |
| `Win+D` | Off until `desktopChords` is true. Shows the desktop, then restores those windows. While Explorer runs, Windows may still keep the chord and it is `Taken`. |
| `Win+X` | Off until `desktopChords` is true. Opens the power-user menu. While Explorer runs, Windows may still keep the chord and it is `Taken`. |

`desktopChords` is on the Hotkeys page. `Win+S` is registered on its own, whether or not `desktopChords` is on. `Win+Alt+number` stays with Windows. A hotkey another app or Windows already holds isn't taken from it; Winshell Settings shows that it is taken. See [hotkeys](../hotkeys.md).

## Window switcher

With `desktopChords` on, Alt+Tab opens a popup over the main taskbar. It lists the window list in the order windows opened, not by most recent use, and each card is a `WindowThumbnail`. Enter, Space or a click activates that window and closes the popup. Releasing Alt does not. Win+Tab opens the same popup on the window in front, or closes it. When virtual desktops are supported, a row of desktops sits under the cards and a click switches to that desktop. Win+D minimizes every window that is not already minimized and remembers them; the next press restores those. Nothing is drawn on the taskbar for it. Win+X opens a menu from the top left of the main taskbar: Installed apps, System, Network, Settings, File Explorer, Search, Desktop and Taskbar settings. It does not shut the PC down or open Task Manager. Widgets are not part of the default shell.

## Taskbar

A Windows 11 taskbar along the bottom of each screen, or of the main screen only: Start, Search and the apps in the middle or on the left, then the hidden icons chevron, the tray icons, the network, volume and battery icons, and the clock. It reserves the bottom edge, so maximized windows stop above it, unless it hides itself. Explorer's taskbar stays, and the two stack, unless `hideExplorerTaskbar` is true. That key is false until `preferences.toml` sets it. Winshell Settings has no switch for it. Winshell puts Explorer's taskbar and its edge back when it exits.

[The taskbar](../taskbar.md) explains its settings (`taskbarCentered`, `taskbarAutoHide`, `taskbarAppsOn`), pinned apps, previews, the keyboard and Narrator. Pins are in `taskbar.json` in the shell's data folder.

Right-click an empty part of the taskbar for **Taskbar settings**. Search opens Start and focuses its search box. While a Winshell update is ready to install, an icon in the notification area opens the updates card in Winshell Settings; see [Winshell updates](../updates.md).

A click or a right-click on a tray icon reaches its app, which draws its own menu. Winland's icon is the exception: when the Winland that added it is the one answering on its pipe, the taskbar draws Winland's menu itself, with Winland's rows and access keys: **Reload**, **Edit config**, **Open log** and **Quit**. Reload and Quit go through the pipe ([`WinlandService.Reload`](winshell-winland.md#winlandservice) and `Dispatch("exit")`). Edit config and Open log open the files Winland reports with its `files` request, [`ConfigPath`](winshell-winland.md#winlandservice) and `LogPath`, so a Winland started with `--config` opens its own config. Open log is there only while Winland writes a log and the file exists. They open in the program Windows opens that file's type with, or in Notepad when the type has none, as Winland does. The icon is Winland's when its process is the one serving the pipe, so a second Winland on another pipe keeps its own menu. While no Winland answers, or one too old to answer `files` does, a click on its icon goes to Winland, which draws its Win32 menu.

## Start

A popup window over the taskbar, opened by the Start button or the Windows key, on the material chosen in Winshell Settings. It has a search box, the pinned apps, and up to six recommended apps: the ones used most recently that aren't pinned. **All apps** lists every app. Typing searches apps first ([`AppsService.Search`](winshell-apps.md#appsservice)), then the Windows Settings pages this shell already opens, then recent documents ([`RecentDocuments.Search`](winshell-apps.md#recentdocuments)). The box still says Search apps. While you are typing, Start says that web results aren't included. The arrow keys move through those results, Enter opens the selected app, settings page, or document, and Escape or the Windows key closes Start. Recommended stays apps.

Pins are in `launcher.toml` in the config folder (`Shell.ConfigRoot`). Until you pin or unpin something, Start shows its default pins. Winshell Settings' Start page turns recommended apps off and moves or unpins pins.

## Quick settings

The popup window the network, volume and battery icons open, as on Windows 11. It has:

- **The media playing**, with play, pause, previous and next for that [media session](winshell-media.md).
- **Tiles**: Wi-Fi, Bluetooth, Airplane mode, Energy saver, Power mode, Night light and Accessibility. A tile shows the setting's state where Winshell can read it. Power mode opens a page that changes the [power mode](winshell-power.md#powermodeservice). The other tiles open their page in Windows Settings. A configuration can set `NetworkingService.WifiEnabled` and `BluetoothAdapter.Enabled`. These tiles do not, and they do not change the other Windows settings. A tile for hardware the PC doesn't have is hidden.
- **Sliders**: the volume of the default playback device, with a page to pick the output. **App volume** on that page sets the volume and mute of each audio session. The brightness slider is a monitor that reports one.
- **The battery**, on a PC that has one.

**Edit quick settings** edits the tiles: unpin, add back and reorder them, with the mouse or the keyboard. They are kept in `quick-settings.toml` in the shell's state folder.

## Notification center and calendar

The clock on the taskbar opens one popup window with the notifications above a month calendar. Its accessible name includes how many of those notifications are showing, and the time and date it draws stay as they are. Notifications are grouped by app. Each shows the time it came in, in the regional short time format the taskbar clock uses; one from another day has the abbreviated weekday in front, such as `Tue 14:05`. Dismiss, Clear and Clear all remove them from Windows. Winshell reads notifications only after you allow access with **Allow notification access**. Toast banners stay Windows' own.

[Windows notifications](../notifications.md#the-default-shell) explains access, grouping and the calendar's regional formats.

## On-screen display

When the volume, a monitor's brightness, the microphone mute or the media playing changes, a short panel appears above the taskbar, centered on the screen, as on Windows 11. Volume, brightness and mute stay for 3 seconds and media for 5; the pointer over it keeps it open. It only shows levels and never changes them, except that its media buttons control the session that is playing. It doesn't reserve space.

## Winshell Settings

A window laid out like Windows 11 Settings, for Winshell's own choices: light or dark, the accent color, the material, the taskbar, Start, quick settings, notifications, hotkeys, running at sign-in, and Winshell's own updates. Open it from the taskbar's right-click menu, its hotkey, or `winshell ipc call settings open <page>`.

[Winshell Settings](../settings.md) lists its pages and the keys of `preferences.toml`.

## Files

| File | Where | What |
| --- | --- | --- |
| `preferences.toml` | The config folder, `Shell.ConfigRoot` | Winshell Settings' choices |
| `launcher.toml` | The config folder, `Shell.ConfigRoot` | Start pins |
| `taskbar.json` | The shell's data folder, `Shell.DataPath("taskbar.json")` | Taskbar pins |
| `quick-settings.toml` | The shell's state folder, `Shell.StatePath("quick-settings.toml")` | Quick settings tiles and their order |

The data and state folders follow the shell id. The built-in shell's id is `winshell-default`, so those two files are under `by-shell\winshell-default`. `preferences.toml` and `launcher.toml` are in the config folder, which is shared by that id only in the sense that the default shell reads them from `ConfigRoot`. A project you create with `dotnet new winshell-default` has its own copy of the shell and its own shell id once you set one.
