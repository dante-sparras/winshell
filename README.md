# Winshell

A desktop shell toolkit for Windows 11. A configuration is a .NET project that builds into one program: its windows, and what they show. The windows are WinUI 3. Winshell reads Windows itself for apps, windows, audio, media, networks, Bluetooth, power, notifications and the notification area.

Winshell ships with a default shell that looks like Windows 11: a taskbar on every screen, Start, quick settings, a notification center with a calendar, an on-screen display for volume and brightness, and Winshell Settings. It runs next to Explorer. Running the default shell needs no .NET SDK. Writing a shell does.

Until 1.0 is tagged, a release can still break a configuration. The [changelog](CHANGELOG.md) lists every change.

## Install

Winshell runs on Windows 11. The portable zip and the PowerShell command install the latest release. winget and the Microsoft Store are not available yet. Building from source is for working on Winshell, and for now for writing a shell, since the templates are not published yet. Only someone writing a shell needs the .NET SDK.

### Portable zip

A release's [GitHub release page](https://github.com/enpaze/winshell/releases) carries a self-contained build, which needs no .NET install: `winshell-<version>-win-x64.zip` and `winshell-<version>-win-arm64.zip`, with `SHA256SUMS.txt` for their hashes. The zip is about 48 MB. Unzip one into a folder of its own and run `winshellw.exe`, which starts Winshell with no console window, or `.\winshell run` from a terminal in that folder. That build updates itself from GitHub Releases, and the default shell updates with it. A build from source does not, and neither does a shell you built yourself.

Releases will be signed through SignPath Foundation once its project for Winshell is approved. Until then a published zip is unsigned.

### Install with PowerShell

No administrator account. This downloads the portable build for the current user. It needs `install.ps1` and the zip on that release.

```powershell
irm https://github.com/enpaze/winshell/releases/latest/download/install.ps1 | iex
```

`iex` cannot take parameters. Set these first, or run the downloaded script as a script block:

```powershell
$env:WINSHELL_VERSION = 'x.y.z'  # optional. Omit it for the latest release.
$env:WINSHELL_AUTOSTART = '1'    # optional. Run Winshell at sign-in.
irm https://github.com/enpaze/winshell/releases/latest/download/install.ps1 | iex
```

```powershell
& ([scriptblock]::Create((irm https://github.com/enpaze/winshell/releases/latest/download/install.ps1))) -Version x.y.z -AutoStart
```

The installer checks the zip against `SHA256SUMS.txt` on the release and stops if the hash does not match. It installs into `%LOCALAPPDATA%\Programs\winshell`, adds the `bin` folder to your user PATH, and creates a Start menu shortcut. It will not replace an existing install while that Winshell is running. A later install removes a leftover `bin.old` folder once nothing from that install is running. If a Microsoft Store copy is also installed, the installer says so and still finishes. The Store's WindowsApps alias usually comes first on PATH, so `winshell` may start that copy. The files are the portable build, so Winshell updates itself the same way. From `irm | iex`, run `winshell` in that same window. A window that only launched the script, such as `pwsh scripts/install.ps1`, needs a new terminal.

Remove that install with:

```powershell
irm https://github.com/enpaze/winshell/releases/latest/download/uninstall.ps1 | iex
```

That removes the install folder, the PATH entry, the shortcut, the sign-in entry, and a staged update in `%LOCALAPPDATA%\winshell\updates`. It leaves your configuration in `%APPDATA%\winshell`. Logs and state stay in `%LOCALAPPDATA%\winshell`. From `irm | iex`, that same window drops the `bin` folder and keeps every other PATH entry.

### winget, coming

`winget install DanteSparras.Winshell` once winget has accepted the package. It is not available yet. It will install the same portable build. winget updates that copy; Winshell does not.

### Microsoft Store, coming

The Store package is built for Partner Center and is not published. Publishing waits on that setup, so there is nothing to install from the Store. The Store would sign that package and update it.

### Build from source

