# Migrating from Quickshell

Winshell started as a port of Quickshell v0.3.2 and now has its own API ([ADR-0007](adr/0007-windows-first-api.md)). A Quickshell configuration does not load as it is. Rename its imports and types with the tables below. Most properties, methods, signals and enum values keep their Quickshell names; the module-specific changes below describe exceptions.

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
