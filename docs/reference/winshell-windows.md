# Winshell.Windows

`import Winshell.Windows`. The other programs' windows, as the taskbar and Alt+Tab list them, live previews of them, and the virtual desktops Task View shows. A shell uses it for a taskbar, a window switcher, or a desktop switcher. The default shell's [taskbar](../taskbar.md) is built on it.

[Migrating from Quickshell](../migrating-from-quickshell.md#windows) maps the `ToplevelManager` names onto these, and [Virtual desktops](../migrating-from-quickshell.md#virtual-desktops) says which Windows builds `VirtualDesktops` works on.

## WindowList

The `WindowList` singleton: the app windows, as the taskbar and Alt+Tab list them. Reach it as `WindowList`, for example `WindowList.windows`.

| Member | Type | Description |
| --- | --- | --- |
| `windows` | ObjectModel of [AppWindow](#appwindow), read-only | The app windows; a new window is added at the end. A window is listed when it is visible, not cloaked, has a title, and is one the taskbar gives a button. The shell's own windows are not listed. |
| `activeWindow` | [AppWindow](#appwindow), read-only | The window in the foreground, or null when the foreground is not an app window, such as the desktop or the shell itself. |

## AppWindow

A top-level window of another program. Windows are in `WindowList.windows`; QML cannot create one. When the window closes, it emits `closed()` and leaves the list.

| Member | Type | Description |
| --- | --- | --- |
| `title` | string, read-only | The window's title. |
| `appId` | string, read-only | The AppUserModelID the window carries. When the window sets none, the application user model id of its process's package, or its program's name in lower case. `Apps.find(window.appId)` in [Winshell.Apps](winshell-apps.md#apps) finds its app. |
| `executable` | string, read-only | The full path of the program that owns the window, or empty when it cannot be read. |
| `icon` | string, read-only | A path the icon provider draws: a packaged app's Start icon, or the program's own icon. Use it as an `Image` source. |
| `desktopId` | string, read-only | The id of the virtual desktop this window is on, or empty when Windows does not report one. This works on every Windows build, also when `VirtualDesktops.supported` is false. |
| `desktop` | [VirtualDesktop](#virtualdesktop), read-only | The desktop in `VirtualDesktops.desktops` whose id is `desktopId`, or null when that list is not loaded. |
| `activated` | bool, read-only | True while the window is in the foreground. |
| `attention` | bool, read-only | Whether the window flashed to ask for attention, as a chat or a finished download does, and has not been activated since. The taskbar shows this as a highlighted button. |
| `parent` | [AppWindow](#appwindow), read-only | The window that owns this one, such as the main window of a dialog, when it is listed too. Otherwise null. |
| `screens` | list of [ShellScreen](winshell.md#shellscreen), read-only | The screen the window is on: the one Windows says it is nearest. Empty when the screen is not known. |
| `maximized` | bool | Whether the window is maximized. Setting it maximizes or restores the window. |
| `minimized` | bool | Whether the window is minimized. Setting it minimizes or restores the window. |
| `fullscreen` | bool, read-only | Whether the window covers its screen without a frame. Windows has no request that puts another program's window into fullscreen. |
| `activate()` | method | Brings the window to the foreground, restoring it when it is minimized. |
| `close()` | method | Asks the window to close, as its close button does. The app may ask to save first, or refuse. |
| `moveTo(screen)` | method | Moves the window onto a monitor, a [ShellScreen](winshell.md#shellscreen). Its logical size stays, and a maximized window is maximized again there. |
| `moveToDesktop(desktop)` | method | Moves the window onto a [VirtualDesktop](#virtualdesktop). This works on every Windows build, also when `VirtualDesktops.supported` is false. |
| `closed()` | signal | The window closed. It comes after the window has left `WindowList.windows`, just before the object goes away. |

## WindowThumbnail

A live preview of an app window, as the taskbar and Alt+Tab show it. It is an Item: create one with `WindowThumbnail { window: WindowList.activeWindow }`. It has the members of an Item, and these:

| Member | Type | Description |
| --- | --- | --- |
| `window` | [AppWindow](#appwindow) | The app window to preview, from `WindowList.windows`. Null shows nothing. It becomes null when the window closes. |
| `sourceSize` | size, read-only | The previewed part of the window in physical pixels, without its invisible resize borders, or zero before the preview is first shown. |
| `peek` | bool | While true, Windows shows this window at full size and hides the others, as its own peek does. It stays off when the user turned peek off in System Properties, and ends with this item or the window. Windows has no documented call for this, so a build without it shows no peek. |

Windows draws the preview over the shell window this item is in, so it stays live without copying pixels and puts no capture border around the source. It fills the item as far as it can without being enlarged, keeping its shape, centered. Without a size of its own, the item takes the window's size in logical pixels. The preview follows the item's position and opacity, and is hidden whenever the item is not drawn. Other items cannot draw over it, and a clipping parent does not clip it.

## VirtualDesktops

The `VirtualDesktops` singleton: the desktops Task View shows. Reach it as `VirtualDesktops`, for example `VirtualDesktops.current`.

| Member | Type | Description |
| --- | --- | --- |
| `desktops` | ObjectModel of [VirtualDesktop](#virtualdesktop), read-only | The desktops, in Task View order. Empty when `supported` is false. |
| `supported` | bool, read-only | Whether the desktop list can be read and changed on this Windows build. |
| `unsupportedReason` | string, read-only | Why `supported` is false, such as a Windows build outside the ones Winshell knows. Empty when the list can be used. |
| `current` | [VirtualDesktop](#virtualdesktop), read-only | The desktop the user is on, or null when `supported` is false. |
| `switchTo(desktop)` | method | Switches to that desktop. |
| `create()` | method, returns [VirtualDesktop](#virtualdesktop) | Creates a desktop and returns it, or returns null when Windows did not create one. |
| `remove(desktop)` | method | Removes a desktop and moves its windows to the desktop before it, or to the next one when it is the first. The last desktop stays. |

`supported` is false outside Windows builds 26100 through 26399, or when the desktop list cannot be read; then `switchTo()`, `create()` and `remove()` do nothing and write a warning to the log.

The list is read again when Windows switches desktop, about once a second, and after `switchTo()`, `create()` or `remove()`. A desktop added, renamed or removed outside Winshell shows up on that read.

## VirtualDesktop

One desktop Task View can switch to. Desktops are in `VirtualDesktops.desktops`; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | The desktop's id, the same text `AppWindow.desktopId` uses. |
| `name` | string, read-only | The name the user gave the desktop in Task View, or `Desktop 1`, `Desktop 2` and so on when it has none. |
| `index` | int, read-only | Where the desktop sits in `VirtualDesktops.desktops`, starting at zero. |
| `current` | bool, read-only | True for the desktop the user is on. |

## Examples

A button for each app window. Clicking one brings it forward, or minimizes it when it is already in front.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Windows

Column {
    Repeater {
        model: WindowList.windows
        Button {
            required property var modelData
            text: (modelData.attention ? "! " : "") + modelData.title
            onClicked: {
                if (modelData.activated)
                    modelData.minimized = true
                else
                    modelData.activate()
            }
        }
    }
}
```

A window with a live preview of the window in front.

```qml
import QtQuick
import Winshell
import Winshell.Windows

FloatingWindow {
    implicitWidth: 320
    implicitHeight: 200

    WindowThumbnail {
        anchors.fill: parent
        window: WindowList.activeWindow
    }
}
```

A desktop switcher, with a button that adds a desktop.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Windows

Row {
    visible: VirtualDesktops.supported

    Repeater {
        model: VirtualDesktops.desktops
        Button {
            required property var modelData
            text: modelData.name + (modelData.current ? " (current)" : "")
            onClicked: VirtualDesktops.switchTo(modelData)
        }
    }
    Button {
        text: "+"
        onClicked: VirtualDesktops.create()
    }
}
```
