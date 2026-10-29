# Winshell

The `Winshell` module is the core of a configuration: the `Shell` singleton, the root object of a shell, its windows (panels, floating windows and popup windows), the screens they go on, and the small helpers a shell needs, such as a clock, a month grid and models that keep their objects. Every configuration imports it with `import Winshell`.

A configuration that started as a Quickshell one finds the old names in [Migrating from Quickshell](../migrating-from-quickshell.md). The default shell shows these types in use: see [the taskbar](../taskbar.md), [Winshell Settings](../settings.md) and [notifications and the calendar](../notifications.md). Tray menus, which `MenuAnchor` opens, are in [the notification area](../tray.md).

## Shell

The `Shell` singleton: the screens, the clipboard, reloads, processes and folders of this shell. Use it by name, as in `Shell.screens`. It mirrors `watchFiles`, `workingDirectory` and `lastWindowClosed()` from [ShellSettings](#shellsettings).

A few of its values come from `//@ pragma` comments at the top of the root file, before the first line of code: `ShellId`, `AppId`, `DataDir`, `StateDir`, `CacheDir`, and `Env NAME=value`, which sets an environment variable for the shell. An `IconTheme` pragma is read but has no effect.

| Member | Type | Description |
| --- | --- | --- |
| `screens` | list of [ShellScreen](#shellscreen), read-only | The connected screens, primary screen first, then by position. Changes when a screen is connected, removed or rearranged. |
| `shellDir` | string, read-only | The folder of the shell's root file, `shell.qml`. |
| `shellRoot` | string, read-only | The same folder as `shellDir`. |
| `configDir` | string, read-only | The same folder as `shellDir`. |
| `dataDir` | string, read-only | This shell's data folder: `%LOCALAPPDATA%\winshell\data\by-shell\<shellId>`, or what `//@ pragma DataDir` names. Reading it creates the folder. Empty when the shell is not run by `winshell.exe`. |
| `stateDir` | string, read-only | This shell's state folder: `%LOCALAPPDATA%\winshell\state\by-shell\<shellId>`, or what `//@ pragma StateDir` names. Reading it creates the folder. |
| `cacheDir` | string, read-only | This shell's cache folder: `%LOCALAPPDATA%\winshell\cache\by-shell\<shellId>`, or what `//@ pragma CacheDir` names. Reading it creates the folder. |
| `configRoot` | string, read-only | The folder configurations are loaded from, where a shell keeps the user's own choices: `%APPDATA%\winshell`, or what `WINSHELL_CONFIG_DIR` names. Empty when the shell is not run by `winshell.exe`. |
| `logPath` | string, read-only | This instance's log file, which `winshell log` prints. Empty when nothing writes one. |
| `version` | string, read-only | The Winshell release, as `winshell --version` prints it. |
| `processId` | int, read-only | The Windows process id of this instance. |
| `instanceId` | string, read-only | This instance's id, as `winshell list` shows it and `--id` takes it. |
| `shellId` | string, read-only | The shell id: `//@ pragma ShellId`, or a hash of the root file's path. |
| `appId` | string, read-only | The application id: `winshell`, unless `//@ pragma AppId` or `WINSHELL_APP_ID` sets another. |
| `launchTime` | date, read-only | When this instance started. |
| `workingDirectory` | string | The process's current folder. `execDetached()` starts programs there unless told otherwise. Setting it changes the folder. |
| `watchFiles` | bool | Whether the shell reloads, 100 ms after a `.qml` or `.js` file or a `qmldir` changes in a folder it loaded files from. Default true; always false while `WINSHELL_DISABLE_FILE_WATCHER` is set. |
| `clipboardText` | string | The text on the Windows clipboard. Setting it copies the text. |
| `env(variable)` | method, returns string | The value of an environment variable, or null when it is not set. |
| `execDetached(context)` | method | Starts a program and does not wait for it. `context` is a list such as `["notepad", "a.txt"]`, or an object with `command`, and optionally `environment`, `clearEnvironment` and `workingDirectory`. The program is found on `PATH` with `PATHEXT`; `.cmd` and `.bat` files run through `cmd`. A `.ps1` file runs through `pwsh` when that is on `PATH`, otherwise `powershell`, otherwise `powershell.exe`, with `-NoProfile -ExecutionPolicy Bypass -File`. No console window opens. |
| `iconPath(icon, checkOrFallback)` | method, returns string | An `image://icon/` URL for a named icon, a file, or a program. With `true` as the second argument, returns an empty string when the icon is not found; with a string, returns that icon's URL instead. |
| `hasThemeIcon(icon)` | method, returns bool | Whether `iconPath()` can find the icon. |
| `shellPath(path)` | method, returns string | `path` joined to `shellDir`. |
| `configPath(path)` | method, returns string | `path` joined to `shellDir`, as `shellPath()` does. |
| `dataPath(path)` | method, returns string | `path` joined to `dataDir`. |
| `statePath(path)` | method, returns string | `path` joined to `stateDir`. |
| `cachePath(path)` | method, returns string | `path` joined to `cacheDir`. |
| `reload(hard)` | method | Reloads the configuration once the current handler returns. By default, the new generation takes over the old windows and the values of [PersistentProperties](#persistentproperties); with `hard` true, it starts fresh. |
| `inhibitReloadPopup()` | method | Hides Winshell's notice for the reload that just failed. Call it in `onReloadFailed`; the next reload shows the notice again. A window that failed still shows its own notice. |
| `hasVersion(major, minor, features)` | method, returns bool | True when this Winshell release is at least `major`.`minor` and every named unreleased feature is present. A newer release includes those features. None are declared, so a feature list matches only on a newer release. |
| `hasQtVersion(major, minor)` | method, returns bool | True when the Qt Quick language level the runtime implements, 6.10, is at least `major`.`minor`. |
| `lastWindowClosed()` | signal | Emitted when the shell's last shown window closes or hides. |
| `reloadCompleted()` | signal | Emitted by the new generation after each load, including the first. |
| `reloadFailed(errorString)` | signal | Emitted when a reload fails, with the error. The shell keeps running the generation it had, and Winshell shows a notice with the error unless the handler calls `inhibitReloadPopup()`. |

## ShellRoot

The root object of a shell: `shell.qml` is a `ShellRoot { }` that holds the shell's windows and other objects. Its default property is `children`. Has the members of [Scope](#scope).

| Member | Type | Description |
| --- | --- | --- |
| `settings` | [ShellSettings](#shellsettings), read-only | The process settings object. Every `ShellRoot` and the `Shell` singleton share it. |

## ShellSettings

The `settings` of a `ShellRoot`: one object per process, shared with the `Shell` singleton, which mirrors each member. Reach it as `ShellRoot.settings`; it cannot be created.

| Member | Type | Description |
| --- | --- | --- |
| `watchFiles` | bool | Whether the shell reloads when one of its files changes. Default true; always false while `WINSHELL_DISABLE_FILE_WATCHER` is set. |
| `workingDirectory` | string | The process's current folder. Setting it changes the folder. |
| `lastWindowClosed()` | signal | Emitted when the shell's last shown window closes or hides. |

## Scope

A non-visual holder for objects and windows, created as `Scope { }`. Its default property is `children`. On a reload, each child takes over the old child at the same position. Has the members of [Reloadable](#reloadable).

| Member | Type | Description |
| --- | --- | --- |
| `children` | list of objects, read-only | The objects the scope holds. |

## Singleton

The root object of a file that starts with `pragma Singleton`. The file's name becomes a singleton every file of the shell can use, as `Preferences.qml` is in the default shell. Its default property is `children`. Has the members of [Scope](#scope).

| Member | Type | Description |
| --- | --- | --- |

## Reloadable

The base of the objects that survive a reload: windows, scopes, `Variants`, `LazyLoader` and `PersistentProperties`. After a reload, each new object gets the matching object of the old generation, so windows are reused and state is carried over. A direct child of a `ShellRoot` or `Scope` matches the old child at the same position. Elsewhere, as inside a window, an object matches the old one with the same `reloadableId` under the same parent. It cannot be created.

| Member | Type | Description |
| --- | --- | --- |
| `reloadableId` | string | The name this object is matched by after a reload. Empty by default. |

## Variants

Makes one instance of its delegate for each value in `model`, given to the instance as `modelData`; declare `required property var modelData` to read it. An instance stays while its value is in the model, and after a reload an instance takes over the old instance made for the same value; screens match by screen. Create it as `Variants { }`; its default property is `delegate`. Has the members of [Reloadable](#reloadable).

| Member | Type | Description |
| --- | --- | --- |
| `model` | var | The values: a JavaScript array, a list such as `Shell.screens`, or an [ObjectModel](#objectmodel). A value given twice is used once. |
| `delegate` | Component | What to make for each value. |
| `instances` | list of objects, read-only | The instances, in the model's order. |

## LazyLoader

Creates its component when it is made active, and destroys the object when it is made inactive. Create it as `LazyLoader { }`; its default property is `component`. Has the members of [Reloadable](#reloadable).

| Member | Type | Description |
| --- | --- | --- |
| `component` | Component | What to create. |
| `source` | string | A QML file to create instead of `component`, relative to the file that declares the loader. |
| `active` | bool | Whether the object exists. Default false. Setting it to true creates it; false destroys it. |
| `activeAsync` | bool | Setting it to true creates the object on a later turn of the event loop instead of at once. Reads the same as `active`. |
| `loading` | bool | True while the object waits to be created. Setting it to true starts creating it on a later turn of the event loop; setting it to false does nothing. |
| `item` | object, read-only | The created object, or null. |

## PersistentProperties

An object whose declared properties keep their values across reloads, as `PersistentProperties { property bool open: false }`. After a reload it takes each writable property's value from the old object it matches; see [Reloadable](#reloadable) for how objects match. Has the members of [Reloadable](#reloadable).

| Member | Type | Description |
| --- | --- | --- |
| `loaded()` | signal | Emitted after each load, including the first, once the values are in place. |
| `reloaded()` | signal | Emitted after a reload that took values from an old object, before `loaded()`. |

## ShellScreen

One screen. The object stays the same while the screen is connected. Reach it from `Shell.screens` or a window's `screen`; it cannot be created. Positions and sizes are in logical pixels: a screen keeps its physical top-left corner, and its size is divided by its scale ([ADR-0006](../adr/0006-screen-coordinates-keep-the-physical-corner.md)).

| Member | Type | Description |
| --- | --- | --- |
| `name` | string, read-only | The Windows device name, such as `DISPLAY1`. |
| `model` | string, read-only | The monitor's name in Windows' device list, often `Generic PnP Monitor`. |
| `manufacturer` | string, read-only | Meant to be the three-letter PnP manufacturer code, such as `AUS`. Empty in this release. |
| `serialNumber` | string, read-only | Meant to be the monitor's serial part of its device id. Reads `?` in this release. |
| `x` | real, read-only | The left edge on the virtual desktop. |
| `y` | real, read-only | The top edge on the virtual desktop. |
| `width` | real, read-only | The width in logical pixels. |
| `height` | real, read-only | The height in logical pixels. |
| `workArea` | rect, read-only | A Windows extension: the part of the screen windows maximize into, without the taskbar and the zones other app bars reserve, in the same logical coordinates as `x` and `y`. |
| `devicePixelRatio` | real, read-only | The screen's scale: physical pixels per logical pixel, such as 1.5 at 144 DPI. |
| `physicalPixelDensity` | real, read-only | Physical pixels per millimeter. Winshell does not read the monitor's size, so this is 96 DPI times the scale, about 3.78 × `devicePixelRatio`. |
| `logicalPixelDensity` | real, read-only | Logical pixels per millimeter. Always 96 DPI, about 3.78, for the same reason. |
| `orientation` | int, read-only | `Qt.LandscapeOrientation` (2) when the screen is at least as wide as it is tall, else `Qt.PortraitOrientation` (1). |
| `primaryOrientation` | int, read-only | The same as `orientation`. |
| `refreshRate` | int, read-only | The refresh rate in hertz, or 60 when Windows does not report one. |
| `toString()` | method, returns string | `ShellScreen(DISPLAY1)`, with the screen's name. |

## ShellWindow

`ShellWindow` has two roles. It is the base of [PanelWindow](#panelwindow), [FloatingWindow](#floatingwindow) and [PopupWindow](#popupwindow), so every member below is a member of each of them; it cannot be created itself. A window's default property is `data`: items declared in it go into its content item. Windows also have the members of [Reloadable](#reloadable).

It is also an attached object on any item: `ShellWindow.window` is the window the item is in. The attached object has `window`, `contentItem`, `itemPosition()`, `itemRect()` and `mapFromItem()`; it gives null, or an empty point or rect, for an item that is not in a window. Sizes and positions are in logical pixels.

| Member | Type | Description |
| --- | --- | --- |
| `window` | [ShellWindow](#shellwindow), read-only | Attached only: the window the item is in, or null. |
| `contentItem` | Item, read-only | The root item of the window, which fills it. |
| `data` | list, read-only | The window's items and other objects; its default property. |
| `width` | real | The window's width as Windows sized it. Setting it sets `implicitWidth`. |
| `height` | real | The window's height as Windows sized it. Setting it sets `implicitHeight`. |
| `implicitWidth` | real | The width the window asks for. Default 100, at least 1. |
| `implicitHeight` | real | The height the window asks for. Default 100, at least 1. |
| `visible` | bool | Whether the window is shown. Default true. Closing the window, as with Alt+F4, sets it to false. |
| `backingWindowVisible` | bool, read-only | Whether the native window is on screen now. |
| `color` | color | What the window clears to before drawing its items. Default white. Make it `"transparent"` or translucent to let a [WindowEffects](#windoweffects) backdrop show through. |
| `mask` | [Region](#region) | The part of the window that takes clicks; clicks elsewhere go to the window below. Null, the default, is the whole window. An empty region lets every click through. Windows also clips drawing to a non-empty mask, so the parts outside it are not shown. |
| `surfaceFormat` | group: `opaque` (bool) | Set the field, as in `surfaceFormat.opaque: true`; the group itself can't be replaced. Whether the window is opaque, which by default follows `color`. The Windows backend does not use it. |
| `screen` | [ShellScreen](#shellscreen) | The screen the window is on. Setting it puts a panel or a new floating window on that screen. Unset, it is the screen the window is shown on, or the primary screen. |
| `devicePixelRatio` | real, read-only | The scale of the screen the window is on. |
| `updatesEnabled` | bool | Whether the window draws its items. Default true. While false, it shows only `color`. |
| `windowTransform` | object, read-only | Changes whenever the window moves or resizes. Bind to it to recompute something that depends on the window's place. |
| `activeFocusItem` | Item, read-only | The item in this window that has keyboard focus, or null. |
| `itemPosition(item)` | method, returns point | The top-left corner of `item` in the window's coordinates. |
| `itemRect(item)` | method, returns rect | The bounds of `item` in the window's coordinates. |
| `mapFromItem(item, xOrPoint, y, width, height)` | method, returns var | Maps a point (`x, y`, or a point) or a rectangle (`x, y, width, height`, or a rect) from `item` to the window's coordinates. |
| `closed()` | signal | Emitted when the window is closed, as with Alt+F4. The window hides; it is not destroyed. |
| `resourcesLost()` | signal | Not emitted on Windows. |
| `windowConnected()` | signal | Emitted when the window gets its native window, after each load or reload. |

## PanelWindow

A frameless window attached to edges of a screen, created as `PanelWindow { }`. A panel can reserve its edge through a Windows app bar, so maximized windows stop at it, as the taskbar does. A panel anchored to two opposite edges spans the screen between them; on an axis with no anchored edge, it is centered. If its screen is disconnected, it moves to the primary screen until the screen comes back. Has every member of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `anchors` | group: `top`, `bottom`, `left`, `right` (bool) | The screen edges the panel is attached to. Set each edge, as in `anchors.top: true`; the group itself can't be replaced. Default none. |
| `margins` | group: `top`, `bottom`, `left`, `right` (real) | The distance from each anchored edge, in logical pixels. Set each edge, as in `margins.top: 8`; the group itself can't be replaced. |
| `exclusiveZone` | real | The space reserved at the panel's edge, in logical pixels, in `Normal` mode. Setting it switches `exclusionMode` to `Normal`. |
| `exclusionMode` | [ExclusionMode](#exclusionmode) | How the panel reserves its edge. Default `Auto`. |
| `focusable` | bool | Whether the panel takes the keyboard when an item in it takes focus. A panel that becomes focusable while an item in it has focus takes the keyboard then. Default false. |
| `aboveWindows` | bool | Whether the panel stays above other windows, except while an app is fullscreen. Default true. False puts it behind other windows. |

Enum values: `Normal`, `Ignore`, `Auto`. These are the [ExclusionMode](#exclusionmode) values, so `PanelWindow.Ignore` is `ExclusionMode.Ignore`.

A panel reserves space only at a single edge: anchor one edge, or one edge and both edges across it, such as `top`, `left` and `right`. It reserves nothing while it is hidden.

## FloatingWindow

A normal desktop window with a title bar, created as `FloatingWindow { }`. It opens centered in the work area of its `screen`, and has a Mica backdrop unless [WindowEffects](#windoweffects) sets another. Has every member of [ShellWindow](#shellwindow).

| Member | Type | Description |
| --- | --- | --- |
| `title` | string | The title in the title bar and on the taskbar. |
| `minimumSize` | size | The smallest size the user can resize to, in logical pixels. 0 means no limit, as by default. |
| `maximumSize` | size | The largest size the user can resize to, in logical pixels. 0 means no limit, as by default. |
| `maximized` | bool | Whether the window is maximized. Setting it maximizes or restores the window; it follows the user. |
| `minimized` | bool | Whether the window is minimized. Setting it minimizes or restores the window; it follows the user. |
| `fullscreen` | bool | Not applied on Windows in this release, and set back to false when the window moves or resizes. |
| `parentWindow` | [ShellWindow](#shellwindow) | Stored, but not used on Windows in this release. |
| `activate()` | method | Shows the window, restores it when it is minimized, and brings it to the front with keyboard focus. Windows allows this after a click or hotkey in the shell; asked from elsewhere, Windows may only flash its taskbar button. |
| `startSystemMove()` | method | Starts moving the window with the mouse, as dragging its title bar does. Call it from a mouse press. |
| `startSystemResize(edges)` | method | Starts resizing the window with the mouse from `edges`, an [Edges](#edges) value such as `Edges.Bottom \| Edges.Right`. Call it from a mouse press. |

## PopupWindow

A frameless window placed next to its parent window, created as `PopupWindow { }`, for menus and flyouts. It is placed at an [anchor](#popupanchor) in its parent window and kept inside that screen's work area. It is hidden by default and shows only while its parent window is shown. Has every member of [ShellWindow](#shellwindow); `screen` and `visible` work as below.

| Member | Type | Description |
| --- | --- | --- |
| `anchor` | [PopupAnchor](#popupanchor), read-only | Where the popup window attaches, as `anchor.item: button`. |
| `parentWindow` | [ShellWindow](#shellwindow) | The window it attaches to. The same as `anchor.window`. |
| `relativeX` | real | The anchor's left edge in the parent window. The same as `anchor.rect.x`. |
| `relativeY` | real | The anchor's top edge in the parent window. The same as `anchor.rect.y`. |
| `grabFocus` | bool | Whether it takes the keyboard when it opens, and closes on Escape or a click outside it. Default false: it never takes the keyboard. |
| `screen` | [ShellScreen](#shellscreen) | The parent window's screen. Setting it does nothing. |
| `visible` | bool | Whether the popup window is open. Default false. Setting it to true emits `anchor.anchoring()` first. |

## PopupAnchor

Where a popup window attaches to its parent window. Reach it as `PopupWindow.anchor` or `MenuAnchor.anchor`; it cannot be created. The anchor is a rectangle in the parent window: an item's bounds, or `rect`. `edges` picks a point on it, and the popup window grows from that point toward `gravity`, then flips, slides or shrinks as `adjustment` allows to stay in the work area.

| Member | Type | Description |
| --- | --- | --- |
| `window` | [ShellWindow](#shellwindow) | The parent window. With `item` set, the item's window. Setting it clears `item`. |
| `item` | Item | An item whose bounds are the anchor rectangle. Setting it clears `window`. |
| `rect` | group: `x`, `y`, `width`, `height` (real) | The anchor rectangle in the window, 1 by 1 at the corner by default. Set each field, as in `anchor.rect.x: 12`; the group itself can't be replaced. With `item` set, `x` and `y` move the item's bounds, and `width` and `height` are not used. |
| `margins` | group: `left`, `top`, `right`, `bottom` (real) | Grows the anchor rectangle outward on each side, in logical pixels, to leave a gap. Set each side, as in `anchor.margins.top: 8`; the group itself can't be replaced. |
| `edges` | [Edges](#edges) | The point on the anchor rectangle: an edge or a corner. No edge on an axis means its middle. Default `Edges.Top \| Edges.Left`. |
| `gravity` | [Edges](#edges) | The direction the popup window grows from that point. Default `Edges.Bottom \| Edges.Right`. |
| `adjustment` | [PopupAdjustment](#popupadjustment) | What may change to keep the popup window on its screen. Default `PopupAdjustment.All`. |
| `updateAnchor()` | method | Places the popup window again, after something the anchor does not watch moved. |
| `anchoring()` | signal | Emitted when the popup window is about to open, so a handler can update the anchor first. |

## WindowEffects

An attached object for a window, as `WindowEffects.backdrop: WindowEffects.Mica` on a `PanelWindow`, `FloatingWindow` or `PopupWindow`: the Windows 11 material behind it and the shape of its corners. It has no effect on an item that is not a window. A backdrop shows through where the window's `color` is transparent.

| Member | Type | Description |
| --- | --- | --- |
| `backdrop` | int | The material: `WindowEffects.None`, `Mica`, `Acrylic` or `MicaAlt`. Default `None`, or `Mica` for a floating window. |
| `corners` | int | The corners: `WindowEffects.DefaultCorners` (Windows decides, the default), `SquareCorners`, `RoundCorners` or `SmallRoundCorners`. |

Enum values: `None`, `Mica`, `Acrylic`, `MicaAlt`, `DefaultCorners`, `SquareCorners`, `RoundCorners`, `SmallRoundCorners`

## Region

A shape for a window's `mask`, created as `Region { }`. Child regions in `regions`, its default property, are combined with it in order, each by its own `intersection`. Coordinates are in the window's content item.

| Member | Type | Description |
| --- | --- | --- |
| `item` | Item | An item whose bounds are the region. It follows the item as it moves or resizes. |
| `x` | real | The left edge, when `item` is not set. |
| `y` | real | The top edge, when `item` is not set. |
| `width` | real | The width, when `item` is not set. Default 0. |
| `height` | real | The height, when `item` is not set. Default 0. |
| `shape` | [RegionShape](#regionshape) | `RegionShape.Rect`, the default, or `RegionShape.Ellipse`, which fills the bounds. |
| `intersection` | [Intersection](#intersection) | How this region combines with its parent region. Default `Combine`. On the window's mask itself, anything but `Combine` applies the region to the whole window, so `Subtract` makes every part but the region take clicks. |
| `radius` | real | Not used in this release. |
| `topLeftRadius` | real | Not used in this release. |
| `topRightRadius` | real | Not used in this release. |
| `bottomLeftRadius` | real | Not used in this release. |
| `bottomRightRadius` | real | Not used in this release. |
| `regions` | list of [Region](#region), read-only | The child regions. |
| `changed()` | signal | Emitted when the region or a child region changes. |

Enum values: `Rect`, `Ellipse`. These are the [RegionShape](#regionshape) values.

## FocusGrab

Watches for clicks outside a set of windows, as `FocusGrab { }`: while active, a click outside every listed window clears the grab. Use it to close a menu or flyout made of panels when the user clicks elsewhere. A [PopupWindow](#popupwindow) with `grabFocus` does this on its own.

| Member | Type | Description |
| --- | --- | --- |
| `active` | bool | Set it to true to start the grab. It starts once one of `windows` is shown, and becomes false when a click lands outside them all or none is shown. |
| `windows` | list of [ShellWindow](#shellwindow), read-only | The windows a click may land in, as `windows: [panel, popup]`. |
| `cleared()` | signal | Emitted when a click outside the windows, or every window hiding, ends the grab. Setting `active` to false does not emit it. |

## Session

The `Session` singleton: the Windows session the shell runs in. `lock()` asks Windows to lock it, and Windows' own lock screen takes over. Winshell never draws a lock screen or asks for the Windows password ([ADR-0008](../adr/0008-windows-owns-the-lock-screen.md)).

| Member | Type | Description |
| --- | --- | --- |
| `lock()` | method, returns bool | Asks Windows to lock the session, as Win+L does. Returns whether Windows took the request; the lock itself happens a moment later, in Windows. |

## Startup

The `Startup` singleton: whether Winshell runs when the current user signs in. The entry is a `Winshell` value under the user's `Run` key, which Windows' Startup apps page lists and can turn off. Nothing is registered until the shell sets `runAtSignIn`, and Winshell never changes what that page chose. Winshell Settings has a Startup page for it; see [Winshell Settings](../settings.md) and [where files live](../development.md#where-files-live).

| Member | Type | Description |
| --- | --- | --- |
| `available` | bool, read-only | Whether this shell can register itself: false when it does not run as `winshell.exe`. |
| `runAtSignIn` | bool | Whether this Winshell is registered to run at sign-in. Setting it adds or removes the entry, and setting it when the entry starts another `winshell.exe` points the entry here. |
| `turnedOffInWindows` | bool, read-only | True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on. |
| `registeredElsewhere` | bool, read-only | True when the entry is there but starts Winshell another way: a `winshell.exe` in another folder, as after an update unzipped somewhere new, or other arguments. `runAtSignIn` is false until it is set. |
| `refresh()` | method | Reads the entry again, after the user may have changed it in Windows. |

## Diagnostics

The `Diagnostics` singleton: the checks `winshell doctor` prints, for the About page of Winshell Settings. `run()` runs them off the loop, and they only read. Without `winshell.exe` there is no doctor, and `available` is false. See [winshell doctor](../doctor.md).

| Member | Type | Description |
| --- | --- | --- |
| `available` | bool, read-only | Whether this shell can run the checks: false when it does not run as `winshell.exe`. |
| `running` | bool, read-only | Whether a run is in progress. A second `run()` while this is true does nothing. |
| `checks` | ObjectModel, read-only | The checks of the last run, in the order `winshell doctor` prints them. Each object has `id`, `title`, `status` (`pass`, `warn` or `fail`), `message` and `fix` (empty when the check passed). Empty before the first run. |
| `summary` | string, read-only | The last run counted, such as `8 passed, 2 warnings, 0 failed.`. Empty before the first run. |
| `run()` | method | Runs the checks. Does nothing when `available` is false or a run is already in progress. A run that cannot finish shows one failed check and the reason. |

## Updates

The `Updates` singleton: Winshell's own updates. A portable Winshell, the zip from GitHub Releases, checks that repository's releases, downloads a newer one and installs it when a start finds no other Winshell running. winget, once the package is accepted, installs that same portable build and is the only updater for that copy. While one Winshell is running, the downloaded update stays ready. A build from source never updates itself. A packaged Winshell, such as the Microsoft Store package once it is published, is updated by whatever installed it. Winshell Settings shows it on the About page; see [Winshell updates](../updates.md).

| Member | Type | Description |
| --- | --- | --- |
| `available` | bool, read-only | Whether this Winshell updates itself. |
| `packaged` | bool, read-only | Whether Winshell runs from an MSIX package, which its installer updates. |
| `winget` | bool, read-only | Whether this copy lives under winget's Packages or Links folder. winget updates it, and Winshell does not. |
| `automatic` | bool | Check by itself, 30 seconds after it is turned on and once a day after that, and download what it finds. Off until the shell turns it on. |
| `status` | [UpdateStatus](#updatestatus), read-only | Where the update is. An update downloaded before this run started is `Ready` from the start. |
| `version` | string, read-only | The release found: the latest one when up to date, or the one downloading or ready. Empty before a check, unless an update is already downloaded: that one is `Ready` at startup, and `version` is its release. |
| `error` | string, read-only | Why the last check, download or install failed, while `status` is `Failed`; or, while it is `Ready`, why `restart()` did not restart. |
| `check()` | method | Checks now, whether or not `automatic` is on, and downloads a newer release. Warns in the log when this Winshell does not update itself. |
| `restart()` | method | Starts Winshell over to install a `Ready` update. While another Winshell runs it does not restart and sets `error`. Warns in the log when no update is ready. |

## SystemClock

The time of day, updated at the start of each second, minute or hour, as `SystemClock { precision: SystemClock.Minutes }`. A clock that shows minutes wakes the shell once a minute.

| Member | Type | Description |
| --- | --- | --- |
| `enabled` | bool | Whether the clock updates. Default true. |
| `precision` | int | How often it updates: `SystemClock.Hours`, `Minutes` or `Seconds`, the default. Smaller parts read 0. |
| `date` | date, read-only | The local date and time, cut to `precision`. |
| `hours` | int, read-only | The hour, 0 to 23. |
| `minutes` | int, read-only | The minute, or 0 with `Hours` precision. |
| `seconds` | int, read-only | The second, or 0 unless the precision is `Seconds`. |

Enum values: `Hours`, `Minutes`, `Seconds`

## CalendarMonth

A Gregorian month grid that uses the user's regional weekday order and week numbering rule, created as `CalendarMonth { }`. It has six rows of seven days, and labels in the regional format. Bind `date` to a [SystemClock](#systemclock) to keep "today" current. See [the default notification center and calendar](../notifications.md#default-notification-center-and-calendar).

| Member | Type | Description |
| --- | --- | --- |
| `days` | ObjectModel of [CalendarDay](#calendarday), read-only | The 42 days of the grid, starting on the regional first day of the week. The objects stay the same as the month changes. |
| `weekdayLabels` | list of string, read-only | The shortest names of the seven weekdays, in grid order. |
| `weekNumbers` | list of int, read-only | The week number of each of the six rows, by the regional rule. Monday-first, four-day regions use ISO weeks. |
| `firstDayOfWeek` | int, read-only | The regional first day of the week: 0 for Sunday through 6 for Saturday. |
| `monthLabel` | string, read-only | The shown month and year, such as `October 2026`. |
| `dateLabel` | string, read-only | `date` as a long date. |
| `timeLabel` | string, read-only | `date` as a short time. |
| `shortDateLabel` | string, read-only | `date` as a short date. |
| `selectedDate` | date, read-only | The selected day. Today when the object is created. |
| `date` | date | Today: the local date and time the grid marks as today. The day the object is created, unless bound. |
| `monthOffset` | int | The shown month, in months from `date`'s month. Default 0. It stays within the years 2 to 9998. |
| `localeName` | string | A regional format name, such as `de-DE`. Empty means the process's current Windows regional format; invalid names are ignored. |
| `reset()` | method | Shows `date`'s month again and selects `date`'s day. |
| `selectDay(index)` | method | Selects the day at `index` in `days`. Other indexes are ignored. |

## CalendarDay

One day of a [CalendarMonth](#calendarmonth) grid, from its `days`. It cannot be created.

| Member | Type | Description |
| --- | --- | --- |
| `date` | date, read-only | The day. |
| `label` | string, read-only | The day of the month, as the grid shows it. |
| `fullDate` | string, read-only | The day as a long date, for screen readers. |
| `inMonth` | bool, read-only | Whether the day is in the shown month, not before or after it. |
| `isToday` | bool, read-only | Whether the day is the calendar's `date`. |
| `isSelected` | bool, read-only | Whether the day is the calendar's `selectedDate`. |

## ElapsedTimer

Measures time since it was created or last restarted, as `ElapsedTimer { }`. It starts when it is created.

| Member | Type | Description |
| --- | --- | --- |
| `elapsed()` | method, returns real | The time passed, in seconds. |
| `elapsedMs()` | method, returns real | The time passed, in milliseconds. |
| `elapsedNs()` | method, returns real | The time passed, in nanoseconds, to 100 ns. |
| `restart()` | method, returns real | Starts again and returns the time that had passed, in seconds. |
| `restartMs()` | method, returns real | Starts again and returns the time that had passed, in milliseconds. |
| `restartNs()` | method, returns real | Starts again and returns the time that had passed, in nanoseconds. |

## EasingCurve

An easing curve to evaluate in script, as `EasingCurve { curve.type: Easing.OutCubic }`.

| Member | Type | Description |
| --- | --- | --- |
| `curve` | group: `type`, `amplitude`, `overshoot`, `period`, `bezierCurve` | The curve, set as an animation's `easing` is, field by field: `curve.type: Easing.OutCubic`. The group itself can't be replaced. |
| `valueAt(x)` | method, returns real | The curve's value at progress `x`, which is kept between 0 and 1. |
| `interpolate(x, a, b)` | method, returns var | The value between `a` and `b` at progress `x` along the curve. `a` and `b` are numbers, points or rects. |

## ColorQuantizer

The prevalent colors of an image, as `ColorQuantizer { }`. It splits the image's pixels at the median of their widest channel `depth` times and averages each part, so it finds up to 2 to the power `depth` colors. The work runs in the background, and `colors` changes when it is done.

| Member | Type | Description |
| --- | --- | --- |
| `source` | string | The image: a path relative to the file, or a URL. |
| `depth` | real | How many times to split, from 0 to 8. Default 0, one average color. |
| `rescaleSize` | real | Decodes a larger image at about this many pixels on its shorter side, keeping its aspect ratio, which is faster. Default 0, the full image. |
| `imageRect` | var | A `Qt.rect()` of the image to read, in its pixels after scaling. Unset reads the whole image. |
| `colors` | list of color, read-only | The colors found. Empty while there is no source or the image cannot be read. |

## TransformWatcher

Signals when anything that places two items relative to each other changes, as `TransformWatcher { a: x; b: y }`: a position, size, scale or rotation of either item or of a parent between them, or a new parent.

| Member | Type | Description |
| --- | --- | --- |
| `a` | Item | The first item. |
| `b` | Item | The second item. |
| `commonParent` | Item | An item that holds both, where watching stops. Optional. |
| `transform` | var, read-only | Always null. Its change signal is what matters: bind to it, or handle `onTransformChanged`. |

## BoundComponent

An item that loads a QML file, as `BoundComponent { source: "Card.qml" }`. The properties declared on the `BoundComponent` are passed to the loaded object and kept in sync, and functions named after the object's signal handlers, such as `function onClicked()`, are connected to its signals. A loaded item fills the `BoundComponent`, whose implicit size follows the item's. It is an Item.

| Member | Type | Description |
| --- | --- | --- |
| `source` | string | The QML file, relative to the file that declares it. Changing it loads the new file. |
| `item` | object, read-only | The loaded object, or null. |

## ObjectModel

A list of objects that a service keeps up to date, such as `CalendarMonth.days`. Use it as the `model` of a `Repeater`, `ListView` or `Variants`. It cannot be created; see [ScriptModel](#scriptmodel) for one made in QML.

| Member | Type | Description |
| --- | --- | --- |
| `values` | list, read-only | The objects, in order. |
| `indexOf(obj)` | method, returns int | The index of `obj`, or -1. |
| `objectInsertedPre(object, index)` | signal | Emitted before `object` is inserted at `index`. |
| `objectInsertedPost(object, index)` | signal | Emitted after `object` is inserted at `index`. |
| `objectRemovedPre(object, index)` | signal | Emitted before `object` is removed from `index`. |
| `objectRemovedPost(object, index)` | signal | Emitted after `object` is removed from `index`. |

## ScriptModel

A model made from a script's array, as `ScriptModel { values: list.filter(...) }`. When the array is computed again, it keeps the entries that are still in it, so delegates for them are not made again. Set `values` to the array. Has the members of [ObjectModel](#objectmodel).

| Member | Type | Description |
| --- | --- | --- |
| `objectProp` | string | A property that identifies a JavaScript object, such as `id`. When set, two objects are the same entry when it is equal. |
| `comparisonMode` | [ObjectComparison](#objectcomparison) | How JavaScript objects match without `objectProp`. Default `Structure`. Takes effect the next time `values` changes. |

## MenuHandle

A menu QML can open or read, which a service gives, such as `SystemTrayItem.menu` from `Winshell.Tray`. Open it with [MenuAnchor](#menuanchor), or read its entries with [MenuOpener](#menuopener). A tray icon's menu belongs to its app, which draws it itself, so it has no entries to read; see [the notification area](../tray.md). It cannot be created.

| Member | Type | Description |
| --- | --- | --- |
| `menuChanged()` | signal | Emitted when the menu's entries change. |

## MenuEntry

One entry of a menu, from `MenuOpener.children`. It is also a [MenuHandle](#menuhandle) for its submenu. No service in this release gives a menu with readable entries, so none exist yet. It cannot be created.

| Member | Type | Description |
| --- | --- | --- |
| `enabled` | bool, read-only | Whether the entry can be chosen. |
| `isSeparator` | bool, read-only | Whether the entry is a separator line. |
| `hasChildren` | bool, read-only | Whether the entry opens a submenu. |
| `checkState` | int, read-only | `Qt.Unchecked`, `Qt.PartiallyChecked` or `Qt.Checked`. |
| `text` | string, read-only | The entry's text. |
| `buttonType` | [MenuButtonType](#menubuttontype), read-only | Whether the entry has a check box or a radio button. |
| `icon` | string, read-only | The entry's icon, or empty. |
| `display(parentWindow, relativeX, relativeY)` | method | Shows the entry's submenu as a Windows menu at a point in a window. |
| `triggered()` | signal | Emitted when the entry is chosen. |

## MenuOpener

The entries of a menu, to draw the menu in QML, as `MenuOpener { menu: someMenu }`.

| Member | Type | Description |
| --- | --- | --- |
| `menu` | [MenuHandle](#menuhandle) | The menu to read. |
| `children` | ObjectModel of [MenuEntry](#menuentry), read-only | The menu's entries. Empty for a tray icon's menu. |

## MenuAnchor

Opens a menu as a Windows menu next to an anchor, as `MenuAnchor { menu: item.menu; anchor.item: button }`. The menu opens at the bottom-left corner of the anchor rectangle; the anchor's `edges`, `gravity` and `adjustment` are not used.

| Member | Type | Description |
| --- | --- | --- |
| `menu` | [MenuHandle](#menuhandle) | The menu to open. |
| `anchor` | [PopupAnchor](#popupanchor), read-only | Where the menu opens, as `anchor.item: button`. |
| `visible` | bool, read-only | True from `open()` until `closed()`. |
| `open()` | method | Opens the menu. |
| `close()` | method | Marks the menu closed. Windows runs the menu itself, so the anchor closes on the next turn of the event loop after it opens, without waiting for the menu. |
| `opened()` | signal | Emitted when `open()` opens the menu. |
| `closed()` | signal | Emitted when the anchor closes. |

## Retainable

An attached object that keeps an object alive after it would have been destroyed, so it can finish an exit transition. Only retainable objects have it, and no Winshell type is retainable in this release, so attaching `Retainable` reports an error.

| Member | Type | Description |
| --- | --- | --- |
| `retained` | bool, read-only | True after the object was dropped and a lock keeps it alive. |
| `lock()` | method | Adds a lock. While any lock is held, a dropped object stays. |
| `unlock()` | method | Removes a lock. When the last lock goes after the object was dropped, it is destroyed. |
| `forceUnlock()` | method | Drops every lock. An object that was already dropped is destroyed. |
| `dropped()` | signal | Emitted when the object would have been destroyed. |
| `aboutToDestroy()` | signal | Emitted just before the object is destroyed. |

## RetainableLock

Holds a lock on one retainable object while `locked` is true, as `RetainableLock { object: x; locked: true }`. See [Retainable](#retainable): no Winshell type is retainable in this release, so setting `object` logs an error.

| Member | Type | Description |
| --- | --- | --- |
| `object` | object | The retainable object to hold. |
| `locked` | bool | Whether the lock is held. Default false. |
| `retained` | bool, read-only | True after the object was dropped and a lock keeps it alive. |
| `dropped()` | signal | Emitted when the object would have been destroyed. |
| `aboutToDestroy()` | signal | Emitted just before the object is destroyed. |

## Edges

Edges of a rectangle, combined with `|`, as `Edges.Top | Edges.Right`. [PopupAnchor](#popupanchor) and `FloatingWindow.startSystemResize()` take them.

| Value | Description |
| --- | --- |
| `None` | No edge. 0. |
| `Top` | The top edge. 1. |
| `Left` | The left edge. 2. |
| `Right` | The right edge. 4. |
| `Bottom` | The bottom edge. 8. |

## ExclusionMode

How a [PanelWindow](#panelwindow) reserves its edge, for `exclusionMode`.

| Value | Description |
| --- | --- |
| `Normal` | Reserves `exclusiveZone` logical pixels, and lays out in the work area. |
| `Ignore` | Reserves nothing, and lays out against the whole screen, over the taskbar. |
| `Auto` | Reserves the panel's height, or width for a side panel, plus its margins. The default. |

## PopupAdjustment

What may change to keep a popup window on its screen, for `PopupAnchor.adjustment`. Combine values with `|`. Flips are tried first, then slides, then resizes.

| Value | Description |
| --- | --- |
| `None` | Nothing changes; the popup window may leave the work area. |
| `SlideX` | Slides left or right into the work area. |
| `SlideY` | Slides up or down into the work area. |
| `Slide` | `SlideX` and `SlideY`. |
| `FlipX` | Flips to the other side of the anchor horizontally, when that fits. |
| `FlipY` | Flips to the other side of the anchor vertically, when that fits. |
| `Flip` | `FlipX` and `FlipY`. |
| `ResizeX` | Narrows to fit the work area. |
| `ResizeY` | Shortens to fit the work area. |
| `Resize` | `ResizeX` and `ResizeY`. |
| `All` | Every adjustment. The default. |

## Intersection

How a [Region](#region) combines with its parent region, for `intersection`.

| Value | Description |
| --- | --- |
| `Combine` | Adds the region. The default. |
| `Subtract` | Removes the region. |
| `Intersect` | Keeps only where both overlap. |
| `Xor` | Keeps where one or the other is, but not both. |

## RegionShape

The shape of a [Region](#region), for `shape`.

| Value | Description |
| --- | --- |
| `Rect` | A rectangle. The default. |
| `Ellipse` | An ellipse that fills the region's bounds. |

## ObjectComparison

How a [ScriptModel](#scriptmodel) matches JavaScript objects, for `comparisonMode`.

| Value | Description |
| --- | --- |
| `Structure` | Objects with the same properties and values are the same entry. The default. |
| `Identity` | Only the same object is the same entry. |

## MenuButtonType

The kind of button a [MenuEntry](#menuentry) has, from `buttonType`.

| Value | Description |
| --- | --- |
| `None` | No button. |
| `CheckBox` | A check box. |
| `RadioButton` | A radio button. |

## UpdateStatus

Where an update is, for [Updates](#updates)`.status`.

| Value | Description |
| --- | --- |
| `Idle` | Not checked yet in this run. |
| `Checking` | Asking GitHub for the latest release. |
| `UpToDate` | No release is newer than this Winshell. |
| `Downloading` | Downloading and checking the newer release. |
| `Ready` | Downloaded, its hash checked and unzipped. It installs on a start that finds no other Winshell running. |
| `Failed` | The last check, download or install failed; `error` says why. |

## Examples

### A bar on every screen

`Variants` makes one panel per screen, and makes or removes one as screens are connected and removed. The panel spans the top of its screen and reserves its height there.

```qml
import QtQuick
import Winshell

ShellRoot {
  Variants {
    model: Shell.screens

    PanelWindow {
      required property var modelData
      screen: modelData
      anchors.top: true
      anchors.left: true
      anchors.right: true
      implicitHeight: 32
      color: "transparent"
      WindowEffects.backdrop: WindowEffects.Acrylic

      SystemClock { id: clock; precision: SystemClock.Minutes }
      CalendarMonth { id: format; date: clock.date }

      Text {
        anchors.centerIn: parent
        text: format.timeLabel
      }
    }
  }
}
```

### A popup window from a button

The popup window opens above the button, 8 pixels from it, and closes on Escape or a click elsewhere.

```qml
import QtQuick
import QtQuick.Controls
import Winshell

ShellRoot {
  PanelWindow {
    anchors.bottom: true
    anchors.left: true
    anchors.right: true
    implicitHeight: 48

    Button {
      id: button
      anchors.centerIn: parent
      text: "Open"
      onClicked: popup.visible = !popup.visible
    }

    PopupWindow {
      id: popup
      anchor.item: button
      anchor.edges: Edges.Top
      anchor.gravity: Edges.Top
      anchor.margins.top: 8
      implicitWidth: 320
      implicitHeight: 200
      grabFocus: true
      color: "transparent"
      WindowEffects.backdrop: WindowEffects.Acrylic
      WindowEffects.corners: WindowEffects.RoundCorners

      Text { anchors.centerIn: parent; text: "Hello" }
    }
  }
}
```

### A label that clicks pass through

An empty mask lets every click through to the windows below, while the panel still draws.

```qml
import QtQuick
import Winshell

ShellRoot {
  PanelWindow {
    anchors.top: true
    anchors.right: true
    margins.top: 16
    margins.right: 16
    implicitWidth: 160
    implicitHeight: 40
    color: "#c0202020"
    mask: Region {}

    Text {
      anchors.centerIn: parent
      color: "white"
      text: "Recording"
    }
  }
}
```

### State that survives a reload

The count stays when the configuration reloads after an edit. `PersistentProperties` is the first child of the `ShellRoot`, so it matches the first child of the old one.

```qml
import QtQuick
import QtQuick.Controls
import Winshell

ShellRoot {
  PersistentProperties {
    id: persist
    property int clicks: 0
  }

  PanelWindow {
    anchors.top: true
    anchors.left: true
    anchors.right: true
    implicitHeight: 40

    Button {
      anchors.centerIn: parent
      text: "Clicked " + persist.clicks + " times"
      onClicked: persist.clicks++
    }
  }
}
```
