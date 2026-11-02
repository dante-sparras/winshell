# Windows hotkeys

`shell.NewHotkey()` returns a `Hotkey`. A configuration sets a chord, or a short sequence of chords, that works even when a shell window is not focused. The type is [Hotkey](reference/winshell-input.md#hotkey).

`Sequence` uses Windows names. Modifiers are `Win`, `Ctrl`, `Alt` and `Shift`, written before the key and joined with `+`. A sequence is two or more chords separated by commas, such as `Ctrl+K, C`, and it can have at most eight chords. The Windows key by itself is `Win`, `Left Win` or `Right Win`. `Super`, `Meta` and the other Linux modifier names are rejected, and so are left or right `Ctrl`, `Alt` and `Shift`, because `RegisterHotKey` cannot tell those sides apart. The comma key is written `Comma`, as in `Ctrl+Comma`, because a comma separates chords.

`Enabled` claims the hotkey or releases it. `Active` is true when Windows accepted the claim. `Taken` is true when Windows or another app already owns that chord, and `Error` names the chord. A sequence that cannot be read sets `Error` and claims nothing, including while `Enabled` is false. An empty sequence is idle and clears `Error`. `IsPressed` stays true until the keys of the last chord are up. `Pressed` and `Released` are the events.

A normal chord, including `Win+E`, is registered with `RegisterHotKey`. That API cannot register the Windows key alone, and it does not report the key coming up. `Win`, `Left Win`, `Right Win`, and every chord after the first one in a sequence, use a low-level keyboard hook. The hook is installed only while one of those hotkeys is enabled or a sequence is waiting for its next chord. It is removed when none of those remain, including when the shell exits. The hook runs on a thread of its own, so a busy shell does not slow typing, and Windows does not drop the hook for being slow. A Windows-key tap that this shell handles injects one down and up of the unassigned virtual key `0xE8` while the Windows key is still down, then lets the real key-up through. Windows treats the press as used, so Start does not open, and the key does not stay logically held. The hook ignores that injection. A tap this shell does not use is left alone, so it can still open Start. Chords such as `Win+E` stay on `RegisterHotKey`, and the Windows key going down is not swallowed, so those chords still reach Windows.

A sequence waits one second for the next chord. A different key cancels it. Modifier keys alone do not. The shell keeps one `RegisterHotKey` registration while any of its hotkeys use that chord. A chord that is already taken is reported and is not stolen.

Ctrl+Alt+Del and Win+L cannot be blocked. Windows does not deliver Ctrl+Alt+Del to a user-mode hook. When L is pressed with the Windows key down, the hook passes that key on and does not give it to a Winshell hotkey.

Warnings use the `hotkey` category.

Tests may register an unused chord such as `Ctrl+Alt+Shift+F24`, and may install the keyboard hook for as long as a Windows-key case is running. They remove the registration and the hook before the test ends. They do not inject keystrokes or change keyboard settings.
