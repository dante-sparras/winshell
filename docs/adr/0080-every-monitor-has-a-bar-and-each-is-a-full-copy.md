# Every monitor has a Bar, and each is a full copy

Every monitor has a Bar. Each Bar has every item, the tray, and the Indicators, and each can be clicked, right clicked, and reordered. Omarchy Quattro draws its bar once per monitor, identical in every item, and so does Winoma. With one monitor there is one Bar, built by the same code that builds a second.

One `bar` object in `.config\winshell\settings.json` describes every Bar: the items and their order. `barEdge` and `barVisible` sit beside it, and `"taskbar"` is `"hidden"` or `"shown"`. The file may include another. Objects merge, a list replaces, and the including file applies last. A `bar` object in a file with no include is the whole layout. A drag on any Bar writes that one layout, and every Bar is built again from it.

## What is one, and what is one per Bar

- **The desktop loop is one.** `DesktopLoop` holds the keyboard hook, capture, the `winoma` pipe, and Winoma's turn. It is no window. The shell draws the Bars, owns the Panel, the Toasts, and the Pill, and ticks the plugins. `DesktopLoop` hands the shell the look and the hands.
- **A Bar is a window.** `BarWindow`, in the shell, is one monitor's Bar and holds only what is its own: its handle, the monitor it is on, its mica, and whether it is behind a window that covers its monitor.
- **The plugin host is one.** `BarHost`, in the shell, holds the plugin runtime, the Panel session, the Panel window, the Pill, and the Toasts. A plugin is made once and ticks once, however many Bars there are.
- **A Bar's view is its own.** `BarView` holds one Bar's copy of every item, its reorder drag, the pointer on it, and the presses on the Bar itself. A WinUI element has one parent, so nothing built for one Bar is handed to another.

## One face, drawn on every Bar

A plugin publishes a face, which is data, and the host draws it. `SlotFan` is the plugin's slot on every Bar: it hands the one face to the slot on each. The runtime drops a face that did not change before it reaches the fan, so a tick that changes nothing draws on no Bar. No manifest changes, and a plugin cannot tell how many Bars there are.

A plugin that draws its own item (`IKitBar`) is asked to build it once for each Bar, each time with that Bar's own Kit. It keeps none of the elements it returns. It keeps its state in fields, and every copy reads that state through getters. The indicator cluster works this way: the plugin decides once whether the idle Indicators are revealed, from the pointer being on any Bar or a Panel being open (`BarAttention.Across`), and the strip on each Bar reads that. So the idle Indicators come and go on every Bar together.

A script item runs its program once, and what it prints is drawn on every Bar.

The Workspace list is the same on every Bar, and each Bar marks the Workspace in front on its own monitor (ADR 0079). A Bar that marked the Workspace with the keyboard would name a Workspace its monitor does not show. The plugin still publishes one list: each chip names the monitor its Workspace is in front on (`BarChip.Monitor`), and the Kit of each Bar, which the host made for that monitor, draws the mark only for its own (`ChipBody.On`). The plugin is not told which Bar it draws on.

## Each monitor is inset by its own Bar

`BarReserve` gives each monitor the work area left by the Bar that lies on it, as thick as that Bar is. A Bar is 48 device-independent pixels, so on a monitor with a larger scale it is more pixels thick, and that monitor loses more. A hidden Bar reserves nothing on any monitor. Windows is not told: there is no appbar and no work-area write. That is the only reservation, with or without an extension (ADR 0176). The shell pushes the bar windows and Winland reads them. `ScreenMetrics` reads the monitor rectangle, so an app bar would not move a tile.

Each Bar yields by itself. A window that fills a monitor, or a fullscreen window of the Workspace in front there, puts that monitor's Bar behind it and no other. The shell asks the extension's yield hand, and with no extension the Bars stay in front. The gateway keeps, for each Bar, the window it went behind.

## Monitors that come and go

`BarSet` matches the Bars to the monitors the Host lists, by rectangle. A Bar whose monitor is still listed stays. A monitor with no Bar gets one. With more Bars than monitors the newest are closed, so the first Bar is never closed, and a Bar that lost its monitor takes one that has none. A monitor at a new size keeps its Bar. With no monitor listed nothing changes.

The shell looks on its turn and when Windows reports a display change. When a Bar was made or closed, every Bar is built again from the layout, as after a move to another edge. The plugins that are not kept loaded start again then. The tiles follow at the next arrange (ADR 0079).

