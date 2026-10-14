# The bar loop follows events

The bar does its work on events, and its loop is a safety net. Chords and Super going up come from the keyboard hook. Pointer moves come from the caption mouse hook. Window moves, resizes, and foreground changes come from `WindowMotion`. Windows shown, hidden, and cloaked come from `WindowWatcher`. Nothing polls for a stuck key (ADR 0017).

The loop has three paces, and `BarCadence` holds them:

- **Fast**, for a moment after an event that changed something, and while a panel is open or a workspace recount is pending.
- **Idle**, once a second, just after the wall-clock second so the clock turns on time.
- **Away**, every few seconds, while the session is locked, the display is off, or nobody has touched the Host for a while. A turn still lands just after each wall-clock minute, so a clock that shows minutes is never late, and when the idle lock is due.

A turn does everything the loop is there for, except that a plugin is ticked only when it is due (ADR 0041), and that it looks for a window covering the bar on every turn only while one does.

An event that changed nothing leaves the pace alone. A chord, Super going up, an opened or closed panel, and a notification always speed the loop up. A window shown or hidden does so only when the tiled set or the workspace changed, a hidden window that took the keyboard only when it was followed to another Workspace (ADR 0038), a window move only when the bar went behind a window or came back, and a CLI line only when it is not a plain read such as `winoma desktop` (`CliDispatch.Reads`).

Use of the mouse, Super, a chord, a CLI line that acts, a changed desktop, the session unlocking, or the display turning on ends the away pace at once. A key that is no chord is not an event, so it ends the away pace at the next turn. A plugin that holds the Escape claim is at work, as dictation is for a whole take, and keeps the idle pace.

While the session is locked, a window shown or hidden runs no turn. One turn runs when it unlocks, or before a chord or a CLI line, so both act on the desktop as it is. A dark display holds no turn back: a program that drives the desktop reads the screen whether or not the display is on, and a window it opens is tiled at once. An open panel is dismissed when the session locks or the display turns off.

A burst of window events is spaced. The first runs its turn at once, and those that follow close behind share one turn (`TurnFloor`).

The hook thread turns by itself too: often for a moment after a key, a message, or a foreground change, and while keys wait for a workspace, and seldom otherwise.
