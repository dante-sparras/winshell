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

Foreground and desktop window-order events keep the host in front. Every thirty seconds it also checks the order and removes icons whose windows have gone. A waiting instance still checks the owner every half second.

- **Explorer restarts.** When a new Explorer taskbar broadcasts `TaskbarCreated`, the shell gets back in front and answers once with its own broadcast a second later, so every app re-registers through it. It does not answer its own broadcast, or another one while the same Explorer taskbar is up.
- **More than one instance.** One instance in the session owns the notification area. The others log that they are waiting, and their `items` stay empty. When the owner exits, it removes its window and broadcasts `TaskbarCreated`. When it crashes, Windows releases its owner name. Either way, a waiting instance takes over within half a second.
- **Elevation.** The window accepts `TaskbarCreated` and `WM_COPYDATA` from any integrity level, as Explorer's taskbar does.
- **The shell's own app bars.** When the first `Shell_TrayWnd` is in the calling process, Windows passes an app bar's rectangle by an address in that process, which Explorer cannot read once the message is passed on. So while a panel of this shell reserves or releases its edge, the shell puts Explorer's taskbar in front of its own window, and then puts its window back in front.

## Items

| Member | Meaning |
| --- | --- |
| `id` | The program's file name without its extension |
| `appName` | The program's description, or its file name |
| `executable` | The program's full path |
| `icon` | An `image://trayicon/…` source for the icon the app set, sharp at the screen's scale (see below) |
| `tooltip` | The tooltip text, which may run over several lines |
| `overflow` | Windows shows the icon in the hidden icons flyout, not in the taskbar |
| `menu` | A `MenuHandle`. The app draws the menu itself; `display()` asks it to open |
| `click()` | A left click at the cursor. Most apps show their window |
| `doubleClick()` | A double click at the cursor. Many older apps show their window only for this |
| `middleClick()` | A middle click at the cursor |
| `display(window, x, y)` | A right click, which asks the app to open its menu at that point in the window, or at the cursor when `window` is null |
| `ready()` | Emitted once, just after the icon is added to `items` |
| `balloon(title, message, icon)` | The app asked for a balloon notification. `icon` is a `BalloonIcon`: `None`, `Info`, `Warning`, `Error`, or `App` for the app's own icon |

`overflow` follows Settings > Personalization > Taskbar > Other system tray icons, and changes as soon as the user flips a switch there. Windows keeps that choice per icon in `HKCU\Control Panel\NotifyIconSettings`. An icon Windows has not stored a choice for yet is in the overflow, as Windows 11 does with a new icon.

An icon the app hid with `NIS_HIDDEN` is not in `items`, because Explorer does not show it either. A balloon still reaches Explorer, which shows it as a toast. The signal lets a shell show it as well.

## Icons at every scale

An app hands over one bitmap, usually sized for the primary monitor. Give the `Image` a `sourceSize` in logical pixels, and the shell asks for that size times the scale of the monitor the image is on, and asks again when the image moves to a monitor with another scale. When Windows knows the program or library the icon came from, the shell loads that icon again at the asked size. An icon the app drew itself, such as one with a badge, has no resource, and the `Image` scales the app's bitmap.

## Clicks

A click reaches the app the way Explorer sends it. The app gets the mouse messages its window would get. From interface version 3 on, it also gets `NIN_SELECT` after each left release and `WM_CONTEXTMENU` after a right one. Version 4 apps get the screen point in the message. Older apps read the cursor, so `display()` moves it to the point when it is more than three pixels away. After 400 ms it restores the previous position only if the user has left the pointer at that point.

The shell lets the app take the foreground (`AllowSetForegroundWindow`) and activates nothing itself. When the app shows nothing, the window you were typing in keeps the keyboard. A click that does not come from input to the shell, such as an IPC call, is not allowed to pass the foreground on. In that case the shell briefly joins the foreground thread and brings the app's window forward, so that the app's menu still closes when you click away.

## Limits

- Icons that Explorer draws itself, such as volume, network and battery on Windows 11, are not `Shell_NotifyIcon` icons. Use `Winshell.Audio`, `Winshell.Network` and `Winshell.Power`.
- The order of `items` is the order apps added their icons. It is not the order the user arranged in the taskbar.
- An app that finds the taskbar with `FindWindow("Shell_TrayWnd")` finds the shell's window. Menu commands (`WM_COMMAND` with no control window) and `WM_SYSCOMMAND` are posted on to Explorer's taskbar, but a command sent for an answer gets 0. Private `WM_USER` messages are not forwarded because their data layout is unknown. The window has none of the children of Explorer's, such as `TrayNotifyWnd` or `ReBarWindow32`, so an app that looks for those finds nothing.

## Checks only a live session can do

Automated tests run hosts under private window classes and never broadcast to the session. These need a person at the PC:

1. Run a shell that lists `SystemTray.items`. Restart Explorer from Task Manager, and confirm the log shows "Explorer made a new taskbar" and that every icon is still listed.
2. Run two instances, then `winshell kill` the first. Within a second, the second lists the icons.
3. Turn an icon on in Settings > Other system tray icons, and confirm its `overflow` turns false.
4. Click a tray icon in a bar whose panel does not take focus while typing in another window. An app that shows nothing (a toggle) leaves the keyboard in that window. An app with a menu opens it at the bar, and the menu closes when you click elsewhere.
5. When an app shows a balloon (for example, a download finishing in a tray app), confirm `onBalloon` fires with its title and text, and that Explorer still shows the toast.
6. On a monitor at 150% or 200%, compare a bar's tray icons with a `sourceSize` against Explorer's. Icons from a resource are sharp, not blurred by scaling.
