# Winshell.Windows

App windows and virtual desktops. Reach them from `Shell.Current.WindowList` and `Shell.Current.VirtualDesktops`. A preview of one window is `WindowThumbnail` in [Winshell](winshell.md#windowthumbnail).

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## AppWindow

A top-level window of another program.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Activated` | bool, read-only | Whether this is WindowListService.ActiveWindow. |
| `AppId` | string, read-only | The AppUserModelID the window carries, or its process package's, or its program's name in lower case. |
| `Attention` | bool, read-only | Whether the window flashed to ask for attention, as a chat or a finished download does, and has not been activated since. The taskbar shows this as a highlighted button. |
| `Desktop` | [VirtualDesktop](#virtualdesktop)?, read-only | The desktop in VirtualDesktopsService.Desktops whose id is DesktopId, when that list is loaded. |
| `DesktopId` | string, read-only | The virtual desktop this window is on, or empty when Windows does not report one. This uses the supported desktop API. |
| `Executable` | string, read-only | The full path of the program that owns the window, or empty when it cannot be read. |
| `Fullscreen` | bool, read-only | Whether the window covers its screen without a frame. Windows has no request that puts another program's window into fullscreen. |
| `Icon` | string, read-only | A path an icon is read from: a packaged app's Start icon, or the program's own icon. |
| `Maximized` | bool | Whether the window is maximized. Setting it maximizes the window, or restores it. |
| `Minimized` | bool | Whether the window is minimized. Setting it minimizes the window, or restores it. |
| `Parent` | [AppWindow](#appwindow)?, read-only | The listed window that owns this one, as a main window owns its dialog, or null. |
| `Screens` | IReadOnlyList of [ScreenInfo](winshell.md#screeninfo), read-only | The screen the window is on: the one Windows says it is nearest. |
| `Title` | string, read-only | The window's title, as its title bar shows it. May be empty. |
| `Closed` | EventHandler | The window closed. This comes after it has left WindowListService.Windows. |
| `Activate()` | void | Brings the window to the front and focuses it, restoring it first when it is minimized. |
| `Close()` | void | Asks the window to close, as its close button does. The program can still keep it open. |
| `MoveTo([ScreenInfo](winshell.md#screeninfo)? screen)` | void | Moves the window onto a monitor. Its logical size stays, and a maximized window is maximized again there. |
| `MoveToDesktop([VirtualDesktop](#virtualdesktop)? desktop)` | void | Moves the window onto a virtual desktop. This uses the supported desktop API. |

## VirtualDesktop

One desktop Task View can switch to.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Current` | bool, read-only | Whether this is the desktop on screen. |
| `Id` | string, read-only | The desktop's id, the same text AppWindow.DesktopId uses. |
| `Index` | int, read-only | Where the desktop sits in VirtualDesktopsService.Desktops, starting at zero. |
| `Name` | string, read-only | The name Task View shows: the one the user gave it, or `Desktop 1`, `Desktop 2` and so on. |

## VirtualDesktopsService

The desktops Task View shows. Listing, switching, creating and removing use the internal manager only after a read of a known Windows build succeeds. Which desktop a window is on uses the supported manager, including when this list is not supported.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Current` | [VirtualDesktop](#virtualdesktop)?, read-only | The desktop on screen, or null while Supported is false. |
| `Desktops` | ObservableCollection of [VirtualDesktop](#virtualdesktop), read-only | The desktops in Task View's order. Empty while Supported is false. |
| `Supported` | bool, read-only | Whether this Windows build's desktop list can be read, so the list, switching, creating and removing work. |
| `UnsupportedReason` | string, read-only | Why Supported is false. Empty when the list can be used. |
| `Create()` | [VirtualDesktop](#virtualdesktop)? | Adds a desktop at the end of the list without switching to it. The new desktop, or null when it was not created or Supported is false. |
| `Dispose()` | void | Stops following desktop changes. |
| `Remove([VirtualDesktop](#virtualdesktop)? desktop)` | void | Removes desktop. Its windows move to the desktop before it, or the next one when it is the first. Does nothing for null, for the last desktop left, or while Supported is false. |
| `SwitchTo([VirtualDesktop](#virtualdesktop)? desktop)` | void | Switches to desktop. Does nothing for null, or while Supported is false. |

## WindowListService

The other programs' windows, as the taskbar and Alt+Tab list them.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ActiveWindow` | [AppWindow](#appwindow)?, read-only | The listed window that has the focus, or owns the window that has it. Null when none does. |
| `Windows` | ObservableCollection of [AppWindow](#appwindow), read-only | The windows, in the order they opened. |
| `Dispose()` | void | Stops following the windows. The list stays as it was. |
