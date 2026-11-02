# Windows notification area

`Shell.Current.SystemTray` holds a `SystemTrayItem` for every tray icon that apps add with `Shell_NotifyIcon`, in the order they were added. The types are in `Winshell.Tray`. The members are in the [reference](reference/winshell-tray.md).

## How the shell sees the icons

Apps send their icons to the first window of class `Shell_TrayWnd`. A shell that uses `SystemTray` makes one, keeps it in front of Explorer's taskbar, and passes every message on to Explorer. Explorer's own tray, app bars and toasts keep working. On start, the shell broadcasts `TaskbarCreated` so that running apps add their icons again.

Foreground and desktop window-order events keep the host in front. Every thirty seconds it also checks the order and removes icons whose windows have gone. A waiting instance still checks the owner every half second.

- **Explorer restarts.** When a new Explorer taskbar broadcasts `TaskbarCreated`, the shell gets back in front and answers once with its own broadcast a second later, so every app re-registers through it. It does not answer its own broadcast, or another one while the same Explorer taskbar is up.
- **More than one instance.** One instance in the session owns the notification area. The others log that they are waiting, and their `Items` stay empty. When the owner exits, it removes its window and broadcasts `TaskbarCreated`. When it crashes, Windows releases its owner name. Either way, a waiting instance takes over within half a second.
- **Elevation.** The window accepts `TaskbarCreated` and `WM_COPYDATA` from any integrity level, as Explorer's taskbar does.
- **The shell's own app bars.** When the first `Shell_TrayWnd` is in the calling process, Windows passes an app bar's rectangle by an address in that process, which Explorer cannot read once the message is passed on. So while a panel of this shell reserves or releases its edge, the shell puts Explorer's taskbar in front of its own window, and then puts its window back in front.

## Items

| Member | Meaning |
| --- | --- |
| `Id` | The program's file name without its extension |
| `AppName` | The program's description, or its file name |
| `Executable` | The program's full path |
| `Icon` | A `PixelImage` of the icon the app set, at the size it chose. It changes when the app changes the icon. |
| `Tooltip` | The tooltip text, which may run over several lines |
| `Overflow` | Windows shows the icon in the hidden icons flyout, not in the taskbar |
| `Menu` | A `SystemTrayMenu`. The app draws the menu itself. `Display()` asks it to open |
| `Click()` | A left click at the cursor. Most apps show their window |
| `DoubleClick()` | A double click at the cursor. Many older apps show their window only for this |
| `MiddleClick()` | A middle click at the cursor |
| `Display()` | A right click, which asks the app to open its menu at the cursor |
| `Display(int x, int y)` | The same, at a point on the virtual desktop, in physical pixels |
| `IconAt(int size)` | The icon at a size in pixels. An icon loaded from a resource is drawn again at that size. Any other is the app's bitmap |
| `Ready` | Raised once, just after the icon is added to `Items` |
| `Balloon` | The app asked for a balloon: title, text, and a `BalloonIcon` (`None`, `Info`, `Warning`, `Error`, or `App`) |

`Overflow` follows Settings > Personalization > Taskbar > Other system tray icons, and changes as soon as the user flips a switch there. Windows keeps that choice per icon in `HKCU\Control Panel\NotifyIconSettings`. An icon Windows has not stored a choice for yet is in the overflow, as Windows 11 does with a new icon.

An icon the app hid with `NIS_HIDDEN` is not in `Items`, because Explorer does not show it either. A balloon still reaches Explorer, which shows it as a toast. `Balloon` lets a shell show it as well.

## Icons at every scale

An app hands over one bitmap, usually sized for the primary monitor. Call `IconAt` with the logical size times the scale of the monitor the icon is on, and call it again when the icon moves to a monitor with another scale. When Windows knows the program or library the icon came from, the shell loads that icon again at the asked size. An icon the app drew itself, such as one with a badge, has no resource, and `IconAt` returns the app's bitmap.

The default shell's tray buttons call `IconAt` with the pixel size of the button on that screen.

## Clicks

A click reaches the app the way Explorer sends it. The app gets the mouse messages its window would get. From interface version 3 on, it also gets `NIN_SELECT` after each left release and `WM_CONTEXTMENU` after a right one. Version 4 apps get the screen point in the message. Older apps read the cursor, so `Display(int, int)` moves it to the point when it is more than three pixels away. After 400 ms it restores the previous position only if the user has left the pointer at that point.

The shell lets the app take the foreground (`AllowSetForegroundWindow`) and activates nothing itself. When the app shows nothing, the window you were typing in keeps the keyboard. A click that does not come from input to the shell, such as an IPC call, is not allowed to pass the foreground on. In that case the shell briefly joins the foreground thread and brings the app's window forward, so that the app's menu still closes when you click away.

## Limits

- Icons that Explorer draws itself, such as volume, network and battery on Windows 11, are not `Shell_NotifyIcon` icons. Use `Winshell.Audio`, `Winshell.Network` and `Winshell.Power`.
- The order of `Items` is the order apps added their icons. It is not the order the user arranged in the taskbar.
- An app that finds the taskbar with `FindWindow("Shell_TrayWnd")` finds the shell's window. Menu commands (`WM_COMMAND` with no control window) and `WM_SYSCOMMAND` are posted on to Explorer's taskbar, but a command sent for an answer gets 0. Private `WM_USER` messages are not forwarded because their data layout is unknown. The window has none of the children of Explorer's, such as `TrayNotifyWnd` or `ReBarWindow32`, so an app that looks for those finds nothing.

## Checks only a live session can do

Automated tests run hosts under private window classes and never broadcast to the session. These need a person at the PC:

1. Run a shell that lists `SystemTray.Items`. Restart Explorer from Task Manager, and confirm the log shows that Explorer made a new taskbar and that every icon is still listed.
2. Run two instances, then `winshell kill` the first. Within a second, the second lists the icons.
3. Turn an icon on in Settings > Other system tray icons, and confirm its `Overflow` turns false.
4. Click a tray icon in a bar whose panel does not take focus while typing in another window. An app that shows nothing (a toggle) leaves the keyboard in that window. An app with a menu opens it at the bar, and the menu closes when you click elsewhere.
5. When an app shows a balloon (for example, a download finishing in a tray app), confirm `Balloon` is raised with its title and text, and that Explorer still shows the toast.
6. On a monitor at 150% or 200%, compare a bar's tray icons drawn with `IconAt` against Explorer's. Icons from a resource are sharp.
