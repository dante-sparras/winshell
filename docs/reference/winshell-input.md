# Winshell.Input

`import Winshell.Input`. The module has `Hotkey`, a chord or a short sequence of chords that works anywhere in Windows, and `ShortcutInhibitor`, which gives one shell window the keyboard while that window is in front.

[Windows hotkeys](../hotkeys.md) explains how a sequence is written, how Windows registers it, when the keyboard hook is installed, and what an inhibitor holds back.

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

## ShortcutInhibitor

Sends keys to one shell window, the way a remote desktop client or a game takes the keyboard. Quickshell's `ShortcutInhibitor` lives in `Quickshell.Wayland` and asks the compositor. On Windows the same object lives in `Winshell.Input` and uses the keyboard hook from [Windows hotkeys](../hotkeys.md). There is one hook. It is installed only while a hotkey, a waiting sequence, or an inhibitor needs it.

`active` is true only while `enabled` is true, `window` is the foreground window, and Windows has installed the hook. `enabled` defaults to false. Losing the foreground makes `active` false at once, and the next key is left for Windows. Gaining the foreground makes `active` true again while `enabled` stays true. While `active` is true, Winshell `Hotkey` objects do not run. The hook posts each key to the shell window and holds it back from Windows. A key that cannot be posted is left for Windows and for hotkeys.

`cancelled` means the hook's thread ended without Winshell asking it to stop. Winshell sets `enabled` to false, which is what Quickshell does when the compositor withdraws the inhibitor. Losing the foreground pauses capture and does not cancel it. Windows can also remove a hook that missed its timeout and leave the thread running. Winshell cannot see that, so `cancelled` does not fire. The hook is already gone, so keys are not held back. If Windows refuses the hook, `active` stays false, `enabled` stays true, and `cancelled` does not fire. A warning uses the `hotkey` category.

Ctrl+Alt+Del and Win+L cannot be blocked. Windows does not deliver Ctrl+Alt+Del to a user-mode hook. When L is pressed with the Windows key down, the hook passes that key on and does not give it to a Winshell hotkey.

Capture stops when the shell exits, the shell reloads, or the window is destroyed. Exiting does not fire `cancelled`, because Winshell asked for the stop.

| Member | Type | Description |
| --- | --- | --- |
| `enabled` | bool | Asks to capture keys when true. Default `false`. Set to false by `cancelled`. |
| `window` | `ShellWindow` | The shell window the keys are posted to. Capture requires it to be the foreground window. |
| `active` | bool, read-only | True while `enabled` is true, `window` is the foreground window, and the hook is installed. |
| `cancelled()` | signal | The hook thread ended on its own. `enabled` is now false. |

```qml
import QtQuick
import Winshell
import Winshell.Input

ShellRoot {
    FloatingWindow { id: game }
    ShortcutInhibitor {
        window: game
        enabled: true
        onCancelled: enabled = false
    }
}
```
