# Winshell.Windows

App windows and virtual desktops. Reach them from `Shell.Current.WindowList` and `Shell.Current.VirtualDesktops`. A preview of one window is `WindowThumbnail` in [Winshell](winshell.md#windowthumbnail).

## AppWindow

A top-level window of another program.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Activated` | bool, read-only |  |
| `AppId` | string, read-only | The AppUserModelID the window carries, or its process package's, or its program's name in lower case. |
| `Attention` | bool, read-only | Whether the window flashed to ask for attention, as a chat or a finished download does, and has not been activated since. The taskbar shows this as a highlighted button. |
| `Desktop` | [VirtualDesktop](#virtualdesktop)?, read-only | The desktop in Desktops whose id is DesktopId, when that list is loaded. |
| `DesktopId` | string, read-only | The virtual desktop this window is on, or empty when Windows does not report one. This uses the supported desktop API. |
| `Executable` | string, read-only | The full path of the program that owns the window, or empty when it cannot be read. |
| `Fullscreen` | bool, read-only | Whether the window covers its screen without a frame. Windows has no request that puts another program's window into fullscreen. |
| `Icon` | string, read-only | A path an icon is read from: a packaged app's Start icon, or the program's own icon. |
| `Maximized` | bool |  |
| `Minimized` | bool |  |
| `Parent` | [AppWindow](#appwindow)?, read-only |  |
| `Screens` | IReadOnlyList of [ScreenInfo](winshell.md#screeninfo), read-only | The screen the window is on: the one Windows says it is nearest. |
| `Title` | string, read-only |  |
| `Closed` | EventHandler |  |
| `Activate()` | void |  |
| `Close()` | void |  |
| `MoveTo([ScreenInfo](winshell.md#screeninfo)? screen)` | void | Moves the window onto a monitor. Its logical size stays, and a maximized window is maximized again there. |
| `MoveToDesktop([VirtualDesktop](#virtualdesktop)? desktop)` | void | Moves the window onto a virtual desktop. This uses the supported desktop API. |

## VirtualDesktop

One desktop Task View can switch to.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Current` | bool, read-only |  |
| `Id` | string, read-only | The desktop's id, the same text DesktopId uses. |
| `Index` | int, read-only | Where the desktop sits in Desktops, starting at zero. |
| `Name` | string, read-only |  |

## VirtualDesktopsService

The desktops Task View shows. Listing, switching, creating and removing use the internal manager only after a read of a known Windows build succeeds. Which desktop a window is on uses the supported manager, including when this list is not supported.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Current` | [VirtualDesktop](#virtualdesktop)?, read-only |  |
| `Desktops` | ObservableCollection of [VirtualDesktop](#virtualdesktop), read-only |  |
| `Supported` | bool, read-only |  |
| `UnsupportedReason` | string, read-only | Why Supported is false. Empty when the list can be used. |
| `Create()` | [VirtualDesktop](#virtualdesktop)? |  |
| `Dispose()` | void |  |
| `Remove([VirtualDesktop](#virtualdesktop)? desktop)` | void |  |
| `SwitchTo([VirtualDesktop](#virtualdesktop)? desktop)` | void |  |

## WindowListService

The other programs' windows, as the taskbar and Alt+Tab list them.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ActiveWindow` | [AppWindow](#appwindow)?, read-only |  |
| `Windows` | ObservableCollection of [AppWindow](#appwindow), read-only |  |
| `Dispose()` | void |  |

