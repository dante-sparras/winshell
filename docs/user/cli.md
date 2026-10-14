# The `winshell` command

`winshell.exe` sits next to `Winshell.Host.exe`. It asks the shell the way `qs ipc` asks Quickshell. The same words work whether Winoma is loaded or the shell is hosting plugins on its own.

```powershell
winshell summon winoma.clock
winshell summon winoma.clock tabs
winshell dismiss
winshell plugin run winoma.dictation toggle
winshell state
winshell state -j
winshell stop
```

`-j` may sit on `state`. `winshell` with no words prints the usage and exits 1.

| Invocation | Exit | Prints |
| --- | --- | --- |
| `winshell summon <id> [<command>]` | 0 | `ok`, or the sentence when that id is not running |
| `winshell dismiss` | 0 | `dismissed` |
| `winshell plugin run <id> <command> [down\|up]` | 0 | `ok` |
| `winshell state` | 0 | Several lines: `panel <id>` or `panel none`, then `row …`, `bar <monitor> <edge> <plugin ids>`, and `plugin <id> <name>=<value>` |
| `winshell state -j` | 0 | One JSON line: `panel`, `rows`, `bars`, `plugins`, and `extensions` |
| `winshell stop` | 0 | `stopped`. The shell exits, the taskbar comes back, and `stopped` is written only in `.config\winshell\lifetime`. Winland keeps running |
| Winshell is not running | 1 | `Winshell is not running` on stderr |
| the shell did not answer in time | 1 | `the shell did not answer` on stderr |
| the shell is stopping | 1 | `the shell is stopping` on stderr |
| words the shell does not read | 1 | `Usage: …` or `Unknown command '…'.` on stderr |

`state` without `-j` is more than one line. `-j` is one line. A summon that the roster refuses still exits 0 and prints the sentence.
