# The taskbar

The default shell puts a Windows 11 taskbar along the bottom of every screen. Start, Search and the apps sit in the middle, and the notification area and the clock sit on the right. The taskbar reserves its edge as an app bar, so maximized windows on that screen stop above it. Explorer's taskbar stays, so while Explorer runs the two app bars stack, unless `hideExplorerTaskbar` is true in [preferences.toml](settings.md#where-the-choices-live). That hides Explorer's taskbar window and releases the edge it reserved. Winshell puts both back when it exits. The key is false until the file sets it, and Winshell Settings has no switch for it.

A configuration you write builds its own windows. This page is the default shell.

## Settings

These keys in `preferences.toml` match Settings > Personalization > Taskbar. They apply as the file changes.

| Key | Default | What it does |
| --- | --- | --- |
| `taskbarCentered` | `true` | Start, Search and the apps in the middle. `false` puts them on the left. |
| `taskbarAutoHide` | `false` | The taskbar reserves nothing and slides down to a 2-pixel line at the bottom of the screen. The pointer brings it back at once. It stays while something it opened is open, and slides away a second after the pointer has left and nothing is open. |
| `taskbarAllScreens` | `true` | `false` shows a taskbar on the main display only. |
| `taskbarAppsOn` | `"all"` | With several screens: `"all"` shows every app on every taskbar; `"main"` shows every app on the primary screen's taskbar and only each screen's own windows on the others; `"where"` shows each window only on the taskbar of the screen it is on. Pinned apps show on the main taskbar, and on every taskbar with `"all"`. A value the shell cannot read is `"all"`. |

## Apps

Pinned apps come first, in the order they are pinned, then every other running app in the order it opened. Each app's windows are grouped on one button. The apps take the room between Search and the tray. When they need more, they scroll sideways with the mouse wheel or a drag. A focused button scrolls fully into view. A window is matched to an app through the app index. A window that sets no id takes the application user model id of its process's package, so a running packaged app joins the pin for that app. An app the index does not have is grouped by its app id.

- **Click.** Launches a pinned app that is not running. It brings the app's window forward, or minimizes the window when it is already in front. With several windows, it goes to the next window when one is in front, and otherwise to the one used last.
- **Middle click or Shift+click.** Opens a new window of the app.
- **Hover.** After 400 ms, live previews (`WindowThumbnail`) of the app's windows appear, each with its title and a close button. For an app that isn't running, only the app's name appears. Moving away closes them after 300 ms.
- **Right click.** Opens a menu with the recent and frequent documents Windows will give for that app, then the app, Pin to taskbar or Unpin from taskbar, and Close window or Close all windows. Choosing a document does not open it. Pinned items and custom tasks are not listed: Windows does not give those lists to another program. When the recent and frequent lists cannot be read, the menu says so instead of showing placeholder rows.
- **Drag.** Drag a pinned app onto another pinned app to move it. It lands before that app when you drag it back, and after it when you drag it on. The order is kept in `taskbar.json`, and it is the order Win+1 through Win+0 count. A press that stays on the button is still a click. An app that is not pinned stays where the running apps are.
- **Indicators.** A line marks a running app, and a longer accent line marks the app in front. A window that flashes for attention turns its button to the attention color until it is activated.

## Keyboard

- **Win+T** puts the keyboard on the first app of the main taskbar, with a focus rectangle. While Explorer runs, Windows keeps Win+T for its own taskbar, so the hotkey reports that it is `Taken`.
- **Win+S** opens Search on the main taskbar. **Win+A** opens quick settings, and **Win+N** opens the notification center, the same as a click on those buttons. While Explorer runs, Windows keeps these too, and each hotkey reports that it is `Taken`.
- **Win+1** through **Win+9**, and **Win+0** for the tenth app, do what a click on that button does. **Win+Shift** with the same number opens a new window of that app. The count is the buttons on the main taskbar, pinned apps first.
- **Alt+Tab**, **Alt+Shift+Tab**, **Win+Tab**, **Win+D** and **Win+X** stay with Windows until `desktopChords` is true. That switch is on the Hotkeys page. **Alt+Tab** opens a popup of the windows, each with a `WindowThumbnail`. **Alt+Shift+Tab** moves the other way. Enter, Space or a click activates the chosen window. Letting go of Alt does not, because the chord ends when Tab comes up. **Win+Tab** opens the same popup, including a desktop row when virtual desktops are supported, or closes it. **Win+D** minimizes the windows that are open and restores those on the next press. The taskbar has no show-desktop sliver. **Win+X** opens a menu at the top left of the main taskbar for Installed apps, System, Network, Settings, File Explorer, Search, Desktop and Taskbar settings. It does not shut down or open Task Manager. **Win+Alt+number** stays with Windows.
- **Narrator** can move focus to any taskbar button. The taskbar then takes the keyboard if Windows lets it, and otherwise tells Narrator that focusing failed. An app in front that has stopped answering is left in front, so focusing does not wait for it. The shell never forces its way to the foreground.
- **Tab** and **Shift+Tab**, or **Left** and **Right**, move through Start, Search, the apps, the tray icons, quick settings and the clock. An app scrolled out of view scrolls back when it gets focus.
- **Enter** or **Space** does what a click does. **Shift+Enter** opens a new window of an app. The **menu key** or **Shift+F10** opens an app's menu, or asks a tray app for its own.
- **Up** on a running app shows its previews with the keyboard in them: **Left** and **Right** move between previews and their close buttons, **Enter** brings that window forward, **Delete** closes it. In a group large enough to be listed by title, **Up** and **Down** move between rows and scroll to them, and **Tab** reaches each row's close button. When a window closes, or the group switches between previews and its list, the keyboard stays on the same window, or moves to the first one.
- In an app's menu, **Up** and **Down** move focus between the items, and Narrator reads each one.
- **Escape** closes a menu or previews and goes back to their button. On the taskbar, it gives the keyboard back to the window that had it. When another window becomes active, the taskbar lets go of the keyboard.

Narrator reads each button the way it reads Explorer's: "File Explorer - 2 running windows", "Microsoft Store pinned". It can invoke them.

Pins live in `taskbar.json` in the shell's data folder (`Shell.DataPath("taskbar.json")`). Until the user pins or unpins something, there is no file, and the pins are File Explorer, Microsoft Edge and the Microsoft Store. A pin whose app isn't in the app index, or isn't installed, has no button.

Start, Search, the tray and the clock are part of this taskbar. Start opens the launcher. Search opens Start and focuses its search box. The tray is the [notification area](tray.md): the hidden icons chevron, the icons Windows shows on the taskbar, and the quick settings button. The clock opens the [notification center and calendar](notifications.md#the-default-shell). Its accessible name includes how many notifications that center is showing. Something the taskbar has open keeps an auto-hiding taskbar up.

## Checks only a live session can do

1. Run the default shell. Each screen has a taskbar, and a maximized window stops above it.
2. Hover an app with several windows. Live previews of each window appear. Click one to bring that window forward.
3. Minimize a window, then hover its app. Check how its preview looks.
4. Have an app flash, for example a chat message while another window is in front. Its button stays marked until you switch to it.
5. Turn on Narrator and move through the taskbar. Each app is read with its windows, and Narrator can invoke it.
6. Turn on **Automatically hide the taskbar**. The taskbar slides away, and comes back when the pointer reaches the bottom edge.
