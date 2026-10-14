# Decisions

One line per ADR, by area. Read the ADRs for an area before changing how it behaves.

An ADR states one standing decision and why, in the present tense, and is true against the code. When a decision changes, rewrite its ADR in place and fix its line here. Git keeps the history. A new ADR takes the next number after the highest in any of the three repos, so a number names one decision in Winland, Winshell, and Winoma.

## Products

- [Winoma ADR 0176](https://github.com/dante-sparras/winoma/blob/main/docs/adr/0176-three-products.md) Winland, Winshell, and Winoma are three products. The shell's application is the only one, and the shell draws the bar, panel, toast, and pill. An extension hands it a look and hands. `Winshell.Core` references only `Winland.Ipc`.
- [Winoma ADR 0177](https://github.com/dante-sparras/winoma/blob/main/docs/adr/0177-three-products-are-three-repos.md) The three products are three repos. Winshell builds on the Winland.Ipc package and publishes Winshell.Core and Winshell.Kit as packages Winoma pins. A bug in another product is an issue in that product's repo.
- [Winland ADR 0059](https://github.com/dante-sparras/winland/blob/main/docs/adr/0059-the-desktop-writes-down-the-windows-it-hides.md) `Winshell.Watch` waits for `Local\Winshell` and gives the taskbar back. It runs from a copy, so the live folder can be overwritten.

## Bar and the loop

- [0080](0080-every-monitor-has-a-bar-and-each-is-a-full-copy.md) Every monitor has a Bar, and each is a full copy drawn from one `bar` object. A plugin's one face is drawn on each, each Bar marks the Workspace in front on its own monitor, and each monitor is inset by its own Bar. A Panel and the Menu open on the Bar that was clicked, where one is open, or on the monitor with the keyboard, and Toasts are drawn on every monitor.
- [0029](0029-the-bar-loop-follows-events.md) Events drive the bar. Its loop is a safety net with a fast, an idle, and an away pace.
- [0041](0041-a-plugin-ticks-when-it-is-due.md) A plugin is ticked only when its `NextTick` wait has passed or an event makes it due.

## Panels, plugins, and the Kit

- [0045](0045-a-plugin-draws-with-the-kit.md) A plugin draws its panel and bar item only with the Kit. `IPluginContext` carries nothing for one plugin.
- [0048](0048-the-manifest-says-which-plugin-has-a-panel.md) The manifest's `panel` kind is what a panel chord and Tab reach. The tray leaves it out, and summon is wider. None of them names a monitor.
- [0035](0035-an-open-panel-follows-a-workspace-switch.md) An open panel is Winoma's own window, so a Workspace switch on any monitor leaves it open with the keyboard. Raw keyboard input stops while Winoma has the keyboard.
- [0026](0026-a-plugin-asks-for-its-capabilities.md) A plugin lists the capabilities it wants. `winoma.` ids are reserved, and a clone stands in for a built-in.
- [0031](0031-a-plugin-declares-its-commands-and-winland-routes-the-key.md) A plugin declares its commands, and Winland routes the key. The plugin owns the Escape claim, the pill, the indicator, and the facts. A binds file may include another. A file with no include is its own map.
