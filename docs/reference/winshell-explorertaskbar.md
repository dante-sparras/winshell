# Winshell.ExplorerTaskbar

Explorer's taskbar: whether it is hidden while this shell runs. Reach it from `Shell.Current.ExplorerTaskbar`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## ExplorerTaskbarService

Hides Explorer's taskbar for a shell that draws its own. Winshell shows it again when the shell exits, however it exits. While it is hidden, a taskbar window Explorer makes is hidden too.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Hidden` | bool | Whether Explorer's taskbar is hidden and its screen edge is released. |
| `Dispose()` | void | Shows Explorer's taskbar again when this shell had hidden it. |
