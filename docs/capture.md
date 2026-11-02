# Windows capture

[CaptureSession](reference/winshell-capture.md#capturesession) captures an `AppWindow` or a screen with Windows.Graphics.Capture. [CaptureView](reference/winshell.md#captureview) shows the latest frame in a panel. Constructing a session does not start capture. Call `Start` in response to the user's action; it returns whether Windows started the session. Read `Running`, `HasFrame`, `PixelSize` and `Error`, and handle `FrameReady`.

Set `Target` to `CaptureTarget.ForScreen(shell.Screens[0])` or `CaptureTarget.ForWindow(window)`. For a taskbar or switcher preview, use [WindowThumbnail](reference/winshell.md#windowthumbnail): DWM draws it live, with no capture border around the source and no pixels copied. The thumbnail paints over XAML, so nothing can overlap it. Changing `Target`, or disposing the session, stops capture. Restarting requires another `Start`. `IncludeCursor` takes effect on the next start. `FrameInterval` is the minimum interval between reads, from 16 to 1000 milliseconds. Windows supplies frames when content changes.

`CaptureView` fits and centers the image, preserving its aspect ratio. Its implicit size is the image's physical size divided by the preview window's scale. Explicit width and height are logical pixels. `PixelSize` and exports always use physical pixels, including on mixed-DPI monitors. Window capture uses Windows' capture bounds, which can include the frame. Resize replaces the capture pool and waits for a complete frame at the new size. A minimized, protected, locked or otherwise unavailable source may stop producing pixels. There is no GDI fallback or attempt to bypass content protection.

## Still screenshots

`SaveToFile(path)` saves the last delivered frame as PNG. It accepts a local path or `file://` URL. It creates a new file, never overwrites an existing file, and does not create directories. `CopyToClipboard(owner)` publishes that frame as a Windows image and needs an open window of this program to own the clipboard. Both return a boolean and set `Error` on failure, such as no frame, an existing file, or a busy clipboard. They do not start capture themselves. A successful export clears the previous export error.

`Frame` is a [PixelImage](reference/winshell.md#pixelimage): width, height, and premultiplied BGRA. `EncodePng` writes those bytes.
