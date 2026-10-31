# Winshell.Tray

The tray icons apps add to the notification area with `Shell_NotifyIcon`, with their tooltips, menus and clicks. Explorer's own tray keeps working. [Windows notification area](../tray.md) explains how the shell receives the icons, how clicks reach the app, and the limits.

`import Winshell.Tray`

## SystemTray

The singleton for the whole shell. It lists every app's tray icon. Only one running instance in the Windows session owns the notification area; in the others `items` stays empty until that one exits.

| Member | Type | Description |
| --- | --- | --- |
| `items` | ObjectModel of [SystemTrayItem](#systemtrayitem), read-only | The tray icons, in the order apps added them, not the order the user arranged. An icon the app hid is left out until the app shows it again. |

## SystemTrayItem

One app's tray icon, reached from `SystemTray.items`. Its menu belongs to the app, which draws it itself. `display()` asks the app to open it.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | The program's file name without its extension. |
| `appName` | string, read-only | The program's description, as Task Manager shows it, or its file name. |
| `executable` | string, read-only | The full path of the program that added the icon. |
| `icon` | string, read-only | An `image://trayicon/…` source for the icon the app set. It changes when the app changes the icon. Give the `Image` a `sourceSize` to get it sharp at the screen's scale. |
| `tooltip` | string, read-only | The icon's tooltip, which may run over several lines. |
| `overflow` | bool, read-only | Whether Windows shows the icon in the overflow rather than on the taskbar. Users choose in Settings. |
| `menu` | [MenuHandle](winshell.md#menuhandle), read-only | The app's menu. `display()` opens it. |
| `click()` | method | A left click at the cursor, which most apps answer by showing their window. |
| `doubleClick()` | method | A double click at the cursor, which many older apps need before they show their window. |
| `middleClick()` | method | A middle click at the cursor. |
| `display(parentWindow, relativeX, relativeY)` | method | A right click, which asks the app to open its menu at that point in the window, or at the cursor when `parentWindow` is null. |
| `ready()` | signal | Emitted once, just after the icon is added to `items`. |
| `balloon(title, message, icon)` | signal | Emitted when the app shows a balloon notification. `icon` is a [BalloonIcon](#balloonicon) value. Explorer still shows it as a toast. |

## BalloonIcon

The icon a balloon notification asked for. `App` is the app's own icon.

Values: `None`, `Info`, `Warning`, `Error`, `App`

## Examples

A bar with the tray icons Settings shows on the taskbar. A right click opens the app's menu under the icon.

```qml
import QtQuick
import Winshell
import Winshell.Tray

PanelWindow {
    id: bar
    anchors { top: true; left: true; right: true }
    implicitHeight: 32
    Row {
        Repeater {
            model: SystemTray.items
            MouseArea {
                id: icon
                required property var modelData
                visible: !modelData.overflow
                width: 32; height: 32
                acceptedButtons: Qt.LeftButton | Qt.MiddleButton | Qt.RightButton
                onClicked: mouse => {
                    const point = icon.mapToItem(null, icon.width / 2, icon.height)
                    if (mouse.button === Qt.RightButton) icon.modelData.display(bar, point.x, point.y)
                    else if (mouse.button === Qt.MiddleButton) icon.modelData.middleClick()
                    else icon.modelData.click()
                }
                Image { anchors.centerIn: parent; source: icon.modelData.icon; sourceSize: Qt.size(16, 16) }
            }
        }
    }
}
```
