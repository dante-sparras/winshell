# A plugin ticks when it is due

A turn of the bar loop (ADR 0029) does not tick every plugin. A plugin says how long the bar may wait after a tick, so a face that cannot have changed is not built again. Without this, every built-in built its face five times a second for as long as a panel was open or the mouse moved.

`IWinomaPlugin.NextTick()` is read just after each `Tick()`, and `PluginRuntime` skips the plugin on later turns until that wait has passed. The default is zero, which ticks on every turn. Each built-in's wait is its own `NextTick`.

The runtime still ticks a plugin on the next turn when:

- it just started, or a mount just ran,
- a gesture, panel command, field, toast command, or plugin command reached it,
- its last tick threw,
- its panel is open, or closed since the last turn.

`TickAll()` ticks every plugin regardless. The bar uses it when the theme changes and when Windows changes time zone. `TickIndicators()` ticks only the plugins that read the indicator roster, and the bar uses it when a plugin's glyph changes. `Tick(id)` ticks one plugin at once.

`TouchLater(id)` makes a plugin due on the next turn from any thread, and wakes nothing. The bar's polled snapshots call it when a read lands with a value that differs from the last, and a plugin that draws the pill is due on the turn its pill changes.

A plugin of kind `service` has no bar item. It is ticked only when it asks for a wait: on the turn after `TouchLater` names it, and from then each time that wait has passed. A service that asks for no wait is never ticked, and a command that reaches it does not make it due, so one that only answers commands costs no turn.

A face that is the same as the plugin's last one stops in the runtime, so a tick that changes nothing draws nothing.

A pointer move runs hover focus and does not speed up the loop.

## Consequences

While the loop is fast, a snapshot plugin's face can lag its data by up to its wait, the same as on an idle bar. A user plugin that wants fewer ticks overrides `NextTick`.
