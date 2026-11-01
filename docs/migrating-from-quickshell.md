# Migrating from Quickshell

Winshell started as a port of Quickshell v0.3.2 and now has its own API ([ADR-0007](adr/0007-windows-first-api.md)). A Quickshell configuration does not load as it is. Rename its imports and types with the tables below. A property, method, signal, or enum value that is not listed under [Properties](#properties) or [Notifications](#notifications) keeps its Quickshell name. `Pipewire.defaultAudioSink.audio.volume` is `Audio.defaultPlaybackDevice.audio.volume`.

The Qt Quick modules (`QtQuick`, `QtQuick.Controls`, `QtQuick.Layouts`, `QtQuick.Shapes`, `QtQuick.Effects`, `QtQml`) are unchanged.

## Modules

| Quickshell | Winshell |
| --- | --- |
| `Quickshell` | `Winshell` |
| `Quickshell.Io` | `Winshell.Io` |
| `Quickshell.Widgets` | `Winshell.Widgets` |
| `Quickshell.Windows` | `Winshell` for `WindowEffects`, `Winshell.Theme` for the theme |
| `Quickshell.Wayland` | `Winshell.Power` for idle, `Winshell.Windows` for the window list; the session lock is [removed](#session-lock) |
| `Quickshell.Services.Pipewire` | `Winshell.Audio` |
| `Quickshell.Services.Mpris` | `Winshell.Media` |
| `Quickshell.Networking` | `Winshell.Network` |
| `Quickshell.Bluetooth` | `Winshell.Bluetooth` |
| `Quickshell.Services.UPower` | `Winshell.Power` |
| `Quickshell.Services.Notifications` | `Winshell.Notifications` |
| `Quickshell.Services.SystemTray` | `Winshell.Tray` |
| `Quickshell.Services.Pam` | [Removed](#session-lock) |
| `Quickshell.Hyprland` | `Winshell.Winland` |
| `import qs.widgets` | `import config.widgets` |

## Types

Types not listed keep their names in the new module.

| Quickshell | Winshell | Module |
| --- | --- | --- |
| `Quickshell` (the singleton) | `Shell` | `Winshell` |
| `QuickshellSettings` | `ShellSettings` | `Winshell` |
| `QsWindow` | `ShellWindow` | `Winshell` |
| `QsMenuHandle`, `QsMenuButtonType` | `MenuHandle`, `MenuButtonType` | `Winshell` |
| `QsMenuEntry`, `QsMenuOpener`, `QsMenuAnchor` | Removed. A tray menu opens with `SystemTrayItem.display()` | `Winshell` |
| `DesktopEntries`, `DesktopEntry` | `Apps`, `AppEntry` | `Winshell.Apps` |
| `HyprlandFocusGrab` | `FocusGrab` | `Winshell` |
| `WindowsTheme` | `Theme` | `Winshell.Theme` |
| `Pipewire` | `Audio` | `Winshell.Audio` |
| `PwNode`, `PwNodeAudio`, `PwNodeType` | `AudioNode`, `AudioVolume`, `AudioNodeType` | `Winshell.Audio` |
| `PwLink`, `PwLinkGroup`, `PwLinkState` | `AudioLink`, `AudioLinkGroup`, `AudioLinkState` | `Winshell.Audio` |
| `PwObjectTracker`, `PwNodeLinkTracker`, `PwNodePeakMonitor`, `PwAudioChannel` | `AudioObjectTracker`, `AudioLinkTracker`, `AudioPeakMonitor`, `AudioChannel` | `Winshell.Audio` |
| `Mpris`, `MprisPlayer` | `Media`, `MediaSession` | `Winshell.Media` |
| `MprisPlaybackState`, `MprisLoopState` | `MediaPlaybackState`, `MediaLoopState` | `Winshell.Media` |
| `NMSettings` | `NetworkProfile` | `Winshell.Network` |
| `UPower`, `UPowerDevice` | `Power`, `PowerDevice` | `Winshell.Power` |
| `UPowerDeviceState`, `UPowerDeviceType` | `PowerDeviceState`, `PowerDeviceType` | `Winshell.Power` |
| `PowerProfiles`, `PowerProfile` | `PowerMode`, `PowerModeLevel` | `Winshell.Power` |
| `IdleInhibitor` | `KeepAwake` | `Winshell.Power` |
| `NotificationServer` | `NotificationListener` | `Winshell.Notifications` |
| `ToplevelManager`, `Toplevel` | `WindowList`, `AppWindow` | `Winshell.Windows` |
| `Hyprland`, `HyprlandEvent`, `HyprlandMonitor`, `HyprlandWorkspace` | `Winland`, `WinlandEvent`, `WinlandMonitor`, `WinlandWorkspace` | `Winshell.Winland` |
| `HyprlandToplevel` | `WinlandWindow` | `Winshell.Winland` |
| `GlobalShortcut` | `Hotkey` | `Winshell.Input` |

## Properties

### Audio

`Audio.defaultPlaybackDevice` and `Audio.defaultRecordingDevice` read and write the Windows default device. Quickshell's preferred default is not a separate device on Windows.

| Quickshell | Winshell |
| --- | --- |
| `defaultAudioSink` | `defaultPlaybackDevice` |
| `defaultAudioSource` | `defaultRecordingDevice` |
| `preferredDefaultAudioSink` | `defaultPlaybackDevice` |
| `preferredDefaultAudioSource` | `defaultRecordingDevice` |
| `PwNode.isSink` | `AudioNode.isPlayback` |
| `PwNode.isStream` | `AudioNode.isSession` |
| `PwNode.nickname` | `AudioNode.interfaceName` |
| `properties["node.name"]` | `properties["endpointId"]` on a device, `properties["sessionId"]` on a session |
| `properties["node.description"]` | `properties["friendlyName"]` |
| `properties["node.nick"]` | `properties["interfaceName"]` |
| `properties["media.class"]` | `properties["flow"]`, `playback` or `recording` |
| `properties["device.description"]` | `properties["adapter"]` |
| `properties["device.icon-name"]` | `properties["icon"]` |
| `properties["device.form-factor"]` | `properties["formFactor"]` |
| `properties["application.name"]` | `properties["appName"]` |
| `properties["application.icon-name"]` | `properties["icon"]` |
| `properties["application.process.id"]` | `properties["processId"]` |
| `properties["application.process.binary"]` | `properties["processName"]` |
| `properties["object.id"]` | `AudioNode.id` |

`AudioNodeType.Playback`, `Recording`, and `Session` use the bits of `Sink`, `Source`, and `Stream`.

| Quickshell | Winshell |
| --- | --- |
| `Sink` | `Playback` |
| `Source` | `Recording` |
| `Stream` | `Session` |
| `AudioSink` | `PlaybackDevice` |
| `AudioSource` | `RecordingDevice` |
| `AudioOutStream` | `PlaybackSession` |
| `AudioInStream` | `RecordingSession` |
| `Video`, `VideoSource`, `VideoSink`, `AudioDuplex` | Removed. A Windows audio node is not a video node. |

`AudioLinkState.Active` is 1 and `Inactive` is 0.

| Quickshell | Winshell |
| --- | --- |
| `Paused` | `Inactive` |
| `Active` | `Active` |
| `Error`, `Unlinked`, `Init`, `Negotiating`, `Allocating` | Removed. A session is playing or it is not. |

### Media

`Media.sessions` is the list of system media sessions. Per-app volume stays on the audio session, because Windows media controls have no volume of their own.

| Quickshell | Winshell |
| --- | --- |
| `Mpris.players` | `Media.sessions` |
| `MprisPlayer.identity` | `MediaSession.appName` |
| `MprisPlayer.desktopEntry` | `MediaSession.appId`, the AppUserModelID |
| `MprisPlayer.dbusName` | Removed. Windows has no bus name for a session. |
| `MprisPlayer.canRaise`, `raise()` | `canSwitchTo`, `switchTo()` |
| `MprisPlayer.canQuit`, `quit()` | Removed. Windows cannot ask the app to quit. |
| `MprisPlayer.canControl` | Removed. |
| `MprisPlayer.volume`, `volumeSupported` | Removed. Change the app's audio session instead. |
| `MprisPlayer.fullscreen`, `canSetFullscreen` | Removed. |
| `MprisPlayer.openUri()`, `supportedUriSchemes`, `supportedMimeTypes` | Removed. |
| `MprisPlayer.trackArtists` | `trackArtist` |
| `metadata["mpris:trackid"]` | Removed. |
| `metadata["xesam:title"]` | `metadata["title"]` |
| `metadata["xesam:artist"]` | `metadata["artist"]`, one string |
| `metadata["xesam:album"]` | `metadata["album"]` |
| `metadata["xesam:albumArtist"]` | `metadata["albumArtist"]` |
| `metadata["mpris:artUrl"]` | `metadata["artUrl"]` |
| `metadata["mpris:length"]` | `metadata["duration"]`, seconds |
| `metadata["xesam:trackNumber"]` | `metadata["trackNumber"]` |
| `metadata["xesam:genre"]` | `metadata["genres"]` |
| `MediaLoopState.Playlist` | `MediaLoopState.List` |

`canStop` is new. Windows reports it, and `stop()` uses it.

### Windows

`Winshell.Windows` names windows the way Windows does.

| Quickshell | Winshell |
| --- | --- |
| `ToplevelManager.toplevels` | `WindowList.windows` |
| `ToplevelManager.activeToplevel` | `WindowList.activeWindow` |
| `Toplevel.fullscreenOn(screen)` | `AppWindow.moveTo(screen)`, which moves the window onto that monitor |
| `Toplevel.setRectangle`, `Toplevel.unsetRectangle` | Removed. Windows does not take a rectangle for the minimize animation |
| `Toplevel.fullscreen` writable | `AppWindow.fullscreen` is read-only. Windows has no request that puts another program's window into fullscreen |

`AppWindow` also has `executable`, the program's path, and `icon`, a path the icon provider can draw. `appId` is the window's AppUserModelID, the application user model id of its process's package when the window sets none, or the program's file name in lower case.

### Apps

`import Winshell.Apps`. `Apps` lists Start menu shortcuts for all users and the current user, and packaged apps from PackageManager. A shortcut in the user's Programs folder replaces the common shortcut with the same path. The Startup folder is not indexed. An app whose windows use an AppUserModelID is listed once, as the packaged app, when PackageManager has that id.

`icon` is the shortcut, or `shell:AppsFolder\<AppUserModelID>` for a packaged app. An `Image` with that source and a `sourceSize` asks the shell for that many pixels times the screen scale.

`search(query)` ranks the visible apps. An exact name beats a prefix, which beats a word, which beats initials (`vsc` for Visual Studio Code), which beats a substring, which beats a tight fuzzy match (`notpad` for Notepad), which beats the description, the target, or a folder. Recent and frequent use, from Windows, lifts an app inside the same kind of match and does not jump a better name match. A blank query returns nothing. `recent` is the visible apps with a last-used time, newest first. `frequent` is the visible apps with a use count, highest first. `launch()` counts as a use immediately. Windows' own counts come from FeatureUsage (the higher of launch and switch) and UserAssist (the count and the last run).

`find` looks up an id, then an app id, then a display name, including hidden apps. `byId` is the id only. `appId` matches `AppWindow.appId`.

| Quickshell | Winshell |
| --- | --- |
| `DesktopEntries.applications` | `Apps.apps` |
| `heuristicLookup` | `find` |
| `execute()` | `launch()` |
| `comment` | `description` |
| `execString` | `commandLine` |
| `command` | Removed. `target` is the program and `arguments` is the argument string |
| `workingDirectory` | `startIn` |
| `startupClass` | `appId` |
| `categories` | `folders` |
| `noDisplay` | `hidden` |
| `genericName`, `keywords`, `runInTerminal` | Removed. Windows shortcuts do not have them |
| `actions`, `DesktopAction` | Removed. `launch()` opens the app |

`packaged`, `runCount`, and `lastUsed` are new. `lastUsed` is Unix milliseconds, or 0 when Windows has no record.

## Virtual desktops

`VirtualDesktops` is not a Quickshell type. It lists the desktops Task View shows. `supported` is false when Windows is outside builds 26100 through 26399, or when that method table cannot be read, and then `switchTo`, `create` and `remove` do nothing. `AppWindow.desktopId` and `moveToDesktop` use the supported window API and still work when the list does not. `desktop` is the matching object from `desktops` when the list is loaded. Winland's workspaces are unchanged.

| Member | Meaning |
| --- | --- |
| `VirtualDesktops.desktops` | The desktops, in Task View order |
| `VirtualDesktops.current` | The desktop the user is on |
| `VirtualDesktops.switchTo(desktop)` | Switch to that desktop |
| `VirtualDesktops.create()` | Create a desktop and return it |
| `VirtualDesktops.remove(desktop)` | Remove a desktop, moving its windows to a neighbor. The last one stays |
| `VirtualDesktop.id` | The desktop's id |
| `VirtualDesktop.name` | The name Windows stored, or `Desktop N` when it stored none |
| `AppWindow.desktopId` | The id of the desktop the window is on |
| `AppWindow.moveToDesktop(desktop)` | Move the window onto that desktop |

### Power

`PowerMode.mode` is the Windows power mode slider. `PowerMode.effective` is the mode Windows is applying. `Brightness.monitors` lists each monitor that reports a brightness.

| Quickshell | Winshell |
| --- | --- |
| `PowerProfiles.profile` | `PowerMode.mode` |
| `PowerProfile.PowerSaver` | `PowerModeLevel.BestEfficiency` |
| `PowerProfile.Balanced` | `PowerModeLevel.Balanced` |
| `PowerProfile.Performance` | `PowerModeLevel.BestPerformance` |
| `PowerProfiles.hasPerformanceProfile`, `degradationReason`, `holds` | Removed. `PowerMode.available` says whether Windows answered. |
| `PowerDevice.nativePath` | `deviceId` |
| `PowerDevice.powerSupply` | `powersTheSystem` |
| `PowerDevice.energy` | `remainingCapacity`, watt-hours |
| `PowerDevice.energyCapacity` | `fullChargeCapacity`, watt-hours |
| `PowerDevice.changeRate` | `chargeRate`, watts |
| `PowerDevice.isLaptopBattery` | `isSystemBattery` |
| `PowerDevice.iconName` values ending in `-symbolic` | The same name without `-symbolic`. A full battery is `battery-full`. |
| `PowerDeviceState.PendingCharge` | `NotCharging` |
| `PowerDeviceState.PendingDischarge` | Removed. |
| `PowerDeviceType` values other than `Unknown` and `Battery` | Removed. Windows reports batteries. |
| `IdleInhibitor.window` | Removed. `KeepAwake` is not tied to a window. |
| `IdleMonitor.respectInhibitors` | `respectKeepAwake` |

`Brightness`, `BrightnessMonitor`, `BrightnessConnection`, and `EffectivePowerMode` are new.

### Tray

`SystemTray.items` lists the notification area icons Explorer would show. An icon the app hid (`NIS_HIDDEN`) is left out until the app shows it again. See [Windows notification area](tray.md).

| Quickshell | Winshell |
| --- | --- |
| `SystemTrayItem.title` | `appName`, the program's description |
| `SystemTrayItem.tooltipTitle`, `tooltipDescription` | `tooltip`, the whole text |
| `SystemTrayItem.status`, `Status` | Removed. Hidden icons are not listed, and `overflow` says where Windows shows the rest |
| `SystemTrayItem.category`, `Category` | Removed. Windows has no categories |
| `SystemTrayItem.hasMenu`, `onlyMenu` | Removed. Every icon's app may show a menu; `display()` asks it to |
| `SystemTrayItem.scroll()` | Removed. Windows does not send scrolls to tray icons |
| `SystemTrayItem.activate()` | `click()`, a left click |
| `SystemTrayItem.secondaryActivate()` | `middleClick()` |

`executable`, `overflow`, `doubleClick()`, the `balloon(title, message, icon)` signal and the `BalloonIcon` enum are new.

## Hotkeys

`Winshell.Input` `Hotkey` registers a Windows hotkey. A normal chord, including `Win+E`, uses `RegisterHotKey`. The Windows key by itself, and the keys after the first chord of a sequence, use a low-level keyboard hook only while one of those hotkeys is enabled or a sequence is in progress. See [Windows hotkeys](hotkeys.md).

| Quickshell | Winshell |
| --- | --- |
| `appid`, `name`, `description`, `triggerDescription` | Removed. |
| `pressed`, `released` | `pressed`, `released` |
| | `sequence`, `enabled`, `active`, `taken`, `error` |

`sequence` uses `Win`, `Ctrl`, `Alt` and `Shift`.

## Session lock

Windows locks the session, not Winshell ([ADR-0008](adr/0008-windows-owns-the-lock-screen.md)). A lock drawn by the shell sits on the user's own desktop, where anything running as the user can kill it, and a password prompt there looks like phishing. Call `Session.lock()` from `Winshell` instead. It asks Windows to lock, as Win+L does, and returns whether Windows took the request. Windows' lock screen then handles sign-in, Windows Hello, every monitor, and Narrator. Set its look in Windows Settings, under Personalization > Lock screen.

| Quickshell | Winshell |
| --- | --- |
| `WlSessionLock`, `WlSessionLockSurface`, `locked: true` | `Session.lock()` |
| `PamContext`, `PamResult`, `PamError` | Removed. Winshell never asks for the Windows password |

There are no surfaces to draw, no `secure` property and no unlock: Windows unlocks.

## Removed

These did nothing on Windows, or repeated what another type does.

| Quickshell | Instead |
| --- | --- |
| `WlrLayershell.layer` | `PanelWindow.aboveWindows`: `false` for `WlrLayer.Background` and `WlrLayer.Bottom` |
| `WlrLayershell.keyboardFocus` | `PanelWindow.focusable` |
| `WlrLayershell.namespace` | Nothing |
| `BackgroundEffect.blurRegion` | `WindowEffects.backdrop: WindowEffects.Acrylic` |
| `HyprlandWindow` | Nothing |
| `HyprlandToplevel` attached to a toplevel | `Winland.toplevels`. `WinlandWindow` is not attached to an `AppWindow` |
| `ScreencopyView` | `Winshell.Capture`: `CaptureSession` and `CaptureView`; see [Windows capture](capture.md) |
| `ShortcutInhibitor` | Nothing |
| `Quickshell.Services.Greetd`, `Quickshell.Services.Polkit`, `Quickshell.I3`, `Quickshell.DBusMenu`, `Quickshell.WindowManager` | Nothing |
| `DesktopAction` | Removed. `AppEntry.launch()` opens the app |

## Capture

`Quickshell.Wayland.ScreencopyView` becomes a `CaptureSession` plus a `CaptureView` from `Winshell.Capture`. See [Windows capture](capture.md) for complete examples, export methods and Windows access rules. No Wayland compositor or protocol names remain in the module.

| Former member | Windows API |
| --- | --- |
| `captureSource` | `CaptureSession.target`: an `AppWindow` or `ShellScreen` |
| `paintCursor` | `CaptureSession.includeCursor`, applied on start |
| `live`, `captureFrame()` | Explicit `start()` / `stop()`; for a still export on the first `frameReady`, then stop |
| `hasContent` | `CaptureSession.hasFrame` |
| `sourceSize` | `CaptureSession.pixelSize`, in physical pixels |
| `constraintSize` | `CaptureView.width` / `height`, in logical pixels; the image fits with its aspect ratio preserved |
| `stopped` | Observe `runningChanged`; read `error` for access or device failures |

Stopping clears the preview and all retained pixels. Export with `saveToFile()` or `copyToClipboard()` before stopping if a still must outlive the session. There are no aliases for the old type or properties.

## Everything else

- Configurations live in `%APPDATA%\winshell`. `WINSHELL_CONFIG_DIR` selects another folder for that run. The first time Winshell uses the default folder, it copies `%USERPROFILE%\.config\winshell` when that folder exists, and it leaves the old folder in place. `%XDG_CONFIG_HOME%` is not read. A `quickshell` folder is not used.
- Cache is `%LOCALAPPDATA%\winshell\cache`, state is `%LOCALAPPDATA%\winshell\state`, per-shell data is `%LOCALAPPDATA%\winshell\data`, and an instance's log is `%LOCALAPPDATA%\winshell\run\<id>\log.txt`. `Shell.dataDir`, `Shell.stateDir`, and `Shell.cacheDir` are those per-shell folders. `//@ pragma DataDir`, `StateDir`, and `CacheDir` still override them; `$BASE` is the folder above `by-shell`.
- `Winshell.Io` adds `TomlAdapter`, which is to a TOML file what `JsonAdapter` is to a JSON file. Writing changes only the keys it has properties for, so the file keeps its comments, and `comments` sets the line written above a key it adds. Winshell's own config files are TOML ([ADR-0009](adr/0009-user-config-files-are-toml.md)).
- Winshell adds `Shell.configRoot`, the folder named configs load from, `Shell.logPath`, this instance's log, and `Shell.version`, the Winshell release as `winshell --version` prints it.
- `FloatingWindow.activate()` shows the window, restores it from minimized, and asks Windows to bring it to the front with the keyboard. Windows lets a shell do that after a click or hotkey in it; asked from another program, such as `winshell ipc call`, Windows may only flash its taskbar button.
- The environment variables are `WINSHELL_CONFIG_DIR`, `WINSHELL_APP_ID`, `WINSHELL_DISABLE_FILE_WATCHER` and `WINSHELL_NO_RELOAD_POPUP`, not `XDG_CONFIG_HOME`, `QS_APP_ID`, `QS_DISABLE_FILE_WATCHER` and `QS_NO_RELOAD_POPUP`.
- `Shell.appId` is `winshell` unless `//@ pragma AppId` or `WINSHELL_APP_ID` sets it.
- `//@ pragma IconTheme` is not a shell setting. A configuration that contains it fails to load.
- `ShellScreen.physicalPixelDensity`, `ShellScreen.logicalPixelDensity`, `ShellWindow.surfaceFormat` and `ShellWindow.resourcesLost` are removed. `Region` corner radii do clip the input mask: a corner above 0 replaces `radius`, and 0 uses `radius`. An ellipse ignores them.
- `FloatingWindow.fullscreen` covers the monitor, including the taskbar, and restores the previous place and frame. `FloatingWindow.parentWindow` is the owning window: the owned window stays above it, and is hidden and closed with it. It can be changed after the window is visible. Quickshell refuses that change. Quickshell's type is `QtObject`; here the owner is a shell window.
- `Shell.hasVersion` and `//@ if hasVersion(...)` answer for the Winshell release stamped into the build from `.release-please-manifest.json`, not Quickshell 0.3. `hasVersion(0, 3)` is true on 0.3.0 and any later release. A feature list names unreleased features of that exact release; none are declared, so `hasVersion(0, 3, ["feature"])` is false on 0.3.0 and true on a later release. `Shell.hasQtVersion` and `//@ if hasQtVersion(...)` still check the Qt Quick language level this runtime implements, which is 6.10.
- Log categories are short: `audio`, `media`, `power`, `hotkey`, `winland` and so on, not `pipewire`, `mpris`, `quickshell.service.upower` and `hyprland`.

## Notifications

`Winshell.Notifications` is a Windows Notification Center model and native toast sender, rather than a Linux notification server. See [Windows notifications](notifications.md) for permission, deployment and API examples.

| Former member | Windows API |
| --- | --- |
| `NotificationListener.trackedNotifications` | `notifications`, automatically populated; `groups` contains app sections |
| `Notification.desktopEntry` | `appId` (Windows AppUserModelID) |
| `Notification.summary` | `title` |
| `Notification.expireTimeout` | `expiresAt` (Unix milliseconds, zero when absent); `createdAt` is also available |
| `onNotification` and `notification.tracked = true` | `onNotificationAdded`; all readable toasts are already in the models |
| `keepOnReload`, `lastGeneration` | Removed; every listener reads the current Windows snapshot |
| Writable `*Supported` flags, `extraHints` | Removed; Windows determines listener capabilities |
| `NotificationAction`, incoming `actions`, `sendInlineReply()` | Removed; Windows exposes no incoming toast actions or activation arguments; `canInvoke` is false and `invoke()` reports this limitation |
| `appIcon` | `appIcon`, a `file:///` url of the logo Windows returns for the sending app. Empty when Windows gives none. It can fill in after the notification appears. Quickshell also tries the desktop entry's icon; Windows has no desktop entry, so there is no second lookup |
| `image`, `urgency`, `transient`, `resident`, `hasActionIcons`, `hasInlineReply`, `inlineReplyPlaceholder`, `hints` | Removed; the listener does not supply these fields |
| `expire()`, `NotificationCloseReason`, `NotificationUrgency`, `closed(reason)` | Removed; use `dismiss()` and observe model removals or `destroyed` |

`NotificationListener.requestAccess()` explicitly requests Windows permission. Importing or constructing the type never prompts. Read-only `accessStatus` and `error` distinguish cancellation, denial and unavailable access. Focus and Do Not Disturb are read-only states, with `Unknown` for unsupported Windows APIs. `ToastMessage` and `ToastAction` send native toasts with protocol activation under an installed Windows identity; this does not require listener access.

## Network

See [Windows networking](network.md) for the native models, permissions and explicit actions.

| Former member | Windows API |
| --- | --- |
| `Network.nmSettings`, `connectWithSettings()`, `NetworkProfile.uuid` | Removed; `Networking.profiles` exposes real Windows connection profiles and their `adapterId`, connectivity and cost |
| `NetworkDevice.nmManaged`, `autoconnect` | Removed; Windows manages adapters and per-network reconnection |
| `NetworkBackendType`, `Networking.backend` | Removed; the backend is always Windows |
| `ConnectivityCheckEnabled`, `canCheckConnectivity` | Removed; `connectivity` reports Windows NCSI without masking it |
| `checkConnectivity()` | `refresh()` |
| `NetworkConnectivity.Full`, `Limited`, `Portal` | `InternetAccess`, `LocalAccess`, `CaptivePortal` |
| `ConnectionFailReason.NoSecrets`, `WifiAuthTimeout`, `WifiNetworkLost`, `WifiClientFailed` | Action `error`: `InvalidPassword`, `Timeout`, `NetworkUnavailable`, `UnsupportedAuthentication` |
| `Network.known`, `stateChanging`, `onConnectionFailed` | `saved`, `busy`, `onOperationFailed(message)` and `error` |
| `WifiNetwork.connectWithPsk()` | `connectWithPassword()` |
| `WifiDevice.scannerEnabled`, `mode`, `WifiDeviceMode` | Removed; call `scan()` for one scan and observe `scanning` |
| Writable `Networking.wifiEnabled` | Writable `wifiEnabled`. Quickshell updates the value, then sets an rfkill software block. Winshell asks Windows and leaves the property on what Windows reports. `wifiState` stays read-only |
| `NetworkDevice.disconnect()`, `WiredDevice.network` | Use the `networks` model; Wi-Fi network objects provide `disconnect()`; wired connections are managed by Windows |

Wi-Fi scan permission is requested only through `Networking.requestAccess()`. Radio control is a separate request, made only when a configuration sets `wifiEnabled` to a different value. Connection changes require method calls. `airplaneMode` is a best-effort persisted Windows state with an explicit Unknown value, independent of individual radios.

## Bluetooth

See [Windows Bluetooth](bluetooth.md) for state, discovery, battery and action limits.

| Former member | Windows API |
| --- | --- |
| `BluetoothAdapter.adapterId` (`hci0`), adapter/device `dbusPath` | Windows device `id`; no D-Bus or HCI aliases |
| Writable adapter `enabled`, `discoverable`, `discoverableTimeout`, `pairable`, `pairableTimeout` | `enabled` asks Windows to change the radio and keeps the value Windows reports. `discoverable`, `discoverableTimeout`, `pairable` and `pairableTimeout` are removed |
| `BluetoothAdapterState.Enabled`, `Disabled`, `Blocked`, `Enabling`, `Disabling` | `On`, `Off`, `Disabled`, `Unknown`; pending device work is reported separately by `operation` |
| `BluetoothAdapter.discovering`, `devices` | Singleton `Bluetooth.discovering`, `startDiscovery()`, `stopDiscovery()`, `devices`, `pairedDevices`, `nearbyDevices` |
| `BluetoothDevice.deviceName`, writable `name` | Read-only `name` |
| `bonded`, writable `trusted`, `blocked`, `wakeAllowed` | Removed BlueZ/device-manager flags; use `paired` and Windows Settings |
| `icon` (BlueZ icon names) | Removed; configurations can use their own Fluent icons |
| Writable `connected`, pending `state` values | Read-only Windows-reported `connected`/`state`; explicit `connect()`/`disconnect()` calls and separate `operation`/`busy` |
| `cancelPair()`, `forget()` | `cancelPairing()`, `unpair()` |

All device actions report `error` and `onOperationFailed(message)`. Bluetooth reads open no remote device handles. Pairing uses Windows' UI without auto-confirming PINs; connect/disconnect supports paired audio drivers, with an explicit failure for unsupported devices. Battery remains a 0–1 fraction when Windows exposes it.
