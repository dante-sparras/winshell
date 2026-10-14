# The manifest says which plugin has a panel

A panel chord, Tab in an open panel, and `right-panel:N` reach a plugin because its manifest says it has a panel, not because Core lists its id. So a clone, a sheet, and a user's Kit panel are reached like a built-in's.

- The manifest's `panel` kind says a left click on the plugin's item opens its panel. `panel:<id>`, Tab, and `right-panel:N` reach a running plugin that declares it. A built-in with a panel declares it, `winoma plugin new` writes it for a sheet, and a clone copies it. Which key opens a built-in's panel by default is still a line in Core's `ChordMap`: a manifest's `chords` bind only the plugin's own commands (ADR 0031).
- The tray draws a panel and leaves the kind out. Its item is a row of icons, not a panel widget, so Tab and the right-hand count skip it.
- A `panel:` chord for a built-in opens the clone that stands in for it, the way a plugin command goes to the clone. `winoma summon` stays exact, so a clone is summoned by its own id.
- `winoma summon` keeps a wider gate than the chords. It asks any running plugin with a bar item, or a panel plugin with none, and refuses when the click opened nothing. That is how an agent reaches the tray's panel.
- A panel chord, Tab, and `winoma summon` open the panel where a panel is already open, and otherwise on the monitor with the keyboard. A click opens it under the Bar that was clicked. `winoma summon` takes no monitor (ADR 0027). The rule is `PanelSession.BarFor` (ADR 0080).
- A plugin with `panel` and no `bar-widget`, such as the gallery, opens at the center of that monitor's Bar by its chord or by a summon. Tab and `right-panel:N` skip it, because it has no place on the bar. A built-in that declares both and is taken off the bar stays stopped.
- `binds.json` reads `panel:<id>` by its shape. The chord book skips the line with a warning when no running plugin declares that panel, as it does for a `plugin:` line.
- `PanelRoster` in Core owns these entries, over `PluginRuntime` and `PanelSession`. The runtime remembers whether a plugin's last face was hidden, so Core does not ask the bar. The Kit press that follows a summon stays in the bar host.

## Considered options

- **A type Core can see, checked after the plugin loads.** The tray would need a second rule to stay out, and the chord file could not be checked from the manifests alone.
- **Ask by clicking, as summon does.** Tab cannot list the panels without clicking each item.
- **One gate for chords and summon.** Either the CLI loses the tray's panel, or the tray joins Tab and moves the right-hand numbers.

## Consequences

A sheet or a clone on the right is counted by `right-panel:N`, so those chords move when one is added. A plugin that declares `panel` and opens nothing on a click is logged once for each mount. A plugin that opens a panel without declaring it works on a click and by summon only. A `panel:` line for a plugin that is not running is skipped, so that key reaches the focused window.