## What `winoma desktop` says

`bars` lists one Bar for each monitor, with `monitor`, `visible`, and `yields`. It replaced the single `bar` object (ADR 0066).

## What a second Bar costs

Not measured on a second screen yet. By design, a second Bar adds:

- one WinUI window with its own mica, and one more copy of every item's elements;
- one more draw of each face that changed, such as System usage once a second;
- one more read of each self-drawn item's getters on a tick;
- one more walk of the top-level windows each time the Bars' layers are looked at, and one more window rectangle read on a turn.

It does not add a plugin, a tick, a poll, a program run, a Panel window, or a Pill. A Toast is a window on each monitor for as long as it shows, and one spare window is kept for each. With one monitor none of the above is paid.

## Considered options

- **A lighter Bar on the other monitors**, with the Workspace list and the clock only. It needs the same two splits, of the loop from the window and of the host from the view, and then a second way to draw a Bar beside the first. It would save perhaps a fifth of the work, and Omarchy Quattro has no such bar.
- **A Bar on one monitor only, or a setting for which.** Omarchy Quattro has neither.
- **One window spanning every monitor.** Monitors differ in scale and need not share an edge.
- **Handing a new Bar the faces already published, without building the others again.** Every Bar would then need each plugin's last face kept for it. Monitors change rarely, and building again is the path an edge move already takes.
- **Telling the plugin which Bar it draws on.** `IPluginContext` carries nothing for one plugin (ADR 0045), and a plugin that knew would have to be written for two monitors.

## Which monitor a surface opens on

Every surface that is not a Bar item follows Omarchy Quattro's rule for it. A surface is on a monitor by the Bar it is placed against, and Core decides which Bar.

- **A Panel opened by a click** opens under the Bar that was clicked: a click on an item, or a right click on a Bar itself.
- **A Panel opened by a Chord, by Tab, or by `winoma summon`** opens where a Panel is already open, and otherwise on the monitor with the keyboard (ADR 0079). `winoma summon` takes no monitor (ADR 0027). A Panel that is refreshed stays where it is. `PanelSession.BarFor` is this rule and the one above, and `SlotFan` asks it for each open.
- **One Panel is open at a time, across every monitor.** It is one window. Escape closes it, and so does a click outside it. A click on the Bar it is on switches Panels in one click, as with one Bar. A press on another monitor's Bar, on an item or on the Bar itself, only dismisses: the release of that press is no click (`PanelSession.PointerDown`, `PanelSession.BarPressed`). The item whose Panel is open is marked on every Bar.
- **The Menu** is a Panel, so the same rule names its monitor, and it opens at the middle of that monitor (ADR 0049).
- **Toasts are drawn on every monitor**, the same list at each Bar's outer corner (ADR 0036). A toast is one entry with a window on each monitor. Its time, the pointer resting on it, a click, and a dismissal belong to the entry, so it leaves every monitor together (`ToastMonitors`). A monitor that a window covers draws no new toast, and a stack too tall for one monitor loses its oldest toast on all.
- **The Pill** is on the monitor of the window that has the keyboard. It asks Windows for that window, not a Bar, so a Bar on every monitor changed nothing for it.
- **The open Panel is layered with the Bar it is on.** A fullscreen window on another monitor does not cover it.
- **Windows' own notification windows** are put against the Bar on the monitor Windows opened them on (`BarSet.On`).
- **The idle Indicators** come up on every Bar while the pointer is on any Bar or a Panel is open (`BarAttention.Across`).

`winoma desktop` says which monitor the open Panel is on as `panelMonitor`.

A right or middle click on an item of another monitor's Bar still reaches that item's plugin after it dismissed the Panel, as it reaches it past an open Panel on its own Bar. Only the click that would open or switch a Panel is spent on the dismissal.

Turned down:

- **A Panel window for each monitor.** One Panel is open at a time, so a second window would only ever be hidden.
- **Letting `winoma summon` name a monitor.** Quattro's word takes none, and a script moves the keyboard with `next-monitor` first.
- **Dismissing from the watch for presses anywhere on screen when the press is on another Bar.** The watch runs beside the Bar's own report of the press. Whichever came second would find no Panel up, and the release would open one on that Bar.
- **Toasts on the monitor with the keyboard only.** Quattro draws its notifications on every monitor, and only its volume and brightness display on the focused one.