You need Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
git clone https://github.com/enpaze/winshell.git
cd winshell
dotnet publish src\Winshell -c Release -o "$env:LOCALAPPDATA\winshell-dev"
```

That puts `winshell.exe` in `%LOCALAPPDATA%\winshell-dev`, away from the folder the PowerShell installer uses. Add that folder to your `PATH` to run `winshell` from anywhere. Like a release, the build carries its own .NET runtime. Unlike one, it does not update itself.

To update a source build, close Winshell, pull the newer source and publish it over the same folder. To uninstall one, turn off **Run Winshell when I sign in** in Winshell Settings if you turned it on, close Winshell, and delete its folder. Your configuration is in `%APPDATA%\winshell`, and logs, state and crash reports are in `%LOCALAPPDATA%\winshell`; delete them too if you don't want to keep them.

## First run

Open a terminal anywhere, if Winshell is on your `PATH`, and run:

```powershell
winshell run
```

In Winshell's own folder, PowerShell needs the path: `.\winshell run`. `winshell` with no command prints the list of commands and starts nothing.

With no project of your own, Winshell runs its default shell: a Windows 11 taskbar on every screen, with Start, quick settings, the notification center and calendar, the on-screen display, and Winshell Settings. It writes the log to the terminal and to a file. The default shell is built in. It is not a folder of files Winshell writes out.

By default Winshell leaves Explorer's taskbar in place. Both reserve the bottom of the screen, so the two taskbars stack, and maximized windows stop above both. To hide Explorer's taskbar and release its edge while Winshell is running, set `hideExplorerTaskbar = true` in `preferences.toml`. That key stays off until the file sets it, and there is no switch for it in Winshell Settings. Winshell puts the window and the edge back when it exits. If you would rather leave Explorer's taskbar visible, turn on **Automatically hide the taskbar** for it in Windows Settings > Personalization > Taskbar. See [Explorer's taskbar is missing](docs/troubleshooting.md#explorers-taskbar-is-missing) if a killed Winshell leaves it hidden.

- **Run it without a terminal:** `winshell run -d` starts it in the background and returns. From a terminal, a failure before it starts is printed there. The Start tile and the sign-in entry have no console, so that same failure is a dialog.
- **Stop it:** press Ctrl+C in its terminal, or run `winshell kill`.
- **Start it when you sign in:** right-click an empty part of the taskbar, choose **Taskbar settings**, and turn on **Run Winshell when I sign in** on the Startup page. Signing in runs the last build and does not build again.
- **See what is running:** `winshell list`. `winshell log` prints the log of the running instance.

The [command line](docs/reference/command-line.md) page lists every command.

## Where a configuration lives

A configuration is a .NET project. Winshell looks for a `.csproj` in this order. With none, it runs the default shell.

| What you run | The project it builds and runs |
| --- | --- |
| `winshell run --path <folder or file>` | That `.csproj`, or the one `.csproj` in that folder |
| `winshell run --config <name>` | The project in `%APPDATA%\winshell\<name>` |
| `winshell run` | The project in `%APPDATA%\winshell`; else `%APPDATA%\winshell\default`; else the one subfolder of `%APPDATA%\winshell` that has a project. With several, Winshell asks you to pick one with `--config`. With none, it runs the default shell. |

`WINSHELL_CONFIG_DIR` replaces `%APPDATA%\winshell` in this table. The default shell keeps `preferences.toml` in that folder.

## Make a shell of your own

A shell of your own is a project from `dotnet new winshell -n MyShell`, or from `dotnet new winshell-default -n MyShell` to start from the default shell instead of an empty panel. That needs the .NET SDK and, for now, a clone of Winshell: the templates and the `Winshell` package they reference are not published yet. [Create one](docs/configuration.md#create-one) has the steps. Run it with `winshell run --path MyShell`. Winshell builds the project to `bin\shell` when a source is newer than that program, then starts the program. A build that fails does not start the last program that built.

The [configuration guide](docs/configuration.md) is the rest of that, and the [default shell reference](docs/reference/default-shell.md) is what the built-in shell does.

## Reload

`winshell reload` builds the configuration. When the build fails, the running instance stays and Winshell shows a notice. When it builds, a new instance starts and takes over with `--after`. There is no file watcher. While you are writing the shell, [`winshell dev`](docs/configuration.md#develop-a-shell) is the fast loop.

`shell.Reload()` starts the same program again with `--after` and then quits. It does not build. False means that process did not start, and the shell keeps running.

A build error is a notice in the bottom right corner of the main screen. It does not take the keyboard. `winshell ipc call notice dismiss` closes it. The log has the same error.

Read the log with `winshell log`, or `winshell log -f` to follow it.

## Documentation

See the [configuration guide](docs/configuration.md), [troubleshooting](docs/troubleshooting.md), and [API reference](docs/reference/index.md). To work on Winshell itself, see the [development runbook](docs/development.md).

Something not working? Run `winshell doctor`. It checks this PC's setup and says how to fix what's wrong. It does not build.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR.

## Privacy

Winshell collects nothing.

## License

[MIT](LICENSE)
