# Winshell.ExplorerTaskbar

Explorer's taskbar: whether it is hidden while this shell runs. Reach it from `Shell.Current.ExplorerTaskbar`.

## ExplorerTaskbarService

Hides Explorer's taskbar for a shell that draws its own. The session belongs to the program, which restores the taskbar when it exits, however it exits.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Hidden` | bool | Whether Explorer's taskbar is hidden and its screen edge is released. |

## ExplorerTaskbarSession

One process-wide handoff. Hide is the first time the taskbar object exists, so leaving this off never looks at Explorer's windows. Restore is safe to call again, including after a crash handler and the process exit have both run.

| Member | Type | Description |
| --- | --- | --- |
| `Hidden` | bool, read-only |  |
| `Dispose()` | void |  |
| `ForThisProcess()` | [ExplorerTaskbarSession](#explorertaskbarsession), static | The session the running shell uses. Tests construct a session with a fake instead. |
| `Hide()` | void |  |
| `Restore()` | void | Puts Explorer's taskbar back when this session had hidden it. Does nothing otherwise. |

