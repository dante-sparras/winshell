# Command line

`winshell` talks to the instances that are running and checks this PC's setup. `winshell run` starts a shell. `winshell` with no arguments prints this summary, and so does `winshell --help`. `winshell <command> --help`, `-h` or `/?` prints that command's usage and does not run the command, and so does `winshell help <command>`. A word that is not a command prints `Unknown command "<name>". Run winshell --help.`, whatever follows it, and a first word after `ipc` or `inspect` that is not one of theirs prints `Unknown ipc command "<name>". Run winshell ipc --help.`, or the same for `inspect`. Both exit with code 2. So does an option the command does not take: `winshell list --force` prints `winshell list does not take "--force".` Every command takes `-v`. With any command but `help`, `-h` prints that command's help and `-V` the version, and it exits. `winshellw`, and `winshell` with only the options under [Run a shell](#run-a-shell), still start a shell. That is how the Start tile and the sign-in entry launch it.

```text
winshell run [options]              start a shell
winshell                            print this help
winshell <command> --help           help for that command, which does not run
winshell list [-j]                  list running instances
winshell kill [--force] [instance options]
                                    stop an instance and wait until it exits
winshell reload [instance options]
                                    reload an instance's configuration
winshell dev [-c <name> | -p <path>]
                                    run a project and apply each edit to it
winshell log [-f] [-t N] [file]     print an instance's log
winshell ipc show [-j]              list IPC targets
winshell ipc call <target> <function> [args...]
winshell ipc prop get <target> <property>
winshell ipc wait <target> <signal>
winshell ipc listen <target> <signal>
winshell inspect tree | screenshot <file> | click <name> | hover <name>
                                    look at a running shell, or act on it
winshell doctor [config options] [-j] [--no-color]
                                    check this PC's setup for Winshell
winshell help [command]             print this summary, or that command's help
```

## Run a shell

