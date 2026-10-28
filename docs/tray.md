# Windows notification area

`import Winshell.Tray`. `SystemTray.items` holds a `SystemTrayItem` for every tray icon that apps add with `Shell_NotifyIcon`, in the order they were added.

```qml
Repeater {
    model: SystemTray.items
    Image {
        required property var modelData
        visible: !modelData.overflow
        source: modelData.icon
        sourceSize: Qt.size(16, 16)
    }
}
```

## How the shell sees the icons

Apps send their icons to the first window of class `Shell_TrayWnd`. A shell that uses `SystemTray` makes one, keeps it in front of Explorer's taskbar, and passes every message on to Explorer. Explorer's own tray, app bars and toasts keep working. On start, the shell broadcasts `TaskbarCreated` so that running apps add their icons again.

- **Explorer restarts.** When a new Explorer taskbar broadcasts `TaskbarCreated`, the shell gets back in front and answers once with its own broadcast a second later, so every app re-registers through it. It does not answer its own broadcast, or another one while the same Explorer taskbar is up.
- **More than one instance.** One instance in the session owns the notification area. The others log that they are waiting, and their `items` stay empty. When the owner exits, it removes its window and broadcasts `TaskbarCreated`. When it crashes, Windows releases its owner name. Either way, a waiting instance takes over within half a second.
- **Elevation.** The window accepts `TaskbarCreated` and `WM_COPYDATA` from any integrity level, as Explorer's taskbar does.

## Items

| Member | Meaning |
| --- | --- |
| `id` | The program's file name without its extension |
| `appName` | The program's description, or its file name |
| `executable` | The program's full path |
| `icon` | An `image://trayicon/…` source for the icon the app set |
| `tooltip` | The tooltip text, which may run over several lines |
| `overflow` | Windows shows the icon in the hidden icons flyout, not in the taskbar |
| `menu` | A `MenuHandle` for `MenuAnchor`; the app draws the menu itself |
| `balloon(title, message, icon)` | The app asked for a balloon notification. `icon` is a `BalloonIcon`: `None`, `Info`, `Warning`, `Error`, or `App` for the app's own icon |

`overflow` follows Settings > Personalization > Taskbar > Other system tray icons, and changes as soon as the user flips a switch there. Windows keeps that choice per icon in `HKCU\Control Panel\NotifyIconSettings`. An icon Windows has not stored a choice for yet is in the overflow, as Windows 11 does with a new icon.

An icon the app hid with `NIS_HIDDEN` is not in `items`, because Explorer does not show it either. A balloon still reaches Explorer, which shows it as a toast. The signal lets a shell show it as well.

## Limits

- Icons that Explorer draws itself, such as volume, network and battery on Windows 11, are not `Shell_NotifyIcon` icons. Use `Winshell.Audio`, `Winshell.Network` and `Winshell.Power`.
- The order of `items` is the order apps added their icons. It is not the order the user arranged in the taskbar.

## Checks only a live session can do

Automated tests run hosts under private window classes and never broadcast to the session. These need a person at the PC:

1. Run a shell that lists `SystemTray.items`. Restart Explorer from Task Manager, and confirm the log shows "Explorer made a new taskbar" and that every icon is still listed.
2. Run two instances, then `winshell kill` the first. Within a second, the second lists the icons.
3. Turn an icon on in Settings > Other system tray icons, and confirm its `overflow` turns false.
