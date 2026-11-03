# Winshell.Input

A hotkey that works anywhere in Windows. Make one with `Shell.NewHotkey()`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## Hotkey

A hotkey that works anywhere in Windows. Sequence uses Windows names: `Win`, `Ctrl`, `Alt` and `Shift`. Dispose it to give the keys back.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Active` | bool, read-only |  |
| `Enabled` | bool |  |
| `Error` | string, read-only |  |
| `IsPressed` | bool, read-only | True from the press until the keys of the last chord are up. |
| `Sequence` | string |  |
| `Taken` | bool, read-only |  |
| `Pressed` | EventHandler |  |
| `Released` | EventHandler |  |
| `Dispose()` | void |  |

