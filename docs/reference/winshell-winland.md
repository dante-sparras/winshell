# Winshell.Winland

Monitors, workspaces and windows from Winland, over its named pipe. Reach them from `Shell.Current.Winland`.

## WinlandEvent

One line of the event stream.

| Member | Type | Description |
| --- | --- | --- |
| `WinlandEvent(string Name, string Data)` | constructor |  |
| `Data` | string, init |  |
| `Name` | string, init |  |
| `Parse(int argumentCount)` | IReadOnlyList of string | Splits Data at commas into that many strings. The last keeps any further commas, and missing ones are empty. |

## WinlandMonitor

A Monitor as Winland reports it.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ActiveWorkspace` | [WinlandWorkspace](#winlandworkspace)?, read-only | The Workspace shown on the Monitor, or null. |
| `Description` | string, read-only |  |
| `Focused` | bool, read-only | Whether this is FocusedMonitor. |
| `Height` | int, read-only |  |
| `Id` | int, read-only | Winland's number for the Monitor. -1 until Winland reports it. |
| `LastIpcObject` | JsonElement?, read-only | The Monitor's entry from Winland's last monitor list. Null before the first one. |
| `Name` | string, read-only | The Windows display name, such as `\\.\DISPLAY1`, which is the screen's id. |
| `Scale` | double, read-only |  |
| `Width` | int, read-only |  |
| `X` | int, read-only |  |
| `Y` | int, read-only |  |

## WinlandService

Winland's state. It reads it with `j/` queries and keeps it current from the event stream, updating the same objects for each event. Winland adds the Monitor to every event that names a Workspace, and it is used to tell same-numbered Workspaces apart. While Winland is not running the lists are empty; they fill when it starts.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `ActiveWindow` | [WinlandWindow](#winlandwindow)?, read-only |  |
| `ConfigPath` | string?, read-only | The full path of the config Winland read, as its `files` request reports it. Null while no Winland answers, and for a Winland too old to answer `files`. |
| `FocusedMonitor` | [WinlandMonitor](#winlandmonitor)?, read-only |  |
| `FocusedWorkspace` | [WinlandWorkspace](#winlandworkspace)?, read-only |  |
| `LogPath` | string?, read-only | The full path of the log Winland writes now, as its `files` request reports it. Null while it writes none: `debug:disable_logs` is true, or it runs elevated. |
| `Monitors` | ObservableCollection of [WinlandMonitor](#winlandmonitor), read-only | The Monitors, by id. |
| `PipePath` | string, read-only | Winland's pipe. It takes requests and, after an `events` request, streams events. |
| `ProcessId` | int, read-only | The process that answers on the pipe, or 0 while none does. |
| `Windows` | ObservableCollection of [WinlandWindow](#winlandwindow), read-only | The windows Winland manages, in the order they were first reported. |
| `Workspaces` | ObservableCollection of [WinlandWorkspace](#winlandworkspace), read-only | The Workspaces, by id, then by Monitor where two have the same number. |
| `RawEvent` | Action of [WinlandEvent](#winlandevent) |  |
| `Dispatch(string request)` | void | Sends `dispatch` and the request to Winland. A failure is logged. |
| `Dispose()` | void |  |
| `MonitorFor([ScreenInfo](winshell.md#screeninfo)? screen)` | [WinlandMonitor](#winlandmonitor)? | The Monitor of a screen, matched by its `\\.\DISPLAYn` name. Before Winland's first Monitor list arrives, it adds a Monitor for the screen that the list fills in later; after it, null when Winland reports no such Monitor. |
| `Reload()` | void | Asks Winland to read its config again, as `winland reload` does. A failure is logged. |
| `RefreshMonitors()` | void | Asks Winland for its Monitors again. Updates the listed ones and removes those it no longer reports. |
| `RefreshWindows()` | void | Asks Winland for its windows again, adding new ones and updating titles and Workspaces. |
| `RefreshWorkspaces()` | void | Asks Winland for its Workspaces again and updates the listed ones. |

## WinlandWindow

A window as Winland reports it. Its address is the window handle.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Activated` | bool, read-only | Whether this is ActiveWindow. |
| `Address` | string, read-only | The window handle in hexadecimal, without `0x`. |
| `Handle` | IntPtr, read-only | The window handle, which names the same window in `Winshell.Windows`. |
| `LastIpcObject` | JsonElement?, read-only | The window's entry from Winland's last window list. Null before the first one. |
| `Monitor` | [WinlandMonitor](#winlandmonitor)?, read-only | The Monitor of the window's Workspace, or null. |
| `Title` | string, read-only |  |
| `Urgent` | bool, read-only | Whether Winland marked the window urgent. It clears when the window is activated or its Workspace is focused. |
| `Window` | [AppWindow](winshell-windows.md#appwindow)?, read-only | The same window in WindowListService, or null when that list is not in use or has no such window. |
| `Workspace` | [WinlandWorkspace](#winlandworkspace)?, read-only |  |

## WinlandWorkspace

A Workspace. Winland numbers Workspaces per Monitor, so two can both be 3; Monitor tells them apart.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Active` | bool, read-only | Whether the Workspace is shown on its Monitor. |
| `Address` | string, read-only | The same as Name. |
| `Focused` | bool, read-only | Whether this is FocusedWorkspace. |
| `HasFullscreen` | bool, read-only |  |
| `Id` | int, read-only | The Workspace number. A named Workspace gets a negative id, and `special:special` gets -99. |
| `LastIpcObject` | JsonElement?, read-only | The Workspace's entry from Winland's last workspace list. Null before the first one. |
| `Monitor` | [WinlandMonitor](#winlandmonitor)?, read-only |  |
| `Name` | string, read-only | The Workspace's name: its number, a name, or `special:` and a name. |
| `Windows` | ObservableCollection of [WinlandWindow](#winlandwindow), read-only |  |
| `Urgent` | bool, read-only | Whether a window on it is urgent while the Workspace is not focused. |
| `Activate()` | void | Shows the Workspace. A Workspace number means that number on the focused Monitor in Winland, so the Workspace's own Monitor is focused first when it is another one. |

