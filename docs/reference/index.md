# Reference

Every public type a configuration uses, by namespace. A page lists each type and the members it declares. A test fails when a type or member is missing, or when a table names one the type does not have. Members inherited from another type on these pages are listed on that type.

See also the [command line](command-line.md).

| Namespace | What it has |
| --- | --- |
| [Winshell](winshell.md) | The shell, its windows and screens, IPC, preferences files and logging. |
| [Winshell.Apps](winshell-apps.md) | Installed apps, search, jump lists and recent documents. |
| [Winshell.Audio](winshell-audio.md) | Playback and recording devices, sessions, volume, mute and levels. |
| [Winshell.Bluetooth](winshell-bluetooth.md) | The Bluetooth adapter, paired and nearby devices, pairing and connecting. |
| [Winshell.Capture](winshell-capture.md) | A still or a live capture of a window or a screen. |
| [Winshell.Diagnostics](winshell-diagnostics.md) | The checks `winshell doctor` prints. Reading a project does not build it. |
| [Winshell.ExplorerTaskbar](winshell-explorertaskbar.md) | Whether Explorer's taskbar is hidden while this shell runs. |
| [Winshell.Input](winshell-input.md) | A hotkey that works anywhere in Windows. |
| [Winshell.Media](winshell-media.md) | What is playing, and play, pause, skip and seek. |
| [Winshell.Network](winshell-network.md) | Adapters, Wi-Fi networks, connection profiles and connectivity. |
| [Winshell.Notifications](winshell-notifications.md) | Notification Center, Focus, and toasts. |
| [Winshell.Power](winshell-power.md) | The battery, the power mode, monitor brightness, idle time and keeping the PC awake. |
| [Winshell.Session](winshell-session.md) | Locking the Windows session, and running at sign-in. |
| [Winshell.Tray](winshell-tray.md) | The icons apps add to the notification area. |
| [Winshell.Updates](winshell-updates.md) | A portable Winshell checking GitHub Releases. |
| [Winshell.Windows](winshell-windows.md) | App windows and virtual desktops. |
| [Winshell.Winland](winshell-winland.md) | Monitors, workspaces and windows from Winland. |

## Conventions

- **Type** is the C# type. `read-only` has no public setter. `init` is set when the object is created. `static` is on the type.
- A service raises `PropertyChanged` when Windows reports a change. The members of [ObservableObject](winshell.md#observableobject) are listed once.
- Sizes and positions on a window are logical pixels unless a member says otherwise. A screen's `DevicePixelRatio` turns them into physical pixels.
- A window's content is any `UIElement`. Winshell does not reference Reactor. A configuration that wants it sets `new ReactorHostControl(component)` as the content.
