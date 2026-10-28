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
| `Quickshell.Wayland` | `Winshell` for the session lock, `Winshell.Power` for idle, `Winshell.Windows` for the window list |
| `Quickshell.Services.Pipewire` | `Winshell.Audio` |
| `Quickshell.Services.Mpris` | `Winshell.Media` |
| `Quickshell.Networking` | `Winshell.Network` |
| `Quickshell.Bluetooth` | `Winshell.Bluetooth` |
| `Quickshell.Services.UPower` | `Winshell.Power` |
| `Quickshell.Services.Notifications` | `Winshell.Notifications` |
| `Quickshell.Services.SystemTray` | `Winshell.Tray` |
| `Quickshell.Services.Pam` | `Winshell` |
| `Quickshell.Hyprland` | `Winshell.Winland` |
| `import qs.widgets` | `import config.widgets` |

## Types

Types not listed keep their names in the new module.

| Quickshell | Winshell | Module |
| --- | --- | --- |
| `Quickshell` (the singleton) | `Shell` | `Winshell` |
| `QuickshellSettings` | `ShellSettings` | `Winshell` |
| `QsWindow` | `ShellWindow` | `Winshell` |
| `QsMenuHandle`, `QsMenuEntry`, `QsMenuButtonType`, `QsMenuOpener`, `QsMenuAnchor` | `MenuHandle`, `MenuEntry`, `MenuButtonType`, `MenuOpener`, `MenuAnchor` | `Winshell` |
| `DesktopEntries`, `DesktopEntry`, `DesktopAction` | `Apps`, `AppEntry`, `AppAction` | `Winshell` |
| `WlSessionLock`, `WlSessionLockSurface` | `SessionLock`, `SessionLockSurface` | `Winshell` |
| `PamContext`, `PamResult`, `PamError` | `SignInContext`, `SignInResult`, `SignInError` | `Winshell` |
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
| `IdleInhibitor` | `KeepAwake` | `Winshell.Power` |
| `NotificationServer` | `NotificationListener` | `Winshell.Notifications` |
| `ToplevelManager`, `Toplevel` | `WindowList`, `AppWindow` | `Winshell.Windows` |
| `Hyprland`, `HyprlandEvent`, `HyprlandMonitor`, `HyprlandWorkspace` | `Winland`, `WinlandEvent`, `WinlandMonitor`, `WinlandWorkspace` | `Winshell.Winland` |
| `HyprlandToplevel` | `WinlandWindow` | `Winshell.Winland` |

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

### Windows

`Winshell.Windows` names windows the way Windows does.

| Quickshell | Winshell |
| --- | --- |
| `ToplevelManager.toplevels` | `WindowList.windows` |
| `ToplevelManager.activeToplevel` | `WindowList.activeWindow` |
| `Toplevel.fullscreenOn(screen)` | `AppWindow.moveTo(screen)`, which moves the window onto that monitor |
| `Toplevel.setRectangle`, `Toplevel.unsetRectangle` | Removed. Windows does not take a rectangle for the minimize animation |
| `Toplevel.fullscreen` writable | `AppWindow.fullscreen` is read-only. Windows has no request that puts another program's window into fullscreen |

`AppWindow` also has `executable`, the program's path, and `icon`, a path the icon provider can draw. `appId` is unchanged: the window's AppUserModelID, or the program's file name in lower case.

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

## Removed

These did nothing on Windows, or repeated what another type does.

| Quickshell | Instead |
| --- | --- |
| `WlrLayershell.layer` | `PanelWindow.aboveWindows`: `false` for `WlrLayer.Background` and `WlrLayer.Bottom` |
| `WlrLayershell.keyboardFocus` | `PanelWindow.focusable` |
| `WlrLayershell.namespace` | Nothing |
| `BackgroundEffect.blurRegion` | `WindowEffects.backdrop: WindowEffects.Acrylic` |
| `GlobalShortcut` | An `IpcHandler`, and a key bound in Winland or another hotkey tool to run `winshell ipc call` |
| `HyprlandWindow` | Nothing |
| `ScreencopyView`, `ShortcutInhibitor` | Nothing |
| `Quickshell.Services.Greetd`, `Quickshell.Services.Polkit`, `Quickshell.I3`, `Quickshell.DBusMenu`, `Quickshell.WindowManager` | Nothing |

## Everything else

- The configuration lives in `%XDG_CONFIG_HOME%\winshell` or `%USERPROFILE%\.config\winshell`. A `quickshell` folder there is no longer used.
- The environment variables are `WINSHELL_APP_ID`, `WINSHELL_DISABLE_FILE_WATCHER` and `WINSHELL_NO_RELOAD_POPUP`, not `QS_APP_ID`, `QS_DISABLE_FILE_WATCHER` and `QS_NO_RELOAD_POPUP`.
- `Shell.appId` is `winshell` unless `//@ pragma AppId` or `WINSHELL_APP_ID` sets it.
- `Shell.hasVersion` and `//@ if hasVersion(...)` still answer for version 0.3.
- Log categories are short: `audio`, `media`, `power`, `winland` and so on, not `pipewire`, `mpris`, `quickshell.service.upower` and `hyprland`.

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
| `appIcon`, `image`, `urgency`, `transient`, `resident`, `hasActionIcons`, `hasInlineReply`, `inlineReplyPlaceholder`, `hints` | Removed; the listener does not supply these fields |
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
| Writable `Networking.wifiEnabled` | Read-only `wifiEnabled` and `wifiState`; use Windows Settings to control radios |
| `NetworkDevice.disconnect()`, `WiredDevice.network` | Use the `networks` model; Wi-Fi network objects provide `disconnect()`; wired connections are managed by Windows |

Wi-Fi permission is requested only through `Networking.requestAccess()`. All state is read-only; connection changes require method calls. `airplaneMode` is a best-effort persisted Windows state with an explicit Unknown value, independent of individual radios.
