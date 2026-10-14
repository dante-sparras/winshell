# Files Winshell keeps

A person edits `settings.json` in `%USERPROFILE%\.config\winshell` ([../user/config.md](../user/config.md)). These are the files the shell writes for itself. Do not write `lifetime` by hand except where the workflow in `AGENTS.md` writes `replacing` before a swap.

| File | Holds |
| --- | --- |
| `lifetime` | One word the shell writes and `Winshell.Watch` reads, the same three words as Winland's. The shell writes `running` only here. A stop or a replace also writes Winland's file |
| `restarts` | The times a shell with no extension started again. The Winoma-armed watcher uses `.config\winoma\restarts` |
| `winshell.log` | What the shell host did before Winoma takes the log. Loaded Winoma writes `ready` in `winoma.log`. The previous chunk is `winshell.log.1` |
| `plugins\<id>\` | A plugin the person added. Built-ins stay in `plugins` beside the program. An `winoma.` id is reserved (ADR 0026) |

One shell runs. Its lock is `Local\Winshell`, and a second start leaves quietly. Loaded Winoma also holds `Local\Winoma.Desktop`. `Winshell.Watch.exe` is the shell's watcher, from a copy under `%LOCALAPPDATA%\Winshell\watch\<stamp>-<pid>`. On `running` it starts the shell again. On `replacing` it waits 30 seconds for `Local\Winshell`. On `stopped` it gives the taskbar back and leaves the shell down.