`winshell run` with no project runs the default shell, which is built in. With a project it builds to `bin\shell` when a source is newer than that program, then starts the program. A failed build does not start the last program that built. Options may come before `run`. [Where a configuration lives](../../README.md#where-a-configuration-lives) explains which project it picks. A command may also come after its options, so `winshell -c work kill` stops that config instead of starting it.

| Option | What it does |
| --- | --- |
| `-p`, `--path <path>` | Builds and runs this `.csproj`, or the one `.csproj` in this folder. |
| `-c`, `--config <name>` | Builds and runs the project in `<name>` under the config folder, `%APPDATA%\winshell` or `WINSHELL_CONFIG_DIR`. The name is one folder: use `-p` for a path. |
| `--no-build` | Runs the program the last build left in `bin\shell`. Does not build, so a newer source is left unbuilt. The sign-in entry uses this. |
| `-d`, `--daemonize` | Starts the shell in the background, with no console window, and returns at once. |
| `-n`, `--no-duplicate` | Exits 0 without starting, and without a message, when this shell id is already running. Two starts that overlap do the same: only one of them keeps running. |
| `-v`, `--verbose` | Also logs debug messages. |
| `-V`, `--version` | Prints the version, such as `winshell 0.6.0+e8d05d1…` for a build from source, and exits. |
| `-h`, `--help` | Prints `run`'s usage, or with no command the summary, and exits. |

The program a project builds also takes `--no-duplicate` and `-d` on its own command line. `--no-duplicate` exits at once when its shell id is already running. `-d` starts that program again and returns. A start that carries `--after` keeps it.

The log goes to the console and to the instance's log file. The file is kept to about 4 MB, and the previous part is `log.1.txt` beside it. Ctrl+C in the console stops the shell.

A failure before that log exists, such as no project and no default shell to run, is printed to the console. The Start tile, the sign-in entry and any other start with no console window also show that message in a dialog, unless another program is reading the error. **Open config folder** opens the `--path` folder, or the config folder when there was no `--path`. An attached console prints the message and shows no dialog.

A build that fails prints why and does not start a program. `Building a configuration needs the .NET SDK, from https://dot.net/download.` is the message when the SDK is missing. A downloaded `.csproj`, or a `.props` or `.targets` beside it, is refused before the build: the file name, then `was downloaded from the internet. It runs as you.`

Any other option is an error, and exits with code 2. An option winshell does not have prints `Unknown option "<name>".`, and one of another command prints `winshell run does not take "<name>".`

## Pick an instance

`kill`, `reload`, `log`, `ipc` and `inspect` act on one running instance:

| Option | Picks |
| --- | --- |
| `-i`, `--id <id>` | The instance with this id, or the one whose id starts with it |
| `--pid <pid>` | The instance with this process id |
| `-p`, `--path`, `-c`, `--config` | The instance running that configuration |
| None | The only running instance. With several, the command exits with 2 and lists them. |

## Commands

| Command | What it does |
| --- | --- |
| `winshell list` | Lists the running instances with their id, process id, shell id, configuration, launch time and log file. `-j`, `--json` prints them as JSON. Stopped instances are not listed. The shape is under [JSON](#json). |
| `winshell kill` | Asks an instance to exit, waits until that process has exited, and prints `Killed <id>`. `--force` ends the process when it does not exit. `kill` is the one command a CLI and a shell of different releases still accept. |
| `winshell reload` | Builds the configuration. On failure the running instance stays, a notice shows, and the command prints why and exits with 1. On success a new instance starts with `--after` and takes over, then the command prints `Reloaded <id>`. The default shell has nothing to build, so it starts again. There is no file watcher. |
| `winshell dev` | Runs a configuration project under `dotnet watch` and applies each edit to the running shell, until Ctrl+C. See [Develop a shell](../configuration.md#develop-a-shell). Takes `-c` and `-p`. |
| `winshell log` | Prints an instance's log. `-i`, `--pid`, `-p` and `-c` can also name a stopped instance. With no selector and no instance running, it prints the log of the newest stopped one and says so on stderr. A selector that matches nothing is an error. `-t`, `--tail <n>` prints the last `n` lines; `-f`, `--follow` keeps printing new lines until you press Ctrl+C. `winshell log <file>` prints that file. |
| `winshell ipc show` | Lists the instance's IPC targets with their functions, properties and signals. `-j`, `--json` prints that list as JSON. The shape is under [JSON](#json). |
| `winshell ipc call <target> <function> [args...]` | Calls a function registered with [`ShellIpc.Handle`](winshell.md#shellipc) and prints what it returns. |
| `winshell ipc prop get <target> <property>` | Prints a property registered with `ShellIpc.Property`. |
| `winshell ipc wait <target> <signal>` | Waits for a signal once and prints its first argument. |
| `winshell ipc listen <target> <signal>` | Prints the first argument of each signal until you press Ctrl+C. |
| `winshell inspect tree` | Prints each open window of an instance as JSON: its kind, place and size, and its live XAML tree, with each element's type, `x:Name`, automation id and name, position in the window, size and what it holds, such as `text`, `value`, `on`, `checked` or `selectedIndex`. What the window's open flyouts and menus show is under `popups`, each placed from its own corner. A password is left out. |
| `winshell inspect screenshot <file> [window]` | Saves a window's content as a PNG at its real size in pixels. The system draws a backdrop such as Mica, so a capture sits on the plain dark or light color of the theme instead. A `[window]` that names the type of an open popup's content, such as `MenuFlyoutPresenter`, saves that popup. |
| `winshell inspect click <name> [window]` | Activates an element through its UI Automation peer, as Narrator does: it invokes a button, toggles a toggle and opens what expands. A disabled control refuses. Nothing goes through Windows' input. |
| `winshell inspect hover <name> [window]` | Puts a control in its `PointerOver` look. A `PointerEntered` handler does not run, because XAML has no way to raise one from code. |
| `winshell doctor` | Checks this PC's setup and prints one line per check, with a fix under each warning and failure. It reads a project as data and never builds. `-c` and `-p` pick a configuration, as they do when running a shell; with neither, it checks the one Winshell would run. `-j`, `--json` prints the checks as JSON. `--no-color` turns color off, as does `NO_COLOR` or a redirected output. It exits with 1 when a check fails, and it changes nothing. See [winshell doctor](../doctor.md). |

In `winshell inspect`, a `[window]` is the type of a window's content, such as `TaskbarView`, or its `window` number in `tree`. Without one, `screenshot` takes the first window, and `click` and `hover` search every window. A `<name>` is an `x:Name`, an automation id or an automation name, in a window or in its open popups. A name that matches more than one element acts on none: the error lists each match with its window number, content type, element type, name, id and label, so a window number narrows it down when two taskbars both have a `StartButton`. A match inside another counts as the outer one, such as a button and the card in it that carry the same label.

`winshell ipc call notice dismiss` closes Winshell's own notice for a build error or a failure. That notice is not a target the configuration registered, so `ipc show` does not list it. It takes no arguments.

The default shell has one IPC target, `settings`: `winshell ipc call settings open <page>` opens [Winshell Settings](../settings.md#opening-it).

A CLI and a shell whose releases differ, in major.minor.patch, refuse each other. The message is `This shell is Winshell <shell> and this winshell is <client>. Use the winshell that matches, or build the shell with this one.` `kill` is exempt.

An IPC request longer than 256 KiB is refused, which no command line can reach. `winshell ipc listen` keeps the latest signals when the client falls behind, and the shell releases that subscription when the client disconnects, even if the signal never fires.

A `--` ends options. Words after it are arguments, so `winshell log -- -f` prints the file `-f`. `ipc call` does the same after the function name, with no `--`: `winshell ipc call notes add -v` passes `-v` to `add`. `--after <pid>` is removed while options are still being read. A reload and an update's restart put it there, so the new process waits until that process has exited. After `--`, and after an `ipc call` function name, those words stay arguments.

Output sent to a pipe or a file is UTF-8 without a byte order mark. In a console window it follows the console's code page. To read it in PowerShell, set `[Console]::OutputEncoding = [Text.UTF8Encoding]::new()` first, or PowerShell decodes it with the console's code page.

## JSON

`-j` and `--json` on `list` and `ipc show` write JSON to stdout. Characters outside ASCII are written as themselves, the same way [`winshell doctor --json`](../doctor.md) writes a path. `doctor --json` keeps the shape that page shows.

`winshell list --json` prints a JSON array, one object per running instance:

| Field | What it is |
| --- | --- |
| `id` | The instance id. |
| `pid` | The process id. |
| `shellId` | The shell id. |
| `configPath` | The program that is running. |
| `launchTime` | When it started, as a local time with its offset. |
| `pipe` | The name of its control pipe. |
| `log` | Its log file. |

The array does not include `processStart` or `exitedAt`. Those stay in `instance.json`, the file the instance writes for itself, so that file can change without changing this command.

`winshell ipc show -j` prints a JSON array, one object per target, in ordinal order of the target name. Members stay in the order they were registered. A function `ipc call` cannot reach is left out, and so is a property of a type other than `string`, `int`, `bool`, `real`, `double` or `color`.

| Field | What it is |
| --- | --- |
| `target` | The target name. |
| `functions` | The functions `ipc call` can reach. Each has `name`, `parameters` and `returns`. |
| `properties` | The properties `ipc prop get` can read. Each has `name` and `type`. |
| `signals` | The signals registered on the target. Each has `name` and `parameters`. |

A parameter has `name` and `type`. `type` is null when the signal's parameter was declared without one.

```json
[
  {
    "target": "notes",
    "functions": [
      {
        "name": "hello",
        "parameters": [ { "name": "name", "type": "string" } ],
        "returns": "string"
      }
    ],
    "properties": [
      { "name": "title", "type": "string" }
    ],
    "signals": [
      {
        "name": "opened",
        "parameters": [ { "name": "query", "type": "string" } ]
      }
    ]
  }
]
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done. For a shell, it was stopped. |
| 1 | No such configuration or instance, a build failed, an IPC call failed, the instance couldn't be reached or was still running, or a `winshell doctor` check failed. |
| 2 | An unknown command or option, an option that needs a value without one, a spare argument, a `--pid` or `--tail` that is not a whole number, or an `ipc` command missing its arguments. |

`--pid` and `--tail` need a whole number. Anything else prints a message such as `--pid needs a whole number` and exits with code 2.

An unexpected failure of any command but `run` prints `winshell <command>: <message>` and exits with 1. `-v` adds the stack trace. Starting a shell still writes a crash report.

## Environment variables

| Variable | What it does |
| --- | --- |
| `WINSHELL_CONFIG_DIR` | The folder configurations load from, instead of `%APPDATA%\winshell`. |
| `WINSHELL_LOCAL_DIR` | The folder for logs, crash reports, and each shell's data, state and cache, instead of `%LOCALAPPDATA%\winshell`. |
| `WINLAND_PIPE` | The named pipe [`Winshell.Winland`](winshell-winland.md) connects to, instead of `winland-<session id>` and then `winland`, which it tries in that order when the variable isn't set. |

## Files

| What | Where |
| --- | --- |
| Configurations | `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` |
| A project's program | `bin\shell` under the project folder |
| The default shell | Built in. It is not a folder Winshell writes. Its shell id is `winshell-default`. |
| Per-shell data, state and cache | `%LOCALAPPDATA%\winshell\data\by-shell\<shell id>`, and the same under `state` and `cache`. They are `Shell.DataDir`, `Shell.StateDir` and `Shell.CacheDir`. |
| Instance logs | `%LOCALAPPDATA%\winshell\run\<instance id>\log.txt`. A shell that exits cleanly keeps that folder, with `exitedAt` set in `instance.json`, so `winshell log` can still print it. A later start deletes a stopped folder once it is a day old, including a folder that has no `instance.json`. |
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one text file per crash. Nothing is sent anywhere. See [troubleshooting](../troubleshooting.md#winshell-crashed). |
| Run at sign-in | Unpackaged: the `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written only when a shell sets [`StartupService.RunAtSignIn`](winshell-session.md#startupservice). That entry passes `--no-build`. Packaged: the package's startup task, not that value. |

The console writes `LEVEL category: message`. The log file writes that same line with `HH:mm:ss.fff` in front. `INFO` and `WARN` have a leading space, on both. The levels are `DEBUG` (with `-v`), `INFO`, `WARN`, `ERROR` and `FATAL`.
