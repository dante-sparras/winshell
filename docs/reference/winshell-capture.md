# Winshell.Capture

`import Winshell.Capture`. The module captures an app window or a whole display with Windows.Graphics.Capture, shows it live, and saves or copies a frame. Nothing is captured until `start()` is called.

[Windows capture](../capture.md) has complete examples, the rules Windows applies to capture, and how frames are read and released. For a taskbar or switcher thumbnail, `WindowThumbnail` from `Winshell.Windows` is lighter: Windows draws it, and no pixels are copied.

## CaptureSession

One capture, created with `CaptureSession { }`. Set `target`, then call `start()`, usually from a click. Stopping it, destroying it, or reloading the shell releases every captured pixel.

| Member | Type | Description |
| --- | --- | --- |
| `target` | AppWindow or ShellScreen | What to capture: an `AppWindow` from `WindowList.windows`, or a `ShellScreen` from `Shell.screens`. Changing it stops capture, and so does destroying the target. |
| `running` | bool, read-only | Whether the session is capturing. |
| `hasFrame` | bool, read-only | Whether a frame has arrived since the last start. |
| `error` | string, read-only | Why the last start or export failed, or why a running capture failed. Empty after a successful start or export. |
| `pixelSize` | size, read-only | The size of the current frame in physical pixels, or zero without a frame. |
| `includeCursor` | bool | Draws the pointer into the frames. Default `false`. Takes effect on the next `start()`. |
| `frameInterval` | int | The shortest time between two frame reads, in milliseconds. Default `33`, kept between `16` and `1000`. |
| `start()` | method, returns bool | Starts capturing `target`, after stopping a capture in progress. Returns whether Windows started it; on failure `error` says why. |
| `stop()` | method | Stops capturing and drops the current frame. |
| `saveToFile(path)` | method, returns bool | Saves the current frame as a PNG to a new file. Takes a local path or `file://` URL; a relative path is relative to the config folder. Never overwrites a file, and does not create folders. Returns false and sets `error` on failure. |
| `copyToClipboard(owner)` | method, returns bool | Copies the current frame to the Windows clipboard as an image. `owner` is an open shell window, which owns the clipboard data. Returns false and sets `error` on failure. |
| `frameReady()` | signal | A new frame arrived. |

## CaptureView

A live preview of a [CaptureSession](#capturesession), created with `CaptureView { }`. It is an Item: it fits the frame inside its width and height, keeps the aspect ratio, and centers it. Its implicit size is the frame's size divided by the window's scale.

| Member | Type | Description |
| --- | --- | --- |
| `session` | [CaptureSession](#capturesession) | The session to show. The session owns the pixels; the view only draws its current frame. |

```qml
import QtQuick
import Winshell
import Winshell.Capture

FloatingWindow {
    id: window
    implicitWidth: 480
    implicitHeight: 270

    CaptureSession {
        id: capture
        target: Shell.screens[0]
    }

    CaptureView {
        anchors.fill: parent
        session: capture
    }

    MouseArea {
        anchors.fill: parent
        onClicked: capture.running ? capture.stop() : capture.start()
    }
}
```
