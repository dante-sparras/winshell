# Winshell

The configuration's program uses these types. `Shell.Run<T>()` starts one, from `Main` marked `[STAThread]`. Windows are `PanelWindow`, `PopupWindow` and `FloatingWindow`. Their content is any WinUI element. Services hang off `Shell.Current`: [apps](winshell-apps.md), [audio](winshell-audio.md), [Bluetooth](winshell-bluetooth.md), [capture](winshell-capture.md), the [Explorer taskbar](winshell-explorertaskbar.md), [media](winshell-media.md), [network](winshell-network.md), [notifications](winshell-notifications.md), [power](winshell-power.md), [session](winshell-session.md), the [tray](winshell-tray.md), [updates](winshell-updates.md), [windows](winshell-windows.md) and [Winland](winshell-winland.md). `Shell.Ipc` is what `winshell ipc` calls. Preferences are a [TomlFile](#tomlfile).

## Shell

The running shell: its screens and services. There is one in a process.

| Member | Type | Description |
| --- | --- | --- |
| `AccentColor` | string | The accent every window follows, as `#RRGGBB`, or `""` to follow Windows. |
| `Apps` | [AppsService](winshell-apps.md#appsservice), read-only | The apps Start lists. |
| `Audio` | [AudioService](winshell-audio.md#audioservice), read-only | Playback and recording devices, and the audio sessions on them. |
| `Bluetooth` | [BluetoothService](winshell-bluetooth.md#bluetoothservice), read-only |  |
| `Brightness` | [BrightnessService](winshell-power.md#brightnessservice), read-only |  |
| `CacheDir` | string, read-only | This shell's cache folder, which exists once read. |
| `ColorScheme` | [ColorScheme](#colorscheme) | The color scheme every window follows. |
| `ConfigDir` | string, read-only | The folder of this configuration's program, where its own files are. |
| `ConfigRoot` | string, read-only | The config folder: `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` when it is set. |
| `Current` | [Shell](#shell), static, read-only | The shell of this process. Reading it before Run has started it throws. |
| `DataDir` | string, read-only | This shell's data folder, which exists once read. |
| `ExplorerTaskbar` | [ExplorerTaskbarService](winshell-explorertaskbar.md#explorertaskbarservice), read-only | Whether Explorer's taskbar is hidden. Winshell shows it again when it exits. |
| `Ipc` | [ShellIpc](#shellipc), read-only | What `winshell ipc` reaches in this shell. |
| `IsDark` | bool, read-only | Whether windows are dark. ColorScheme overrides Windows mode, and System follows it. |
| `JumpLists` | [JumpListService](winshell-apps.md#jumplistservice), read-only |  |
| `Log` | [Log](#log), read-only |  |
| `LogPath` | string, read-only | This instance's log file, or empty when it writes none. |
| `Loop` | [IEventLoop](#ieventloop), read-only |  |
| `Media` | [MediaService](winshell-media.md#mediaservice), read-only | The media sessions Windows lets the shell see and control. |
| `Networking` | [NetworkingService](winshell-network.md#networkingservice), read-only |  |
| `Notifications` | [NotificationListener](winshell-notifications.md#notificationlistener), read-only | Windows Notification Center. Reading this never prompts for access. |
| `Options` | [ShellOptions](#shelloptions), read-only |  |
| `Power` | [PowerService](winshell-power.md#powerservice), read-only | The batteries of this PC. |
| `PowerMode` | [PowerModeService](winshell-power.md#powermodeservice), read-only |  |
| `RecentDocuments` | [RecentDocuments](winshell-apps.md#recentdocuments), read-only |  |
| `Screens` | ObservableCollection of [ScreenInfo](#screeninfo), read-only | The connected screens. The primary screen comes first. |
| `Session` | [SessionService](winshell-session.md#sessionservice), read-only | The Windows session: locking it, and what state it is in. |
| `ShellId` | string, read-only | The name that identifies this configuration across runs. |
| `Startup` | [StartupService](winshell-session.md#startupservice), read-only | Whether this shell runs when the user signs in. |
| `StateDir` | string, read-only | This shell's state folder, which exists once read. |
| `SystemAccent` | string, read-only | The accent Windows is using, as `#RRGGBB`. |
| `SystemTray` | [SystemTray](winshell-tray.md#systemtray), read-only | The notification area. Only one running instance in the Windows session owns it. |
| `Updates` | [UpdatesService](winshell-updates.md#updatesservice), read-only | Winshell's own updates. A configuration's own program does not update itself. |
| `VirtualDesktops` | [VirtualDesktopsService](winshell-windows.md#virtualdesktopsservice), read-only |  |
| `Version` | string, static, read-only | The Winshell release this program is built on. |
| `WindowList` | [WindowListService](winshell-windows.md#windowlistservice), read-only | The app windows the taskbar and Alt+Tab list. |
| `Winland` | [WinlandService](winshell-winland.md#winlandservice), read-only |  |
| `CachePath(string path)` | string | A path in CacheDir. |
| `ConfigPath(string path)` | string | A path in ConfigDir. |
| `DataPath(string path)` | string | A path in DataDir. |
| `Dispose()` | void |  |
| `NewHotkey()` | [Hotkey](winshell-input.md#hotkey) | A hotkey that works anywhere in Windows. Set its Sequence, and dispose it to give the keys back. |
| `NewIdleMonitor()` | [IdleMonitor](winshell-power.md#idlemonitor) | Tells when there has been no input for a while. |
| `Quit()` | void | Ends the shell. Its windows close and Run returns. |
| `Reload()` | bool | Replaces this instance with a new process of the same program, which takes over once this one has gone. Until the CLI builds a reload, that is all it is. False when that process did not start: the shell then keeps running. |
| `Run<T>([ShellOptions](#shelloptions)? options = null)` | void, static | Runs a configuration until the shell quits, on this thread, as a Winshell instance: `winshell list`, `log`, `kill`, `reload` and `ipc` reach it. Call it from `Main`, marked `[STAThread]`. The program takes `--no-duplicate` and `-d` on its command line. `--no-duplicate` exits at once when its shell id is already running. `-d` starts this program again and returns, so the caller does not wait, and that start keeps `--after`. T is ShellConfiguration with a public parameterless constructor. |
| `StatePath(string path)` | string | A path in StateDir. |

## ShellOptions

What a configuration sets before its shell starts.

| Member | Type | Description |
| --- | --- | --- |
| `ShellOptions()` | constructor |  |
| `CacheDir` | string?, init | Another folder for CacheDir, read as DataDir is. |
| `DataDir` | string?, init | Another folder for DataDir. A relative path is next to the program, `%NAME%` expands, and `$BASE` is the folder above `by-shell`, such as `%LOCALAPPDATA%\winshell\data`. |
| `ShellId` | string?, init | The name that identifies this configuration across runs. It names its data, state and cache folders. Without it, a program in the config folder is named after its folder, and one anywhere else after its path. |
| `StateDir` | string?, init | Another folder for StateDir, read as DataDir is. |

## ShellConfiguration

A configuration: the program that defines a shell. Start makes its windows.

| Member | Type | Description |
| --- | --- | --- |
| `Start([Shell](#shell) shell)` | void | Runs once, on the shell's thread, after WinUI has started. |

## ShellWindow

A top-level surface of the shell. Set Content to any UIElement: XAML, or a `ReactorHostControl`. Its background stays transparent, so Backdrop shows through.

Has the members of [ObservableObject](#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Active` | bool, read-only | Whether this window is the foreground window. |
| `ActualHeight` | double, read-only |  |
| `ActualWidth` | double, read-only |  |
| `Backdrop` | [WindowBackdrop](#windowbackdrop) |  |
| `Content` | UIElement? |  |
| `Corners` | [WindowCorners](#windowcorners) |  |
| `Height` | double | The height in logical pixels. NaN leaves it to the window's kind: anchors, or the default size. |
| `InputRegion` | IReadOnlyList of Rect? | The parts of the window that take clicks, in logical pixels; clicks elsewhere go to the window below. Null is the whole window. An empty list lets every click through. |
| `Opacity` | double | Fades the content. The material behind it does not fade. |
| `Origin` | Point, read-only | The window's origin on the virtual desktop, in logical pixels. |
| `Scale` | double, read-only | How many physical pixels this window draws for one logical pixel. |
| `Screen` | [ScreenInfo](#screeninfo)? | The screen this window is on, or was asked to be on. Null is the primary screen. |
| `Visible` | bool |  |
| `VisibleMask` | Rect? | Clips the content to a rectangle, in logical pixels. Null shows all of it. |
| `Width` | double | The width in logical pixels. NaN leaves it to the window's kind: anchors, or the default size. |
| `Closed` | Action |  |
| `Failed` | Action of Exception |  |
| `Dispose()` | void |  |
| `Focus()` | void | Takes the keyboard, if this kind of window may. |
| `MapToScreen(double x, double y)` | (int, int) | A point in logical window coordinates, in physical pixels on the virtual desktop. |
| `Raise()` | void |  |
| `StartSystemMove()` | void |  |
| `StartSystemResize([Edges](#edges) edges)` | void |  |

## PanelWindow

A window anchored to an edge of a screen. It can reserve that edge.

Has the members of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `PanelWindow()` | constructor |  |
| `AboveWindows` | bool | Stays above normal windows. False puts it below them, as a wallpaper is. |
| `Anchors` | [Edges](#edges) | The edges the panel sticks to. Two opposite edges stretch it across the screen. |
| `ExclusionMode` | [PanelExclusionMode](#panelexclusionmode) |  |
| `ExclusiveZone` | double | How much of its edge the panel reserves, in logical pixels, so maximized windows stay out of it. |
| `Focusable` | bool | Whether the panel can take the keyboard. A panel that cannot never takes activation from the app in front. |
| `Margins` | Thickness |  |

## PopupWindow

A frameless window placed next to another window.

Has the members of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `PopupWindow()` | constructor |  |
| `GrabFocus` | bool | Takes the keyboard while shown. A click outside the popup, or Esc, then dismisses it. |
| `Rect` | Rect | Where the popup sits, in logical pixels on the virtual desktop. |
| `Dismissed` | Action |  |

## FloatingWindow

A normal window with a title bar, as Winshell Settings is.

Has the members of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `FloatingWindow()` | constructor |  |
| `Fullscreen` | bool |  |
| `Maximized` | bool |  |
| `MaximumSize` | Size |  |
| `Minimized` | bool |  |
| `MinimumSize` | Size |  |
| `Owner` | [FloatingWindow](#floatingwindow)? | The window this one stays above and closes with. |
| `Title` | string |  |

## WindowThumbnail

A live preview of an app window, drawn by DWM into the shell window that holds this element. The image is in physical pixels relative to that window, and it is painted over the XAML, so nothing can overlap it. It follows this element's layout and the shell window moving, and it lets go of the preview when it leaves the tree.

A WinUI FrameworkElement. The members here are the ones Winshell adds.

| Member | Type | Description |
| --- | --- | --- |
| `WindowThumbnail()` | constructor |  |
| `Peek` | bool |  |
| `Window` | [AppWindow](winshell-windows.md#appwindow)? |  |
| `PeekProperty` | DependencyProperty, static | While true, Windows shows this window at full size and hides the others. It stays off when the user turned peek off, and it ends when this element unloads or the window goes. |
| `WindowProperty` | DependencyProperty, static | The app window to preview, from Windows. Null shows nothing. |

## ColorScheme

Whether windows are light or dark. System follows Windows.

| Value | Number | Description |
| --- | --- | --- |
| `System` | 0 |  |
| `Light` | 1 |  |
| `Dark` | 2 |  |

## CaptureView

A live view of a CaptureSession. The frame is fitted and centered, keeping its aspect ratio. Its natural size is the frame's physical size divided by this window's scale. The frame is written once into the bitmap, on the thread that delivered it, so the session's loop is this window's. The view lets go of the session when it leaves the tree.

A WinUI Panel. The members here are the ones Winshell adds.

| Member | Type | Description |
| --- | --- | --- |
| `CaptureView()` | constructor |  |
| `Session` | [CaptureSession](winshell-capture.md#capturesession)? |  |
| `SessionProperty` | DependencyProperty, static | The capture to show. Null shows nothing. |

## Edges

These values combine.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 |  |
| `Top` | 1 |  |
| `Left` | 2 |  |
| `Right` | 4 |  |
| `Bottom` | 8 |  |

## FileWatch

Follows one file that may not exist yet, and calls back on the loop when it may have changed. A folder that is missing is watched from the nearest one that exists, and each folder created under it moves the watch one step closer. Nothing is called after Dispose.

| Member | Type | Description |
| --- | --- | --- |
| `FileWatch(string path, [IEventLoop](#ieventloop) loop, Action changed)` | constructor |  |
| `Dispose()` | void |  |

## IEventLoop

The shell's thread. Work from other threads is posted to it.

| Member | Type | Description |
| --- | --- | --- |
| `Now` | TimeSpan, read-only | Time since the loop started, for timers and animations. |
| `Post(Action action)` | void | Runs the action on the loop's thread. Safe to call from any thread. |
| `RequestFrame(Action action)` | void | Runs the action before the next frame is drawn, once per request. |
| `Schedule(TimeSpan delay, Action action)` | IDisposable | Runs the action once after the delay, on the loop's thread. Dispose to cancel. |

## IIpcTargets

What the `ipc` commands reach in a running shell. Every member runs on the shell's thread.

| Member | Type | Description |
| --- | --- | --- |
| `Call(string target, string function, IReadOnlyList of string args)` | string | Calls a function. Returns its result as text, or throws IpcException with a message for the CLI. |
| `Describe()` | IReadOnlyList of [IpcTargetView](#ipctargetview) | Every target with the functions, properties and signals `ipc show` lists. |
| `GetProperty(string target, string property)` | string |  |
| `Listen(string target, string signal, Action of string onSignal)` | IDisposable | Calls back with a signal's argument as text each time it fires, until disposed. |

## IpcException

A refused IPC request. The message goes to the client as it is.

An exception.

| Member | Type | Description |
| --- | --- | --- |
| `IpcException(string message)` | constructor |  |

## IpcFunctionView

A function `ipc show` can call.

| Member | Type | Description |
| --- | --- | --- |
| `IpcFunctionView(string Name, IReadOnlyList of [IpcParameterView](#ipcparameterview) Parameters, string Returns)` | constructor |  |
| `Name` | string, init |  |
| `Parameters` | IReadOnlyList of [IpcParameterView](#ipcparameterview), init |  |
| `Returns` | string, init |  |

## IpcParameterView

One parameter of a function or a signal `ipc show` can list.

| Member | Type | Description |
| --- | --- | --- |
| `IpcParameterView(string Name, string? Type)` | constructor |  |
| `Name` | string, init |  |
| `Type` | string?, init |  |

## IpcPropertyView

A property `ipc show` can read.

| Member | Type | Description |
| --- | --- | --- |
| `IpcPropertyView(string Name, string Type)` | constructor |  |
| `Name` | string, init |  |
| `Type` | string, init |  |

## IpcSignal

A signal of a ShellIpc target. Use it on the shell's thread.

| Member | Type | Description |
| --- | --- | --- |
| `Name` | string, read-only |  |
| `Emit(string value = "")` | void | Tells every `winshell ipc wait` and `listen` following this signal, with value. |

## IpcSignalView

A signal `ipc wait` and `ipc listen` can follow.

| Member | Type | Description |
| --- | --- | --- |
| `IpcSignalView(string Name, IReadOnlyList of [IpcParameterView](#ipcparameterview) Parameters)` | constructor |  |
| `Name` | string, init |  |
| `Parameters` | IReadOnlyList of [IpcParameterView](#ipcparameterview), init |  |

## IpcTargetView

One IPC target, as `ipc show` knows it. `ipc show -j` prints these records as they are.

| Member | Type | Description |
| --- | --- | --- |
| `IpcTargetView(string Target, IReadOnlyList of [IpcFunctionView](#ipcfunctionview) Functions, IReadOnlyList of [IpcPropertyView](#ipcpropertyview) Properties, IReadOnlyList of [IpcSignalView](#ipcsignalview) Signals)` | constructor |  |
| `Functions` | IReadOnlyList of [IpcFunctionView](#ipcfunctionview), init |  |
| `Properties` | IReadOnlyList of [IpcPropertyView](#ipcpropertyview), init |  |
| `Signals` | IReadOnlyList of [IpcSignalView](#ipcsignalview), init |  |
| `Target` | string, init |  |

## Log

Writes messages as ` WARN category: message`.

| Member | Type | Description |
| --- | --- | --- |
| `Log()` | constructor |  |
| `MinimumLevel` | [LogLevel](#loglevel) |  |
| `Sink` | Action of [LogLevel](#loglevel), string, string? | Where messages go. The default writes to the standard error stream. |
| `Default` | [Log](#log), static |  |
| `Debug(string category, string message)` | void |  |
| `Error(string category, string message)` | void |  |
| `Info(string category, string message)` | void |  |
| `Warn(string category, string message)` | void |  |
| `Write([LogLevel](#loglevel) level, string category, string message)` | void |  |

## LogLevel

| Value | Number | Description |
| --- | --- | --- |
| `Debug` | 0 |  |
| `Info` | 1 |  |
| `Warn` | 2 |  |
| `Error` | 3 |  |
| `Fatal` | 4 |  |

## ObservableObject

The base of what a service exposes. A change is raised on the thread that made it, which for a service is its event loop, so XAML bindings and Reactor can read it.

| Member | Type | Description |
| --- | --- | --- |
| `PropertyChanged` | PropertyChangedEventHandler |  |

## PanelExclusionMode

How a panel treats the edges other bars reserve.

| Value | Number | Description |
| --- | --- | --- |
| `Normal` | 0 | Stays inside the work area, and reserves ExclusiveZone if that is set. |
| `Ignore` | 1 | Covers the whole screen, over the taskbar and other bars. |

## PixelImage

A picture in memory: BGRA, one byte a channel, top row first, with no gap between rows. Nothing writes to the pixels after this is made, so the constructor keeps `bgra` rather than copying it, and a UI can hold on to one.

| Member | Type | Description |
| --- | --- | --- |
| `PixelImage(int width, int height, byte[] bgra, bool premultiplied = false)` | constructor |  |
| `Bgra` | ReadOnlySpan of byte, read-only |  |
| `Height` | int, read-only |  |
| `Premultiplied` | bool, read-only |  |
| `Width` | int, read-only |  |
| `EncodePng()` | byte[] | The picture as a PNG file. Blocks while Windows encodes it. |

## ScreenInfo

A monitor, as the platform reports it. Coordinates are logical pixels on the virtual desktop: a monitor keeps its physical top-left corner and its size is divided by its scale, so monitors of mixed DPI never overlap.

| Member | Type | Description |
| --- | --- | --- |
| `ScreenInfo(string Id, string Name, string Model, string Manufacturer, string SerialNumber, Rect Geometry, Rect WorkArea, double DevicePixelRatio, double PhysicalWidthMm, double PhysicalHeightMm, int RefreshRate, bool IsPrimary)` | constructor |  |
| `DevicePixelRatio` | double, init |  |
| `Geometry` | Rect, init |  |
| `Id` | string, init |  |
| `IsPrimary` | bool, init |  |
| `Manufacturer` | string, init |  |
| `Model` | string, init |  |
| `Name` | string, init |  |
| `PhysicalHeightMm` | double, init |  |
| `PhysicalWidthMm` | double, init |  |
| `RefreshRate` | int, init |  |
| `SerialNumber` | string, init |  |
| `WorkArea` | Rect, init |  |

## ShellIpc

What `winshell ipc` reaches in a C# configuration: functions, properties and signals, by target. A function's own types declare what it takes and returns, and only string, int, bool, double, float and Color cross the pipe, as with an `IpcHandler`. The CLI's text is parsed into those types and nothing is ever evaluated. Use this on the shell's thread.

| Member | Type | Description |
| --- | --- | --- |
| `ShellIpc([Log](#log) log)` | constructor |  |
| `Call(string target, string function, IReadOnlyList of string args)` | string |  |
| `Describe()` | IReadOnlyList of [IpcTargetView](#ipctargetview) |  |
| `GetProperty(string target, string property)` | string |  |
| `Handle(string target, string function, Delegate handler)` | IDisposable | Lets `winshell ipc call` run handler, such as `(string page) => Open(page)`. Dispose the result to take it away. A later handler for the same function replaces this one. ArgumentException when the handler takes or returns a type IPC can't pass. |
| `Listen(string target, string signal, Action of string onSignal)` | IDisposable |  |
| `Property<T>(string target, string property, Func of T read)` | IDisposable | Lets `winshell ipc prop get` read a value. Dispose the result to take it away. ArgumentException when T is a type IPC can't pass. |
| `Signal(string target, string signal)` | [IpcSignal](#ipcsignal) | A signal `winshell ipc wait` and `listen` can follow. |

## TomlCommentAttribute

The comment written above a key the file does not have yet. A line break starts another comment line.

An attribute.

| Member | Type | Description |
| --- | --- | --- |
| `TomlCommentAttribute(string text)` | constructor |  |
| `Text` | string, read-only |  |

## TomlFile

A TOML file a user can edit by hand, as an object. A type derives from this and has one public read-write property per key, the key being the property's name with a lowercase first letter. Its type is string, bool, int, long, double, IReadOnlyDictionary of string and object for a table, IReadOnlyList of any of those for an array or an array of tables, or object for anything. A property that holds null until the file has the key is nullable. The file is read in the constructor, so a default belongs in the property's initializer. After that the object follows the file, and assigning a property writes it, on the loop that was passed in. Everything else in the file stays, as TomlDocument says. A write reads the file again and puts only assigned values into it, and the keys it does not have yet. When that read fails or does not parse, nothing is written and a warning is logged.

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
| `None` | 0 |  |
| `Mica` | 1 |  |
| `Acrylic` | 2 |  |
| `MicaAlt` | 3 |  |

## WindowCorners

| Value | Number | Description |
| --- | --- | --- |
| `Default` | 0 |  |
| `Square` | 1 |  |
| `Round` | 2 |  |
| `RoundSmall` | 3 |  |

