# Winshell

The configuration's program uses these types. `Shell.Run<T>()` starts one, from `Main` marked `[STAThread]`. Windows are `PanelWindow`, `PopupWindow` and `FloatingWindow`. Their content is any WinUI element. Services hang off `Shell.Current`: [apps](winshell-apps.md), [audio](winshell-audio.md), [Bluetooth](winshell-bluetooth.md), [capture](winshell-capture.md), the [Explorer taskbar](winshell-explorertaskbar.md), [media](winshell-media.md), [network](winshell-network.md), [notifications](winshell-notifications.md), [power](winshell-power.md), [session](winshell-session.md), the [tray](winshell-tray.md), [updates](winshell-updates.md), [windows](winshell-windows.md) and [Winland](winshell-winland.md). `Shell.Ipc` is what `winshell ipc` calls. Preferences are a [TomlFile](#tomlfile).

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## CaptureView

A live view of a CaptureSession. The frame is fitted and centered, keeping its aspect ratio. Its natural size is the frame's physical size divided by this window's scale. The frame is written once into the bitmap, on the thread that delivered it, so the session's loop is this window's. The view lets go of the session when it leaves the tree.

A WinUI Panel. The members here are the ones Winshell adds.

| Member | Type | Description |
| --- | --- | --- |
| `CaptureView()` | constructor | A view with no session, which shows nothing. |
| `Session` | [CaptureSession](winshell-capture.md#capturesession)? | The capture to show. Null shows nothing. |
| `SessionProperty` | DependencyProperty, static | The capture to show. Null shows nothing. |

## ColorScheme

Whether windows are light or dark. System follows Windows.

| Value | Number | Description |
| --- | --- | --- |
| `System` | 0 | Follows the light or dark mode set in Windows. |
| `Light` | 1 | Light, whatever Windows is set to. |
| `Dark` | 2 | Dark, whatever Windows is set to. |

## Edges

The edges of a screen or a window, such as the ones a panel is anchored to. These values combine.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 | No edge. |
| `Top` | 1 | The top edge. |
| `Left` | 2 | The left edge. |
| `Right` | 4 | The right edge. |
| `Bottom` | 8 | The bottom edge. |

## FileWatch

Follows one file that may not exist yet, and calls back on the loop when it may have changed. A folder that is missing is watched from the nearest one that exists, and each folder created under it moves the watch one step closer. Nothing is called after Dispose.

| Member | Type | Description |
| --- | --- | --- |
| `FileWatch(string path, [IEventLoop](#ieventloop) loop, Action changed)` | constructor | Starts watching path, calling changed on loop. |
| `Dispose()` | void | Stops watching the file. |

## FloatingWindow

A normal window with a title bar, as Winshell Settings is.

Has the members of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `FloatingWindow()` | constructor | A hidden window of the running shell, 640 by 480 unless `Width` and `Height` say otherwise. |
| `Fullscreen` | bool | Whether the window is borderless and covering its monitor. A minimized fullscreen window stays fullscreen. |
| `Maximized` | bool | Whether the window is maximized. It follows the user's own maximize and restore. Setting it true leaves Fullscreen. |
| `MaximumSize` | Size | The largest size the user can drag the window to, in logical pixels. A width or height of 0 sets no limit. |
| `Minimized` | bool | Whether the window is minimized. It follows the user's own minimize and restore. |
| `MinimumSize` | Size | The smallest size the user can drag the window to, in logical pixels. A width or height of 0 sets no limit. |
| `Owner` | [FloatingWindow](#floatingwindow)? | The window this one stays above and closes with. It can be set before that window exists, or changed later. A window can't own itself or a window that owns it. |
| `Title` | string | The text in the title bar and on the taskbar. |

## IEventLoop

The thread the shell's objects live on. Work from other threads is posted to it.

| Member | Type | Description |
| --- | --- | --- |
| `Now` | TimeSpan, read-only | Time since the loop started, for timers and animations. |
| `Post(Action action)` | void | Runs the action on the loop's thread. Safe to call from any thread. |
| `RequestFrame(Action action)` | void | Runs the action before the next frame is drawn, once per request. |
| `Schedule(TimeSpan delay, Action action)` | IDisposable | Runs the action once after the delay, on the loop's thread. Dispose to cancel. |

## IpcException

A refused IPC request. A handler throws it to refuse a call: its message goes to the client as it is, where another exception arrives with its type name.

An exception.

| Member | Type | Description |
| --- | --- | --- |
| `IpcException(string message)` | constructor |  |

## IpcSignal

A signal of a ShellIpc target. Use it on the shell's thread.

| Member | Type | Description |
| --- | --- | --- |
| `Name` | string, read-only | The signal's name, which `winshell ipc wait` and `listen` take. |
| `Emit(string value = "")` | void | Tells every `winshell ipc wait` and `listen` following this signal, with value. |

## Log

Writes messages as ` WARN category: message`.

| Member | Type | Description |
| --- | --- | --- |
| `Default` | [Log](#log), static | This process's log. In a running shell it writes to the instance's log, which `winshell log` shows. |
| `Debug(string category, string message)` | void | Writes a LogLevel.Debug message, kept only when the shell runs with `--verbose`. |
| `Error(string category, string message)` | void | Writes a LogLevel.Error message. |
| `Info(string category, string message)` | void | Writes a LogLevel.Info message. |
| `Warn(string category, string message)` | void | Writes a LogLevel.Warn message. |
| `Write([LogLevel](#loglevel) level, string category, string message)` | void | Writes message under category, such as `tray`. A LogLevel.Debug message is dropped unless the shell runs with `--verbose`. |

## LogLevel

How serious a log message is, from least to most.

| Value | Number | Description |
| --- | --- | --- |
| `Debug` | 0 | Detail for tracking down a problem. Kept only when the shell runs with `--verbose`. |
| `Info` | 1 | Normal progress worth recording. |
| `Warn` | 2 | Something went wrong and the shell carried on. |
| `Error` | 3 | An operation failed. |
| `Fatal` | 4 | A failure that ends the process. |

## ObservableObject

The base of what a service exposes. A change is raised on the thread that made it, which for a service is its event loop, so XAML bindings and Reactor can read it.

| Member | Type | Description |
| --- | --- | --- |
| `PropertyChanged` | PropertyChangedEventHandler | Raised when a property's value changes, with the property's name. |

## PanelWindow

A window anchored to an edge of a screen. It can reserve that edge.

Has the members of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `PanelWindow()` | constructor | A hidden panel of the running shell, anchored to no edge until Anchors is set. |
| `AboveWindows` | bool | Stays above normal windows. False puts it below them, as a wallpaper is. |
| `Anchors` | [Edges](#edges) | The edges the panel sticks to. Two opposite edges stretch it across the screen. |
| `CoversScreen` | bool | Lays the panel out on the whole screen, over the taskbar and other bars, and reserves nothing. False keeps it in the work area. |
| `Focusable` | bool | Whether the panel can take the keyboard. A panel that cannot never takes activation from the app in front. |
| `Margins` | Thickness | The gap, in logical pixels, between the panel and each edge it is anchored to. Other edges' margins are unused. |
| `ReservedSize` | double | How much of its edge the panel reserves, in logical pixels, so maximized windows stay out of it. |

## PixelImage

A picture in memory: BGRA, one byte a channel, top row first, with no gap between rows. Nothing writes to the pixels after this is made, so the constructor keeps `bgra` rather than copying it, and a UI can hold on to one.

| Member | Type | Description |
| --- | --- | --- |
| `PixelImage(int width, int height, byte[] bgra, bool premultiplied = false)` | constructor | A picture of the given size that keeps bgra as its pixels. |
| `Bgra` | ReadOnlySpan of byte, read-only | The pixels: BGRA, four bytes a pixel, top row first. Read-only. |
| `Height` | int, read-only | The height in pixels. |
| `Premultiplied` | bool, read-only | Whether the color channels are already multiplied by alpha. |
| `Width` | int, read-only | The width in pixels. |
| `EncodePng()` | byte[] | The picture as a PNG file. Blocks while Windows encodes it. |

## PopupWindow

A frameless window placed next to another window.

Has the members of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `PopupWindow()` | constructor | A hidden popup of the running shell, 100 by 100 at the virtual desktop's origin until Rect is set. |
| `GrabFocus` | bool | Takes the keyboard while shown. A click outside the popup, or Esc, then dismisses it. |
| `Rect` | Rect | Where the popup sits, in logical pixels on the virtual desktop. |
| `Dismissed` | Action | A click outside the popup, or Esc, dismissed it. It is hidden. |

## ScreenInfo

A monitor, as the platform reports it. Coordinates are logical pixels on the virtual desktop: a monitor keeps its physical top-left corner and its size is divided by its scale, so monitors of mixed DPI never overlap.

| Member | Type | Description |
| --- | --- | --- |
| `ScreenInfo(string Id, string Name, string Model, string Manufacturer, string SerialNumber, Rect Geometry, Rect WorkArea, double DevicePixelRatio, double PhysicalWidthMm, double PhysicalHeightMm, int RefreshRate, bool IsPrimary)` | constructor |  |
| `DevicePixelRatio` | double, init | The monitor's scale: physical pixels per logical pixel, such as 1.5 at 150%. |
| `Geometry` | Rect, init | The whole monitor. |
| `Id` | string, init | The Windows display device name, such as `\\.\DISPLAY1`. |
| `IsPrimary` | bool, init | Whether this is the primary monitor, which Windows puts at the origin. |
| `Manufacturer` | string, init | The three-letter PnP code of the monitor's maker, such as `GSM`. Can be empty. |
| `Model` | string, init | The monitor's name as Windows lists it, such as `Generic PnP Monitor`. Can be empty. |
| `Name` | string, init | The display device name without its `\\.\` prefix, such as `DISPLAY1`. |
| `PhysicalHeightMm` | double, init | The height of the picture, in millimeters, as the monitor reports it. 0 when unknown. |
| `PhysicalWidthMm` | double, init | The width of the picture, in millimeters, as the monitor reports it. 0 when unknown. |
| `RefreshRate` | int, init | The refresh rate in hertz, or 60 when Windows does not say. |
| `SerialNumber` | string, init | The instance part of the monitor's Windows device id, which tells two monitors of one model apart. Can be empty. |
| `WorkArea` | Rect, init | The monitor without the taskbar and the edges panels reserve. |

## Shell

The running shell: its screens and services. There is one in a process. Each service is made the first time it is read, so a service a configuration does not use costs nothing and asks Windows for nothing.

| Member | Type | Description |
| --- | --- | --- |
| `AccentColor` | string | The accent every window follows, as `#RRGGBB`, or `""` to follow Windows. |
| `Apps` | [AppsService](winshell-apps.md#appsservice), read-only | The apps Start lists. |
| `Audio` | [AudioService](winshell-audio.md#audioservice), read-only | Playback and recording devices, and the audio sessions on them. |
| `Bluetooth` | [BluetoothService](winshell-bluetooth.md#bluetoothservice), read-only | The Bluetooth adapter and the paired and nearby devices. |
| `Brightness` | [BrightnessService](winshell-power.md#brightnessservice), read-only | The brightness of the connected monitors that report one. |
| `CacheDir` | string, read-only | This shell's cache folder, which exists once read. |
| `ColorScheme` | [ColorScheme](#colorscheme) | The color scheme every window follows. |
| `ConfigDir` | string, read-only | The folder of this configuration's program, where its own files are. |
| `ConfigRoot` | string, read-only | The config folder: `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` when it is set. |
| `Current` | [Shell](#shell), static, read-only | The shell of this process. Reading it before Run has started it throws. |
| `DataDir` | string, read-only | This shell's data folder, which exists once read. |
| `ExplorerTaskbar` | [ExplorerTaskbarService](winshell-explorertaskbar.md#explorertaskbarservice), read-only | Whether Explorer's taskbar is hidden. Winshell shows it again when it exits. |
| `Ipc` | [ShellIpc](#shellipc), read-only | What `winshell ipc` reaches in this shell. |
| `IsDark` | bool, read-only | Whether windows are dark. ColorScheme overrides Windows mode, and ColorScheme.System follows it. |
| `JumpLists` | [JumpListService](winshell-apps.md#jumplistservice), read-only | The recent and frequent documents of an app, as its jump list shows them. |
| `Log` | [Log](#log), read-only | This instance's log, which `winshell log` shows. |
| `LogPath` | string, read-only | This instance's log file, or empty when it writes none. |
| `Loop` | [IEventLoop](#ieventloop), read-only | The shell's thread. Post work from other threads to it before touching windows or services. |
| `Media` | [MediaService](winshell-media.md#mediaservice), read-only | The media sessions Windows lets the shell see and control. |
| `Networking` | [NetworkingService](winshell-network.md#networkingservice), read-only | The network adapters, connection profiles, radios and connectivity. |
| `Notifications` | [NotificationListener](winshell-notifications.md#notificationlistener), read-only | Windows Notification Center. Reading this never prompts for access. |
| `Options` | [ShellOptions](#shelloptions), read-only | The options the configuration passed to Run. |
| `Power` | [PowerService](winshell-power.md#powerservice), read-only | The batteries of this PC. |
| `PowerMode` | [PowerModeService](winshell-power.md#powermodeservice), read-only | The Windows power mode: best power efficiency, balanced, or best performance. |
| `RecentDocuments` | [RecentDocuments](winshell-apps.md#recentdocuments), read-only | The documents in the user's Recent folder, found by name. |
| `Screens` | ObservableCollection of [ScreenInfo](#screeninfo), read-only | The connected screens. The primary screen comes first. |
| `Session` | [SessionService](winshell-session.md#sessionservice), read-only | The Windows session: locking it, and what state it is in. |
| `ShellId` | string, read-only | The name that identifies this configuration across runs. |
| `Startup` | [StartupService](winshell-session.md#startupservice), read-only | Whether this shell runs when the user signs in. |
| `StateDir` | string, read-only | This shell's state folder, which exists once read. |
| `SystemAccent` | string, read-only | The accent Windows is using, as `#RRGGBB`. |
| `SystemTray` | [SystemTray](winshell-tray.md#systemtray), read-only | The notification area. Only one running instance in the Windows session owns it. |
| `Updates` | [UpdatesService](winshell-updates.md#updatesservice), read-only | Winshell's own updates. A configuration's own program does not update itself. |
| `Version` | string, static, read-only | The Winshell release this program is built on. |
| `VirtualDesktops` | [VirtualDesktopsService](winshell-windows.md#virtualdesktopsservice), read-only | The virtual desktops Task View shows. |
| `WindowList` | [WindowListService](winshell-windows.md#windowlistservice), read-only | The app windows the taskbar and Alt+Tab list. |
| `Winland` | [WinlandService](winshell-winland.md#winlandservice), read-only | The Winland window manager's Monitors, Workspaces and windows. The lists are empty while it is not running. |
| `CachePath(string path)` | string | A path in CacheDir. |
| `ConfigPath(string path)` | string | A path in ConfigDir. |
| `DataPath(string path)` | string | A path in DataDir. |
| `Dispose()` | void | Closes Winshell's notices, stops the services, and closes every window. Quit does this and also ends the shell. |
| `NewHotkey()` | [Hotkey](winshell-input.md#hotkey) | A hotkey that works anywhere in Windows. Set its Hotkey.Sequence, and dispose it to give the keys back. |
| `NewIdleMonitor()` | [IdleMonitor](winshell-power.md#idlemonitor) | Tells when there has been no input for a while. |
| `Quit()` | void | Ends the shell. Its windows close and Run returns. |
| `Reload()` | bool | Replaces this instance with a new process of the same program, which takes over once this one has gone. False when that process did not start: the shell then keeps running. |
| `Run<T>([ShellOptions](#shelloptions)? options = null)` | void, static | Runs a configuration until the shell quits, on this thread, as a Winshell instance: `winshell list`, `log`, `kill`, `reload` and `ipc` reach it. Call it from `Main`, marked `[STAThread]`. The program takes `--no-duplicate` and `-d` on its command line. `--no-duplicate` exits at once when its shell id is already running. `-d` starts this program again and returns, so the caller does not wait, and that start keeps `--after`. T is a ShellConfiguration with a public parameterless constructor. |
| `StatePath(string path)` | string | A path in StateDir. |

## ShellConfiguration

A configuration: the program that defines a shell. Start makes its windows.

| Member | Type | Description |
| --- | --- | --- |
| `Start([Shell](#shell) shell)` | void | Runs once, on the shell's thread, after WinUI has started. |

## ShellIpc

What `winshell ipc` reaches in a C# configuration: functions, properties and signals, by target. A function's own types declare what it takes and returns, and only string, int, bool, double, float and Color cross the pipe. The CLI's text is parsed into those types and nothing is ever evaluated. Use this on the shell's thread.

| Member | Type | Description |
| --- | --- | --- |
| `Handle(string target, string function, Delegate handler)` | IDisposable | Lets `winshell ipc call` run handler, such as `(string page) => Open(page)`. Dispose the result to take it away. A later handler for the same function replaces this one. Throws ArgumentException when the handler takes or returns a type IPC can't pass. |
| `Property<T>(string target, string property, Func of T read)` | IDisposable | Lets `winshell ipc prop get` read a value. Dispose the result to take it away. Throws ArgumentException when T is a type IPC can't pass. |
| `Signal(string target, string signal)` | [IpcSignal](#ipcsignal) | A signal `winshell ipc wait` and `listen` can follow. |

## ShellOptions

What a configuration sets before its shell starts.

| Member | Type | Description |
| --- | --- | --- |
| `ShellOptions()` | constructor |  |
| `CacheDir` | string?, init | Another folder for Shell.CacheDir, read as DataDir is. |
| `DataDir` | string?, init | Another folder for Shell.DataDir. A relative path is next to the program, `%NAME%` expands, and `$BASE` is the folder above `by-shell`, such as `%LOCALAPPDATA%\winshell\data`. |
| `ShellId` | string?, init | The name that identifies this configuration across runs. It names its data, state and cache folders. Without it, a program in the config folder is named after its folder, and one anywhere else after its path. |
| `StateDir` | string?, init | Another folder for Shell.StateDir, read as DataDir is. |

## ShellWindow

A top-level surface of the shell. Set Content to any UIElement: XAML, or a `ReactorHostControl`. Its background stays transparent, so Backdrop shows through.

Has the members of [ObservableObject](#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Active` | bool, read-only | Whether this window is the foreground window. |
| `ActualHeight` | double, read-only | The height the window has now, in logical pixels. 0 before it is first shown. |
| `ActualWidth` | double, read-only | The width the window has now, in logical pixels. 0 before it is first shown. |
| `Backdrop` | [WindowBackdrop](#windowbackdrop) | The Windows 11 material behind the content. WindowBackdrop.None by default. |
| `Content` | UIElement? | What the window shows: any UIElement, or null for nothing. |
| `Corners` | [WindowCorners](#windowcorners) | How the window's corners are drawn. WindowCorners.Default by default. |
| `Height` | double | The height in logical pixels. NaN leaves it to the window's kind: anchors, or the default size. |
| `InputRegion` | IReadOnlyList of Rect? | The parts of the window that take clicks, in logical pixels; clicks elsewhere go to the window below. Null is the whole window. An empty list lets every click through. |
| `Opacity` | double | Fades the content. The material behind it does not fade. |
| `Origin` | Point, read-only | The window's origin on the virtual desktop, in logical pixels. |
| `Scale` | double, read-only | How many physical pixels this window draws for one logical pixel. |
| `Screen` | [ScreenInfo](#screeninfo)? | The screen this window is on, or was asked to be on. Null is the primary screen. |
| `Visible` | bool | Whether the window is shown. A new window is hidden until this is set. It turns false when the user closes the window. |
| `VisibleMask` | Rect? | Clips the content to a rectangle, in logical pixels. Null shows all of it. |
| `Width` | double | The width in logical pixels. NaN leaves it to the window's kind: anchors, or the default size. |
| `Closed` | Action | The user closed the window. It is hidden, and can be shown again. |
| `Failed` | Action of Exception | The native window failed. It was rebuilt with the same content. |
| `Dispose()` | void | Closes the window for good and releases its native window. It can't be shown again. |
| `Focus()` | void | Takes the keyboard, if this kind of window may. |
| `MapToScreen(double x, double y)` | (int, int) | A point in logical window coordinates, in physical pixels on the virtual desktop. |
| `Raise()` | void | Puts the window above the others at its level, without focusing it. |
| `StartSystemMove()` | void | Lets the user move the window by dragging, as a title bar does. Call it when a pointer button goes down. |
| `StartSystemResize([Edges](#edges) edges)` | void | Lets the user resize the window by dragging an edge or a corner, as its border does. Call it when a pointer button goes down. Two adjacent edges, such as top and left, name a corner. Any other combination resizes from the bottom-right corner. |

## TomlCommentAttribute

The comment written above a key the file does not have yet. A line break starts another comment line.

An attribute.

| Member | Type | Description |
| --- | --- | --- |
| `TomlCommentAttribute(string text)` | constructor |  |
| `Text` | string, read-only | The comment's text, without the leading `#`. |

## TomlFile

A TOML file a user can edit by hand, as an object. A type derives from this and has one public read-write property per key, the key being the property's name with a lowercase first letter. Its type is string, bool, int, long, double, IReadOnlyDictionary of string and object for a table, IReadOnlyList of any of those for an array or an array of tables, or object for anything. A property that holds null until the file has the key is nullable. The file is read in the constructor, so a default belongs in the property's initializer. After that the object follows the file, and assigning a property writes it, on the loop that was passed in. Everything else in the file stays, as TomlDocument says. A key no property reads stays too, and when it differs from one only in case, by a letter, or by two in a longer key, a warning names its line and the key it may be a misspelling of. A write reads the file again and puts only assigned values into it, and the keys it does not have yet. When that read fails or does not parse, nothing is written and a warning is logged.

Has the members of [ObservableObject](#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Error` | string, read-only | Why the file could not be read, as `path[line:column]: message`, or empty when the last read parsed. |
| `Loaded` | bool, read-only | Whether the file is there and parses. Without it the properties keep the values they had. |
| `Dispose()` | void | Stops following the file. A write that was on its way runs now. |
| `Save()` | void | Writes the file now, adding the keys it does not have, or making it and its folder when there is none. |

## WindowBackdrop

What a window shows behind its content: nothing, or a Windows 11 material.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 | No material: what the content does not cover is transparent. |
| `Mica` | 1 | Mica, tinted by the desktop background, as Windows Settings uses. |
| `Acrylic` | 2 | Acrylic, a blur of what is behind the window, as Windows' flyouts use. |
| `MicaAlt` | 3 | Mica Alt, a stronger tint of Mica, as tabbed title bars use. |

## WindowCorners

How a window's corners are drawn.

| Value | Number | Description |
| --- | --- | --- |
| `Default` | 0 | The kind's own: a floating window's are Windows' choice, a popup with a backdrop is rounded, and the rest are square. |
| `Square` | 1 | Square corners. |
| `Round` | 2 | Rounded corners. |
| `RoundSmall` | 3 | Corners with a smaller rounding. |

## WindowThumbnail

A live preview of an app window, drawn by DWM into the shell window that holds this element. The image is in physical pixels relative to that window, and it is painted over the XAML, so nothing can overlap it. It follows this element's layout and the shell window moving, and it lets go of the preview when it leaves the tree.

A WinUI FrameworkElement. The members here are the ones Winshell adds.

| Member | Type | Description |
| --- | --- | --- |
| `WindowThumbnail()` | constructor | A thumbnail with no window, which shows nothing. It takes no clicks, and screen readers skip it. |
| `Peek` | bool | While true, Windows shows Window at full size and hides the others. It stays off when the user turned peek off, and it ends when this element unloads or the window goes. |
| `Window` | [AppWindow](winshell-windows.md#appwindow)? | The app window to preview, from WindowListService.Windows. Null shows nothing. |
| `PeekProperty` | DependencyProperty, static | While true, Windows shows Window at full size and hides the others. It stays off when the user turned peek off, and it ends when this element unloads or the window goes. |
| `WindowProperty` | DependencyProperty, static | The app window to preview, from WindowListService.Windows. Null shows nothing. |
