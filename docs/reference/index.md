# Reference

Every type a configuration can use, by module. A module's page lists each type with all of its properties, methods and signals; a test fails when a type or member is missing from it, or when it lists one the type doesn't have. The guide pages linked from each module explain behavior, permissions and limits in more depth.

| Module | What it has |
| --- | --- |
| [Qt Quick](qt-quick.md) | The QML language's own modules: items, controls in the Windows 11 style, layouts, shapes, effects |
| [Winshell](winshell.md) | `Shell`, `ShellRoot`, panels, popup windows and floating windows, screens, `Variants`, the backdrop, menus, a clock, a month grid, locking the session and running at sign-in |
| [Winshell.Io](winshell-io.md) | Running programs, reading and writing files, JSON files as properties, sockets and named pipes, and `IpcHandler` for `winshell ipc` |
| [Winshell.Widgets](winshell-widgets.md) | Icons that stay sharp at every scale, clipping rectangles, and wrappers that size to their child |
| [Winshell.Theme](winshell-theme.md) | Light or dark, the accent color, and the WinUI 3 tokens of the Windows 11 look |
| [Winshell.Input](winshell-input.md) | Hotkeys that work anywhere in Windows |
| [Winshell.Apps](winshell-apps.md) | The apps Start lists, search, and launching them |
| [Winshell.Audio](winshell-audio.md) | Playback and recording devices, audio sessions, volume, mute and levels |
| [Winshell.Media](winshell-media.md) | Media sessions: what is playing, and play, pause, skip and seek |
| [Winshell.Power](winshell-power.md) | The battery, the power mode, monitor brightness, idle time and keeping the PC awake |
| [Winshell.Windows](winshell-windows.md) | App windows, live previews of them, and virtual desktops |
| [Winshell.Network](winshell-network.md) | Adapters, Wi-Fi networks, connection profiles and connectivity |
| [Winshell.Bluetooth](winshell-bluetooth.md) | The Bluetooth adapter, paired and nearby devices, pairing and connecting |
| [Winshell.Notifications](winshell-notifications.md) | Notification Center, Focus and Do Not Disturb, and sending toasts |
| [Winshell.Tray](winshell-tray.md) | The tray icons apps add to the notification area |
| [Winshell.Capture](winshell-capture.md) | Capturing a window or a screen, live or as a still |
| [Winshell.Winland](winshell-winland.md) | Monitors, workspaces and windows from the Winland window manager |

## Conventions

- **Type** says what a member holds: `bool`, `int`, `real`, `string`, `color`, `date`, `var` for any JavaScript value, `list of X`, or `ObjectModel of X`. An `ObjectModel` can be a `Repeater` model as it is; in JavaScript, its `values` is the array.
- **read-only** members change on their own, as Windows reports changes. Bind to them.
- A **group**, such as a panel's `anchors`, is one object you can't replace, but you can set each of its fields: `anchors.top: true`.
- Sizes and positions are in logical pixels unless a member says otherwise. A screen's [scale](winshell.md#shellscreen) turns them into physical pixels.
- A singleton is used by its name, such as `Audio.defaultPlaybackDevice`. A creatable type is declared, such as `Hotkey { }`. Other objects come from a singleton or a model.
