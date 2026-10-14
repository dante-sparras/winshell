# A plugin draws with the kit

A plugin draws its own panel and bar item with the Kit. The host does not draw a panel from a document a plugin sends: when it did, every new idea needed a new row type in the host, half of the row types served one plugin, and most changes drew the panel again from the top.

Omarchy Quattro has no document either. A plugin draws itself with the `qs.Ui` controls and reads the `qs.Commons` tokens, and QML bindings repaint only what changed. Winoma works the same way, in WinUI.

- `src/Winshell.Kit` is the Kit: the controls a panel and a bar item are built from, and the public tokens (spacing, type scale, corners, the chrome brushes). It references Winshell.Core. The extension and the plugins reference it. Core stays free of Windows. Spacing tokens take Omarchy's names (`PanelPadding`, `RowGap`, `ControlGap`, and the rest). The type scale keeps the Windows ramp's names and sizes, because the chrome is Windows (ADR 0016). The brushes live in the shell's `App.xaml`. The shell draws the bar, the panel, the toast, and the pill with the Kit, and those types live in `src/Winshell/Surfaces`. The extension does not keep a second copy of the brushes or of those windows.
- The Kit holds what more than one plugin draws. Controls take `qs.Ui`'s names where one matches (`PanelHero`, `PanelSlider`, `ToggleSwitch`, `ButtonGroup`, `TextField`), plus `Card`, `ListItem`, `Meter`, `Figures`, `Note`, `KeyedList`, and `BarButton`. A drawing only one plugin needs, such as the forecast or the calendar, lives in that plugin and is built from Kit pieces and tokens. It moves into the Kit when a second plugin needs it.
- The host calls `BuildPanel(Kit)` once when a panel opens. A Kit control takes a getter rather than a value, and while the panel is open the Kit reads every getter on each tick and writes to WinUI only what changed, the way a QML binding does. A getter must be cheap. A plugin that has to fetch before its getters are current does it in `UpdatePanel()`. A `KeyedList` reconciles its items by key. The panel is built again only when the plugin calls `Rebuild()`.
- Every interactive Kit control takes a command name and a handler. The control reports its type, label, value, and command to the panel outline, so `winoma desktop` prints it and `winoma summon <id> <command>` presses it. Anything a plugin draws outside the Kit is invisible to the CLI.
- A bar item is a Kit `BarButton`, driven by `Publish(BarFace)`. A plugin with an unusual item, such as the workspace chips, builds its own from the Kit. The bar, the panel, the Pill, and the Toasts are the shell's surfaces, drawn with the Kit.
- A sheet's rows are drawn with the Kit by the host's sheet plugin, and its author writes only the manifest.
- A plugin's decisions stay in a Core type with tests, such as `PowerBoard`. That type returns a model, and the plugin's view sets Kit controls from it.
- A Theme fills the Kit's tokens from its palette, and it is the Host's, not a plugin's: a plugin never reads a color, so none can own what fills them (ADR 0031).
- A source scan in `Winoma.Tests` fails on a numeric size, a resource lookup, or a hand-made color in `src/Winoma.Desktop`, `src/Winoma.Plugins`, or `src/Winshell/Surfaces`. The sizes live in `src/Winshell.Kit`.
- A user plugin loads in a collectible context, so a rebuild in its folder reloads it without a desktop restart. The log says `reload id=… pid=…`.
- Each built-in is one folder in `src/Winoma.Plugins`, holding its manifest and its source, and the publish ships that source in a `source` folder beside the manifest. The native `winoma` client cannot load the WinUI plugin assembly, so the source travels as files, not resources. `winoma plugin clone <winoma.id>` copies it into a project that references the installed `Winoma.Core`, `Winshell.Core`, and `Winshell.Kit`, as Omarchy's clone copies the plugin folder. The Core model stays referenced, not copied.
- `winoma.gallery` is a panel with no bar item. It shows every Kit control with sample values and uses nothing but the Kit. `winoma summon winoma.gallery <section>` opens it at that control. A test fails when a Kit control is missing from the gallery or from the table in `docs/agents/plugins.md`.

## The plugin context carries nothing for one plugin

A member of `IPluginContext` is something every plugin can use. When one plugin needs the panel to do something new, the answer is a Kit control or a field on one, not a method that only that plugin calls. The same holds for the pill, toasts, the indicator, and facts.

## Considered options

- **A document the host draws, reshaped after `qs.Ui`.** That would have enforced the look and kept every row readable by the CLI, but every new idea would still wait on a new row type in the host.
- **A document with one escape-hatch row a plugin draws itself.** That would mean two ways to build a panel, and the bespoke one would be the one with the bugs.

## Consequences

A plugin references WinUI and runs its view on the bar's thread, which is where it ticks. The look holds because the Kit is the easy path and the source scan catches a stray size in this repository. A plugin outside the repository can still draw anything, as an Omarchy plugin can. A reloaded user plugin may leave its old context in memory until the desktop restarts.
