# Command line

`winshell` runs a shell, and talks to the instances that are running. `winshell --help` prints this summary.

```text
winshell [options]                  run a shell
winshell list [-a] [-j]             list running instances
winshell kill [instance options]    stop an instance
winshell log [-f] [-t N]            print an instance's log
winshell ipc show                   list IPC targets
winshell ipc call <target> <function> [args...]
winshell ipc prop get <target> <property>
winshell ipc wait <target> <signal>
winshell ipc listen <target> <signal>
```

## Run a shell

`winshell` with no command loads a configuration and runs it until it is stopped. [Getting started](../guide/getting-started.md#where-your-configuration-lives) explains which configuration it picks.

| Option | What it does |
| --- | --- |
| `-p`, `--path <path>` | Runs this `shell.qml`, or the `shell.qml` in this folder. |
| `-c`, `--config <name>` | Runs `<name>\shell.qml` in the config folder, `%APPDATA%\winshell` or `WINSHELL_CONFIG_DIR`. |
| `-d`, `--daemonize` | Starts the shell in the background, with no console window, and returns at once. |
| `-n`, `--no-duplicate` | Does nothing when an instance of this configuration is already running. |
| `-v`, `--verbose` | Also logs debug messages. |
| `-V`, `--version` | Prints the version, such as `winshell 0.6.0`, and exits. |
| `-h`, `--help` | Prints the summary and exits. |

The log goes to the console and to the instance's log file. Ctrl+C in the console stops the shell. If the configuration fails to load, Winshell keeps running with no windows and loads it again when its files change.

`--no-color`, `--log-times`, `--any-display`, `--newest`, `--log-rules <rules>`, `--manifest <path>` and `--debug <port>` are accepted for Quickshell's command lines and do nothing.

## Pick an instance

`kill`, `log` and `ipc` act on one running instance:

| Option | Picks |
| --- | --- |
| `-i`, `--id <id>` | The instance with this id, or the one whose id starts with it |
| `--pid <pid>` | The instance with this process id |
| `-p`, `--path`, `-c`, `--config` | The instance running that configuration |
| None | The first instance `winshell list` shows |

## Commands

| Command | What it does |
| --- | --- |
| `winshell list` | Lists the running instances with their id, process id, shell id, configuration, launch time and log file. `-j`, `--json` prints them as JSON. |
| `winshell kill` | Asks an instance to exit, and prints `Killed <id>`. |
| `winshell log` | Prints an instance's log. With no instance running, it prints the log of one that has stopped. `-t`, `--tail <n>` prints the last `n` lines; `-f`, `--follow` keeps printing new lines until you press Ctrl+C. `winshell log <file>` prints that file. |
| `winshell ipc show` | Lists the instance's IPC targets with their functions and properties. |
| `winshell ipc call <target> <function> [args...]` | Calls a function of an [`IpcHandler`](winshell-io.md#ipchandler) and prints what it returns. |
| `winshell ipc prop get <target> <property>` | Prints a property of an `IpcHandler`. |
| `winshell ipc wait <target> <signal>` | Waits for a signal of an `IpcHandler` once and prints its first argument. |
| `winshell ipc listen <target> <signal>` | Prints the first argument of each signal until you press Ctrl+C. |

`winshell msg` is the old name of `winshell ipc call`; it still works and says so. There is no command that reloads a running shell yet ([#134](https://github.com/dante-sparras/winshell/issues/134)); save a file, or call [`Shell.reload()`](winshell.md#shell) from an `IpcHandler`.

The default shell has one IPC target, `settings`: `winshell ipc call settings open <page>` opens [Winshell Settings](../settings.md#opening-it).

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done. For a shell, it was stopped. |
| 1 | No such configuration or instance, an IPC call failed, or the instance couldn't be reached. |
| 2 | The command or its options couldn't be read. |

## Environment variables

| Variable | What it does |
| --- | --- |
| `WINSHELL_CONFIG_DIR` | The folder configurations load from, instead of `%APPDATA%\winshell`. With it set, an old `%USERPROFILE%\.config\winshell` isn't copied. |
| `WINSHELL_APP_ID` | Sets `Shell.appId` when the configuration has no `//@ pragma AppId`. |
| `WINSHELL_DISABLE_FILE_WATCHER` | Any value stops the shell from reloading when its files change. |
| `WINSHELL_NO_RELOAD_POPUP` | `1` is meant to hide a notice about reloads. Winshell has no such notice yet, so it does nothing ([#133](https://github.com/dante-sparras/winshell/issues/133)). |
| `WINLAND_PIPE` | The named pipe [`Winshell.Winland`](winshell-winland.md) connects to, instead of `winland-<session id>`. |

A configuration can set variables for its own process with `//@ pragma Env NAME=value` ([configuration guide](../guide/configuration.md#pragmas)).

## Files

| What | Where |
| --- | --- |
| Configurations | `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` |
| The default shell | `%LOCALAPPDATA%\winshell\default-shell`, written again each time Winshell runs it |
| Per-shell data, state and cache | `%LOCALAPPDATA%\winshell\data\by-shell\<shell id>`, and the same under `state` and `cache`. They are `Shell.dataDir`, `Shell.stateDir` and `Shell.cacheDir`. |
| Instance logs | `%LOCALAPPDATA%\winshell\run\<instance id>\log.txt`. A stopped instance's folder is deleted when a later instance starts, once it is a day old. |
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one text file per crash. Nothing is sent anywhere. See [troubleshooting](../guide/troubleshooting.md#winshell-crashed-or-a-surface-closed). |
| Run at sign-in | The `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written only when a shell sets [`Startup.runAtSignIn`](winshell.md#startup) |

A log line is `HH:mm:ss.fff LEVEL category: message`. The levels are `DEBUG` (with `-v`), `INFO`, `WARN`, `ERROR` and `FATAL`.
