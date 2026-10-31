# Command line

`winshell` talks to the instances that are running and checks this PC's setup. `winshell run` starts a shell. `winshell` with no arguments prints this summary, and so does `winshell --help`. `winshell <command> --help`, `-h` or `/?` prints that command's usage and does not run the command. `winshellw`, and `winshell` with only the options under [Run a shell](#run-a-shell), still start a shell. That is how the Start tile and the sign-in entry launch it.

```text
winshell run [options]              start a shell
winshell                            print this help
winshell list [-j]                  list running instances
winshell kill [--force] [instance options]
                                    stop an instance and wait until it exits
winshell log [-f] [-t N]            print an instance's log
winshell ipc show                   list IPC targets
winshell ipc call <target> <function> [args...]
winshell ipc prop get <target> <property>
winshell ipc wait <target> <signal>
winshell ipc listen <target> <signal>
winshell doctor [config options] [-j] [--no-color]
                                    check this PC's setup for Winshell
```

## Run a shell

`winshell run` loads a configuration and runs it until it is stopped. Options may come before `run`. [Getting started](../guide/getting-started.md#where-your-configuration-lives) explains which configuration it picks. A command may also come after its options, so `winshell -c work kill` stops that config instead of starting it.

| Option | What it does |
| --- | --- |
| `-p`, `--path <path>` | Runs this `shell.qml`, or the `shell.qml` in this folder. |
| `-c`, `--config <name>` | Runs `<name>\shell.qml` in the config folder, `%APPDATA%\winshell` or `WINSHELL_CONFIG_DIR`. The name is one folder: use `-p` for a path. |
| `-d`, `--daemonize` | Starts the shell in the background, with no console window, and returns at once. |
| `-n`, `--no-duplicate` | Exits 0 without starting, and without a message, when this configuration is already running. Two starts that overlap do the same: only one of them keeps running. |
| `-v`, `--verbose` | Also logs debug messages. |
| `-V`, `--version` | Prints the version, such as `winshell 0.6.0+e8d05d1…` for a build from source, and exits. |
| `-h`, `--help` | Prints the summary and exits. |

The log goes to the console and to the instance's log file. The file is kept to about 4 MB, and the previous part is `log.1.txt` beside it. Ctrl+C in the console stops the shell. If the configuration fails to load, Winshell keeps running with no windows and loads it again when its files change.

Any other option is an error. The command prints `Unknown option "<name>".` and exits with code 2.

## Pick an instance

`kill`, `log` and `ipc` act on one running instance:

| Option | Picks |
| --- | --- |
| `-i`, `--id <id>` | The instance with this id, or the one whose id starts with it |
| `--pid <pid>` | The instance with this process id |
| `-p`, `--path`, `-c`, `--config` | The instance running that configuration |
| None | The only running instance. With several, the command exits with 2 and lists them. |

## Commands

| Command | What it does |
| --- | --- |
| `winshell list` | Lists the running instances with their id, process id, shell id, configuration, launch time and log file. `-j`, `--json` prints them as JSON. Stopped instances are not listed. |
| `winshell kill` | Asks an instance to exit, waits until that process has exited, and prints `Killed <id>`. `--force` ends the process when it does not exit. |
| `winshell log` | Prints an instance's log. `-i`, `--pid`, `-p` and `-c` can also name a stopped instance. With no selector and no instance running, it prints the log of the newest stopped one and says so on stderr. A selector that matches nothing is an error. `-t`, `--tail <n>` prints the last `n` lines; `-f`, `--follow` keeps printing new lines until you press Ctrl+C. `winshell log <file>` prints that file. |
| `winshell ipc show` | Lists the instance's IPC targets with their functions, properties and signals. |
| `winshell ipc call <target> <function> [args...]` | Calls a function of an [`IpcHandler`](winshell-io.md#ipchandler) and prints what it returns. |
| `winshell ipc prop get <target> <property>` | Prints a property of an `IpcHandler`. |
| `winshell ipc wait <target> <signal>` | Waits for a signal of an `IpcHandler` once and prints its first argument. |
| `winshell ipc listen <target> <signal>` | Prints the first argument of each signal until you press Ctrl+C. |
| `winshell doctor` | Checks this PC's setup and prints one line per check, with a fix under each warning and failure. `-c` and `-p` pick a configuration, as they do when running a shell; with neither, it checks the one Winshell would run. `-j`, `--json` prints the checks as JSON. `--no-color` turns color off, as does `NO_COLOR` or a redirected output. It exits with 1 when a check fails, and it changes nothing. See [winshell doctor](../doctor.md). |

`winshell msg` is the old name of `winshell ipc call`; it still works and says so. There is no command that reloads a running shell yet ([#134](https://github.com/dante-sparras/winshell/issues/134)); save a file, or call [`Shell.reload()`](winshell.md#shell) from an `IpcHandler`.

The default shell has one IPC target, `settings`: `winshell ipc call settings open <page>` opens [Winshell Settings](../settings.md#opening-it).

A `--` ends options. Words after it are arguments, so `winshell log -- -f` prints the file `-f`. `ipc call` and `msg` do the same after the function name, with no `--`: `winshell ipc call notes add -v` passes `-v` to `add`. `--after <pid>` is removed while options are still being read, which is where an update's restart puts it. After `--`, and after an `ipc call` or `msg` function name, those words stay arguments.

Output sent to a pipe or a file is UTF-8 without a byte order mark. In a console window it follows the console's code page. To read it in PowerShell, set `[Console]::OutputEncoding = [Text.UTF8Encoding]::new()` first, or PowerShell decodes it with the console's code page.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done. For a shell, it was stopped. |
| 1 | No such configuration or instance, an IPC call failed, the instance couldn't be reached or was still running, or a `winshell doctor` check failed. |
| 2 | An unknown command or option, an option that needs a value without one, a spare argument, a `--pid` or `--tail` that is not a whole number, or an `ipc` command missing its arguments. |

`--pid` and `--tail` need a whole number. Anything else prints a message such as `--pid needs a whole number` and exits with code 2.

An unexpected failure of `list`, `kill`, `log`, `ipc` or `doctor` prints `winshell <command>: <message>` and exits with 1. `-v` adds the stack trace. Starting a shell still writes a crash report.

## Environment variables

| Variable | What it does |
| --- | --- |
| `WINSHELL_CONFIG_DIR` | The folder configurations load from, instead of `%APPDATA%\winshell`. With it set, an old `%USERPROFILE%\.config\winshell` isn't copied. |
| `WINSHELL_APP_ID` | Sets `Shell.appId` when the configuration has no `//@ pragma AppId`. |
| `WINSHELL_DISABLE_FILE_WATCHER` | Any value stops the shell from reloading when its files change. |
| `WINSHELL_NO_RELOAD_POPUP` | `1` hides Winshell's notice for a configuration that failed to load. The notice for a window that failed still shows. |
| `WINLAND_PIPE` | The named pipe [`Winshell.Winland`](winshell-winland.md) connects to, instead of `winland-<session id>` and then `winland`, which it tries in that order when the variable isn't set. |

A configuration can set variables for its own process with `//@ pragma Env NAME=value` ([configuration guide](../guide/configuration.md#pragmas)).

## Files

| What | Where |
| --- | --- |
| Configurations | `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` |
| The default shell | `%LOCALAPPDATA%\winshell\default-shell`, written when Winshell runs it and no other instance is running that shell. A `.qml` or `.js` file this build does not ship is removed. |
| Per-shell data, state and cache | `%LOCALAPPDATA%\winshell\data\by-shell\<shell id>`, and the same under `state` and `cache`. They are `Shell.dataDir`, `Shell.stateDir` and `Shell.cacheDir`. |
| Instance logs | `%LOCALAPPDATA%\winshell\run\<instance id>\log.txt`. A shell that exits cleanly keeps that folder, with `exitedAt` set in `instance.json`, so `winshell log` can still print it. A later start deletes a stopped folder once it is a day old, including a folder that has no `instance.json`. |
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one text file per crash. Nothing is sent anywhere. See [troubleshooting](../guide/troubleshooting.md#winshell-crashed-or-a-surface-closed). |
| Run at sign-in | The `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written only when a shell sets [`Startup.runAtSignIn`](winshell.md#startup) |

The console writes `LEVEL category: message`. The log file writes that same line with `HH:mm:ss.fff` in front. `INFO` and `WARN` have a leading space, on both. The levels are `DEBUG` (with `-v`), `INFO`, `WARN`, `ERROR` and `FATAL`.
