# Winshell.Capture

A still or a live capture of a window or a screen. `CaptureView` in [Winshell](winshell.md#captureview) shows a session. `WindowThumbnail` is a DWM preview and does not capture.

## CaptureSession

An explicitly started Windows capture. Stop and Dispose release all captured pixels.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `CaptureSession([IEventLoop](winshell.md#ieventloop) loop)` | constructor |  |
| `Error` | string, read-only | Why the last start or export failed, or why a running capture failed. Empty after a successful start or export. |
| `Frame` | [PixelImage](winshell.md#pixelimage)?, read-only | The latest frame, in physical pixels, or null before one arrives and after a stop. |
| `FrameInterval` | int | Minimum time between frame reads; 16–1000 ms. Frames stay on D3D until consumed. |
| `HasFrame` | bool, read-only |  |
| `IncludeCursor` | bool | Applied on the next start. Previews omit the pointer by default. |
| `PixelSize` | [QSize](winshell.md#qsize), read-only | The size of Frame in physical pixels, or zero without a frame. |
| `Running` | bool, read-only |  |
| `Target` | [CaptureTarget](#capturetarget)? | What to capture: an AppWindow from WindowListService, or a CaptureTarget. Changing it stops capture. Closing the app window clears this and stops capture. |
| `FrameReady` | Action |  |
| `CopyToClipboard(IntPtr owner)` | bool | Copies the current frame to the Windows clipboard, owned by an open window of this program. |
| `Dispose()` | void |  |
| `SaveToFile(string path)` | bool | Saves the current frame as a PNG to a new file. Takes a local path or `file://` URL. Never overwrites a file, and does not create folders. |
| `Start()` | bool | Starts capturing Target, after stopping a capture in progress. On failure Error says why. |
| `Stop()` | void | Stops capturing and drops the current frame. |

## CaptureTarget

A source in physical pixels. Monitor ids are Windows display device names.

| Member | Type | Description |
| --- | --- | --- |
| `CaptureTarget(IntPtr Window, string MonitorId)` | constructor |  |
| `MonitorId` | string, init |  |
| `Window` | IntPtr, init |  |
| `ForScreen([ScreenInfo](winshell.md#screeninfo) screen)` | [CaptureTarget](#capturetarget), static |  |
| `ForWindow(IntPtr window)` | [CaptureTarget](#capturetarget), static | A top-level window, by its handle. |
| `ForWindow([AppWindow](winshell-windows.md#appwindow) window)` | [CaptureTarget](#capturetarget), static | An AppWindow from WindowListService. The caller does not need its handle. |

