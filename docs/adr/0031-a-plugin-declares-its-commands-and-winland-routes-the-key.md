# A plugin declares its commands and Winland routes the key

A plugin can reach past its bar slot through the manifest and `IPluginContext`, so a feature like dictation needs no code in the shell or the app. It declares the commands it answers. Winland owns the key that runs one.

A manifest's `commands` names what the plugin answers to. Each has a `name`, a `summary`, and `hold` when the plugin hears the key go down and come up. In `.config\winland\binds.json` a command is `plugin:<id>:<name>`. Winland reads that as a bind to the `global` dispatcher naming `winshell:<id>:<name>`, and sends the key-down and the key-up to the sink the shell gave it. The shell routes that to the plugin, or to the enabled clone that stands in for it. The plugin hears it on `ICommandPlugin.OnCommand` with `Run`, `Down`, or `Up`. A held command keeps `"hold": true`, and only a plugin command can be held. The name is opaque to Winland. The shell parses `winshell:` and routes it.

A command the shell runs itself, a host action, an app role, or a panel, is a global bind named `winoma:` and the chord id (`lock`, `app.terminal`, `panel:winoma.audio`). Winland stores that name as opaque text. The shell parses it. The Menu is the `winoma.menu` plugin, so its bind is `winshell:winoma.menu:open`, the same path as any other plugin.

The manifest's `chords` are written into the binds file Winoma ships, which a new `.config\winland\binds.json` includes. A chord the built-in map or an earlier plugin already binds stays with it. A file may include others: chords merge one at a time, the including file applies last, and `"command": null` removes a chord. A file with no `include` is its own map. A line names a chord the shell understands, an unknown key or command is skipped, and the first duplicate in one file wins. The desktop and the keyboard hook process read it against the running plugins, so a plugin that is switched off loses its chords and the keys reach Windows.

A bind can be active only while the shell asks. Dictation's Escape is that bind, live only while a Take runs. `ClaimEscape` is what asks, and the press arrives on `IEscapePlugin.OnEscape`. An open Panel has the keyboard, so it hears Escape itself.

`ShowPill` and `FeedPill` drive the floating pill, which the host draws, so it matches the bar. The latest face wins. `SetIndicator` puts a glyph in `winoma.indicators`, and a click on it sends that glyph's command. The Host keeps the Indicator of a mode the Host owns, Idle lock and a recording, under a `host:` name, and a click on one runs its Chord command. `SetFacts` publishes strings, booleans, and numbers that `winoma desktop` prints under `plugins` and the plugin id. Every one of these may be called from any thread.

A service whose manifest sets `keepLoaded` is created once. It lives through a remount and is disposed when it is switched off or the desktop stops.

`winoma plugin commands <id>` lists a plugin's commands. `winoma plugin run <id> <command> [down|up]` sends one to the running desktop, through the pipe and the exit codes in ADR 0027. A chord id is not a CLI command.
