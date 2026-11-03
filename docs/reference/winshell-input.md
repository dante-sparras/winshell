# Winshell.Input

A hotkey that works anywhere in Windows. Make one with `Shell.NewHotkey()`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## Hotkey

A hotkey that works anywhere in Windows. Sequence uses Windows names: `Win`, `Ctrl`, `Alt` and `Shift`. Dispose it to give the keys back.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Active` | bool, read-only | Whether the keys are claimed and a press raises Pressed. |
| `Enabled` | bool | Whether the hotkey claims its keys. False gives them back. True by default. |
| `Error` | string, read-only | Why the hotkey is not active, such as a sequence that does not parse or keys that are taken. Empty when it is active, disabled, or has no sequence. |
| `IsPressed` | bool, read-only | True from the press until the keys of the last chord are up. |
| `Sequence` | string | The keys, such as `Win+Ctrl+K`, or several chords separated by commas, such as `Win+Ctrl+K, C`. Empty claims nothing. Setting it claims the new keys from Windows. |
| `Taken` | bool, read-only | Whether Windows or another app already holds the keys, so this hotkey could not claim them. |
| `Pressed` | EventHandler | The last chord of the sequence went down. |
| `Released` | EventHandler | The keys of the last chord came up. |
| `Dispose()` | void | Gives the keys back to Windows. No event is raised after this. |
