# Windows capture

`import Winshell.Capture` provides `CaptureSession` and `CaptureView`. A session captures an `AppWindow` from `WindowList.windows` or a `ShellScreen` from `Shell.screens` with Windows.Graphics.Capture. Constructing or importing it does not start capture. Call `start()` in response to the user's action; it returns whether Windows started the session. Read `running`, `hasFrame`, `pixelSize` and `error`, and observe `onFrameReady`.

```qml
import Winshell
import Winshell.Capture
import QtQuick
import QtQuick.Controls

ShellRoot {
    CaptureSession {
        id: capture
        target: Shell.screens[0]
        includeCursor: false
        frameInterval: 33
    }

    FloatingWindow {
        id: window
        implicitWidth: 480
        implicitHeight: 340
        Column {
            CaptureView { session: capture; width: 480; height: 270 }
            Row {
                Button { text: "Start"; onClicked: capture.start() }
                Button { text: "Stop"; onClicked: capture.stop() }
                Button {
                    text: "Copy image"
                    enabled: capture.hasFrame
                    onClicked: capture.copyToClipboard(window)
                }
            }
        }
    }
}
```

To capture an app window, assign an `AppWindow` to `target` instead. For a taskbar or switcher preview, use `WindowThumbnail` from `Winshell.Windows`: DWM draws it live, with no capture border around the source and no pixels copied. The target must come from the current generation. Changing or destroying it stops capture; restarting requires another explicit `start()`. `includeCursor` takes effect on the next start. `frameInterval` is the minimum interval between reads, clamped to 16–1000 milliseconds; Windows supplies frames when content changes.

`CaptureView` fits and centers the image, preserving its aspect ratio. Its implicit size is the image's physical size divided by the preview window's scale. Explicit width and height are logical scene coordinates. `pixelSize` and exports always use physical pixels, including on mixed-DPI monitors. Window capture uses Windows' capture bounds, which can include the frame; it does not map a logical desktop rectangle or reposition the source. Resize replaces the capture pool/session and waits for a complete frame at the new size. A minimized, protected, locked or otherwise unavailable source may stop producing pixels. There is no GDI fallback or attempt to bypass content protection.

## Still screenshots

`saveToFile(path)` saves the last delivered frame as PNG. It accepts a local path or `file://` URL; relative paths use the configuration directory. It creates a new file, never overwrites an existing file, and does not create directories. `copyToClipboard(window)` publishes that frame as a Windows image (CF_DIBV5); it needs an open shell window to own the clipboard. Both return a boolean and set `error` on failure, such as no frame, an existing file, or a busy clipboard. They do not start capture themselves. A successful export clears the previous export error.

For one screenshot, start a session with a target and export on its first frame, then stop it:

```qml
CaptureSession {
    id: screenshot
    target: Shell.screens[0]
    onFrameReady: {
        const saved = screenshot.saveToFile("screenshot.png")
        screenshot.stop()
        if (!saved) console.warn(screenshot.error)
    }
}
```

## Windows access and lifetime

This is a desktop capture API using the supported HWND/HMONITOR interop. Target selection happens in the configuration's UI; it does not silently open the system picker. Windows enforces access and content protection. Unsupported capture and access/device failures set `error` and release the session. Closing the source stops capture. The system's capture border stays enabled; no borderless permission is requested or capability declared. See Microsoft's [desktop interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow) and [border consent rules](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired).

The session owns a private D3D11 device and a two-buffer, free-threaded WGC pool. It samples the latest frame at the configured interval, skipping older frames without per-frame posted callbacks. Frames stay on D3D until consumed. One reusable staging texture bridges to Winshell's current CPU raster Skia scene. Readback uses a nonblocking map: if the GPU is busy, delivery waits for a later read while the previous preview stays visible. This is bounded readback, not zero-copy GPU rendering. Changing the whole scene renderer is outside this module. Capture is SDR BGRA; HDR tone mapping is not implemented, so HDR content may appear clipped. See Microsoft's [frame-pool and HDR guidance](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture).

`stop()`, target destruction and generation teardown cancel pending reads, close the native session/pool, release D3D resources and discard the current image. Queued closure notifications cannot affect a newer session. Pixels are never placed in the image loader's cache, uploaded, or saved automatically. Only explicit file/clipboard exports persist beyond capture. A reload does not transfer a live session to the next generation.

Automated tests use fake frames for actions and error paths. The opt-in native test captures its own test window to a unique temporary folder and deletes it; monitor frames remain in memory. It also verifies resize and four live reloads. It never publishes to the user's clipboard or uploads capture evidence. See [development](development.md#capture-validation) to run it.
