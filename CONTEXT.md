# Winshell

Winshell is the Windows 11 shell toolkit that matches Quickshell: the Kit, the plugin host, and the surfaces a plugin draws on. It ships no widgets. It runs alone, or loads Winoma.

## Language

**Winshell**:
The shell toolkit product: the Kit, plugins, and the surfaces a plugin draws on. It ships no widgets of its own. A Bar reserves its screen edge, and the Windows taskbar is hidden only when a setting asks for it, so a Bar and the taskbar can be on screen together. What it matches is Quickshell.
_Avoid_: the Bar, the shell (meaning the whole desktop)

**Host**:
A Windows 11 PC that runs Winshell. Display layout, browser, and filesystem paths are settings of that Host.
_Avoid_: the Predator, this machine (as if it were the only one)

**Bar**:
The strip the shell draws along a screen edge, one on every monitor. Each is a full copy, and one `bar` object in `settings.json` describes them all (ADR 0080). The shell draws it and the plugins named there fill it with their faces. It reserves its edge through Winland, and a window that fills a monitor is drawn over that monitor's bar. The Windows taskbar is hidden only when a setting asks.
_Avoid_: taskbar, theme

**Panel**:
The popup a Bar widget opens, or the Bar itself on a right click. It sits under the bar and slides out from there, then back when it closes. Arrow keys, Return, Tab, and Escape work inside it. A panel Chord and Tab reach a plugin that says in its manifest that it has a Panel. The tray's Panel is not one of them.
_Avoid_: flyout, toast

**Kit**:
The controls and tokens every plugin builds its Bar item and Panel from. A plugin that draws only with the Kit looks like the rest of the Bar, and the CLI can read and press its controls.
_Avoid_: component library, theme, row

**Toast**:
A card shown at the bar's outer corner: a Notification that arrives, or a Notice. It slides in, stays five seconds, longer while the pointer is on it, and slides out. Up to three stack. It never takes focus.
_Avoid_: banner, Pill, popup

**Pill**:
The floating strip at the bottom of the screen a plugin shows while it works: a live level, progress, then a check or the reason it failed. The host draws it. It never takes focus.
_Avoid_: toast, Notification, overlay

**Indicator**:
A glyph in the indicator cluster on the Bar for a mode that is on or off. A plugin keeps the Indicator of its own mode, and the Host keeps the Indicator of a mode the Host owns: Idle lock and a recording. The cluster shows every active one, in the accent color. The others are hidden, and fade in dimmed while the pointer is anywhere on the Bar, on a widget or between two, and while the Bar has the keyboard, which is while one of its Panels is open. They fade out when the pointer leaves and the Panel closes. With none active the cluster is off the Bar until then. The Menu is not one of them. A click switches the mode, and on a pending Reminder opens the list. A pending Reminder, night light, a recording, and Idle lock switched off are Indicators. Do not disturb is the bell's own face, and Dictation is its own Bar item, so neither is drawn twice.
_Avoid_: tray icon, badge

**Plugin command**:
A command a plugin declares in its manifest. In `binds.json` it is `plugin:<id>:<name>`. A held one hears the key go down and come up. `winoma plugin run` sends one from the terminal.
_Avoid_: chord id (for a plugin's own words), hotkey

**Panel command**:
A word a plugin's Panel hears, such as `mode:balanced` or `brightness`. A click on a Kit control sends one, and so do `winoma summon` and a button on a Toast. The words are the plugin's own. One that has a control works whether or not that control is on screen, and a word the plugin does not know is refused.
_Avoid_: row command, Plugin command (which a Chord or `winoma plugin run` sends), action

**Look**:
The chrome before a Theme is chosen: accent, corner, body text size, and panel gutter. A field left empty keeps the Windows chrome. The corner styles the Bar, Panels, Menu, and Toasts. The Look's corner never reaches another program's window: those keep the corners Windows gives them, and a tiled window stays square and borderless so tiles sit flush.
_Avoid_: Theme, rounding every window
