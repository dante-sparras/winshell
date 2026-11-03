# Winshell.ExplorerTaskbar

Explorer's taskbar: whether it is hidden while this shell runs. Reach it from `Shell.Current.ExplorerTaskbar`.

## ExplorerTaskbarService

Hides Explorer's taskbar for a shell that draws its own. The session belongs to the program, which restores the taskbar when it exits, however it exits.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Hidden` | bool | Whether Explorer's taskbar is hidden and its screen edge is released. |
