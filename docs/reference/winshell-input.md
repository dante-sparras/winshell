# Winshell.Input

`import Winshell.Input`. The module has `Hotkey`: a chord, or a short sequence of chords, that works anywhere in Windows, even when no shell window is focused.

[Windows hotkeys](../hotkeys.md) explains how a sequence is written, how Windows registers it, and when the Windows key alone uses a keyboard hook.

## Hotkey

A hotkey, created with `Hotkey { }`. It claims `sequence` from Windows while `enabled` is true, and emits `pressed` and `released`. A chord that Windows or another app already owns is reported in `taken` and `error`, and is not stolen. Warnings use the `hotkey` category.

| Member | Type | Description |
| --- | --- | --- |
| `sequence` | string | The chord, such as `"Ctrl+Shift+K"` or `"Win"`, or up to eight chords separated by commas, such as `"Ctrl+K, C"`. Modifiers are `Win`, `Ctrl`, `Alt` and `Shift`. An empty sequence is idle. |
| `enabled` | bool | Claims the hotkey when true and releases it when false. Default `true`. |
| `active` | bool, read-only | Whether Windows accepted the claim. |
| `taken` | bool, read-only | Whether Windows or another app already owns the chord. |
| `pressed` | bool, read-only | True from the press until the keys of the last chord are up. |
| `error` | string, read-only | Why the hotkey is not active: a sequence that cannot be read, or a chord that is taken. Empty otherwise. A sequence that cannot be read sets it even while `enabled` is false. |
| `pressed()` | signal | The hotkey was pressed: the last chord of the sequence went down. |
| `released()` | signal | The keys of the last chord came up. |

```qml
import QtQuick
import Winshell
import Winshell.Input

ShellRoot {
    Hotkey {
        sequence: "Ctrl+Alt+T"
        onPressed: console.log("open a terminal")
        onReleased: console.log("keys up")
    }
}
```
