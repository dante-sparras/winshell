# Winshell.Winland

Monitors, workspaces and windows from Winland, a separate window manager for Windows with workspaces per monitor. Winland speaks Hyprland's IPC over a named pipe: `j/` queries, `dispatch` requests and a `name>>data` event stream. This module is Winshell's counterpart of `Quickshell.Hyprland`; [Migrating from Quickshell](../migrating-from-quickshell.md#types) maps the type names.

`import Winshell.Winland`

Winshell connects to the pipe named by the `WINLAND_PIPE` environment variable, or else `\\.\pipe\winland-<session id>`, then `\\.\pipe\winland` for older Winland builds. It only talks to a Winland in its own Windows session. While Winland is not running the lists are empty. Winshell looks for it after three seconds, then twice as long each time, up to every thirty seconds. It fills the lists when Winland starts and empties them again when it exits.

## Winland

The singleton for the whole shell. It reads Winland's state when it connects and keeps it current from the event stream, updating the same objects for each event.

| Member | Type | Description |
| --- | --- | --- |
| `monitors` | ObjectModel of [WinlandMonitor](#winlandmonitor), read-only | The monitors, by `id`. |
| `workspaces` | ObjectModel of [WinlandWorkspace](#winlandworkspace), read-only | The workspaces, by `id`, then by monitor where two have the same number. |
| `toplevels` | ObjectModel of [WinlandWindow](#winlandwindow), read-only | The windows Winland manages, in the order they were first reported. |
| `focusedMonitor` | [WinlandMonitor](#winlandmonitor), read-only | The monitor Winland has focused, or null. |
| `focusedWorkspace` | [WinlandWorkspace](#winlandworkspace), read-only | The workspace shown on the focused monitor, or null. |
| `activeToplevel` | [WinlandWindow](#winlandwindow), read-only | The window Winland reports as active, or null. |
| `usingLua` | bool, read-only | Always false: Winland has no Lua mode. |
| `requestSocketPath` | string, read-only | The path of Winland's pipe, such as `\\.\pipe\winland-1`. The pipe takes requests and also streams events. |
| `eventSocketPath` | string, read-only | The same pipe path as `requestSocketPath`. |
| `dispatch(request)` | method | Sends `dispatch` and the request to Winland, such as `Winland.dispatch("workspace 3")`. A failure is logged as a warning in the `winland` category. |
| `refreshMonitors()` | method | Asks Winland for its monitors again. Updates the listed ones and removes those Winland no longer reports. |
| `refreshWorkspaces()` | method | Asks Winland for its workspaces again and updates the listed ones. |
| `refreshToplevels()` | method | Asks Winland for its windows again, adding new ones and updating titles and workspaces. |
| `monitorFor(screen)` | method, returns [WinlandMonitor](#winlandmonitor) | The monitor of a [ShellScreen](winshell.md#shellscreen), matched by its `\\.\DISPLAYn` name. Before Winland's first monitor list arrives, it adds a monitor for the screen that the list fills in later; after it, null when Winland reports no such monitor. |
| `rawEvent(event)` | signal | Emitted for every line of the event stream, after the objects are updated. `event` is the [WinlandEvent](#winlandevent). |

## WinlandMonitor

A monitor as Winland reports it, reached from `Winland.monitors` and other Winland objects.

| Member | Type | Description |
| --- | --- | --- |
| `id` | int, read-only | Winland's number for the monitor. -1 until Winland reports it. |
| `name` | string, read-only | The Windows display name, such as `\\.\DISPLAY1`, which is the screen's id. |
| `description` | string, read-only | Winland's description of the monitor. |
| `x` | int, read-only | The left edge, as Winland reports it. |
| `y` | int, read-only | The top edge, as Winland reports it. |
| `width` | int, read-only | The width, as Winland reports it. |
| `height` | int, read-only | The height, as Winland reports it. |
| `scale` | real, read-only | The monitor's scale, as Winland reports it. |
| `focused` | bool, read-only | Whether this is `Winland.focusedMonitor`. |
| `activeWorkspace` | [WinlandWorkspace](#winlandworkspace), read-only | The workspace shown on the monitor, or null. |
| `lastIpcObject` | var, read-only | The monitor's entry from Winland's last monitor list, as a JavaScript object. Null before the first one. |

## WinlandWorkspace

A workspace, reached from `Winland.workspaces` and other Winland objects. Winland numbers workspaces per monitor, so two can both be 3; `monitor` tells them apart.

| Member | Type | Description |
| --- | --- | --- |
| `id` | int, read-only | The workspace number. A named workspace gets a negative id, and `special:special` gets -99. |
| `name` | string, read-only | The workspace's name: its number, a name, or `special:` and a name. |
| `address` | string, read-only | The same as `name`. |
| `monitor` | [WinlandMonitor](#winlandmonitor), read-only | The monitor the workspace is on, or null. |
| `active` | bool, read-only | Whether the workspace is shown on its monitor. |
| `focused` | bool, read-only | Whether this is `Winland.focusedWorkspace`. |
| `urgent` | bool, read-only | Whether a window on it is urgent while the workspace is not focused. |
| `hasFullscreen` | bool, read-only | Whether a window on it is fullscreen. |
| `toplevels` | ObjectModel of [WinlandWindow](#winlandwindow), read-only | The windows on the workspace. |
| `lastIpcObject` | var, read-only | The workspace's entry from Winland's last workspace list, as a JavaScript object. Null before the first one. |
| `activate()` | method | Shows the workspace. When it is on a monitor other than the focused one, that monitor is focused first. |
| `typeChanged()` | signal | Never emitted on this checkout. |

## WinlandWindow

A window as Winland reports it, reached from `Winland.toplevels` and `WinlandWorkspace.toplevels`.

| Member | Type | Description |
| --- | --- | --- |
| `address` | string, read-only | The window handle in hexadecimal, without `0x`. |
| `wayland` | [AppWindow](winshell-windows.md#appwindow), read-only | The same window from `WindowList`. Null unless the shell uses `WindowList` from `Winshell.Windows`. |
| `title` | string, read-only | The window's title. |
| `workspace` | [WinlandWorkspace](#winlandworkspace), read-only | The workspace the window is on, or null. |
| `monitor` | [WinlandMonitor](#winlandmonitor), read-only | The monitor of the window's workspace, or null. |
| `activated` | bool, read-only | Whether this is `Winland.activeToplevel`. |
| `urgent` | bool, read-only | Whether Winland marked the window urgent. It clears when the window is activated or its workspace is focused. |
| `lastIpcObject` | var, read-only | The window's entry from Winland's last window list, as a JavaScript object. Null before the first one. |

## WinlandEvent

One line of the event stream, passed to `Winland.rawEvent`. The same object is reused for every event, so read it in the handler.

| Member | Type | Description |
| --- | --- | --- |
| `name` | string, read-only | The event's name, before `>>`, such as `workspacev2`. |
| `data` | string, read-only | The text after `>>`. |
| `parse(argumentCount)` | method, returns list of string | Splits `data` at commas into that many strings. The last keeps any further commas, and missing ones are empty. |

## Examples

A button for each workspace, highlighted while it is focused, and the active window's title.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Winland

Row {
    spacing: 4
    Repeater {
        model: Winland.workspaces
        Button {
            required property var modelData
            text: modelData.name
            highlighted: modelData.focused
            onClicked: modelData.activate()
        }
    }
    Text { text: Winland.activeToplevel ? Winland.activeToplevel.title : "" }
}
```
