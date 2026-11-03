# Getting started

Winshell runs on Windows 11. This page installs it, runs the default shell, and shows where a configuration lives and how it reloads.

## Install

The portable zip and the PowerShell command install the same self-contained build from the latest release. Running that build needs no .NET install. winget and the Microsoft Store are not available yet. Building from source is for working on Winshell. Writing a shell of your own needs the [.NET SDK](https://dotnet.microsoft.com/download).

### Portable zip

A release's [GitHub release page](https://github.com/enpaze/winshell/releases) carries `winshell-<version>-win-x64.zip` and `winshell-<version>-win-arm64.zip`, with `SHA256SUMS.txt` for their hashes. The zip is 72 MB before ReadyToRun, and it needs no .NET install. Unzip one into a folder of its own and run `winshell.exe`.

The portable build updates itself from GitHub Releases, as [Winshell updates](../updates.md) explains. The default shell updates with Winshell. A shell you built yourself does not.

Releases will be signed through SignPath Foundation once its project for Winshell is approved. Until then a published zip is unsigned. See the [Code signing policy](../code-signing-policy.md).

### Install with PowerShell

No administrator account. This downloads the portable build for the current user, checks the zip against `SHA256SUMS.txt`, installs into `%LOCALAPPDATA%\Programs\winshell`, adds the `bin` folder to your user PATH, and creates a Start menu shortcut. It will not replace an existing install while that Winshell is running. A later install removes a leftover `bin.old-*` folder once nothing from that install is running. If a Microsoft Store copy is also installed, the installer says so and still finishes. The Store's WindowsApps alias usually comes first on PATH, so `winshell` may start that copy. From `irm | iex`, run `winshell` in that same window. A window that only launched the script needs a new terminal.

It needs `install.ps1` and the zip on that release.

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

Remove that install with:

```powershell
irm https://github.com/enpaze/winshell/releases/latest/download/uninstall.ps1 | iex
```

That removes the install folder, the PATH entry, the shortcut, the sign-in entry, and a staged update in `%LOCALAPPDATA%\winshell\updates`. It leaves your configuration in `%APPDATA%\winshell`. Logs and state stay in `%LOCALAPPDATA%\winshell`. From `irm | iex`, that same window drops the `bin` folder and keeps every other PATH entry.

### winget, coming

`winget install DanteSparras.Winshell` once winget has accepted the package. It is not available yet. It will install the same portable build. winget updates that copy; Winshell does not.

### Microsoft Store, coming

The Store package is built for Partner Center and is not published. Publishing waits on that setup. The Store would sign that package and update it. It is not an install option today.

### Build from source

You need Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
git clone https://github.com/enpaze/winshell.git
cd winshell
dotnet publish src\Winshell -c Release -o "$env:LOCALAPPDATA\winshell-dev"
```

That puts `winshell.exe` in `%LOCALAPPDATA%\winshell-dev`, not in the PowerShell installer's folder. Add that folder to your `PATH` to run `winshell` from anywhere. The build uses the .NET runtime the SDK installed.

To update a source build, close Winshell, pull the newer source and publish it over the same folder. A build from source never updates itself.

To uninstall a source build, turn off **Run Winshell when I sign in** in Winshell Settings if you turned it on, close Winshell, and delete its folder. Your configuration is in `%APPDATA%\winshell`, and logs, state and crash reports are in `%LOCALAPPDATA%\winshell`; delete them too if you don't want to keep them.

## First run

Open a terminal in Winshell's folder, or anywhere if it is on your `PATH`, and run:

```powershell
winshell
```

With no project of your own, Winshell runs its default shell: a Windows 11 [taskbar](../taskbar.md) on every screen, with Start, quick settings, the notification center and calendar, the on-screen display, and [Winshell Settings](../settings.md). It writes the log to the terminal and to a file. The default shell is built in. It is not a folder of files Winshell writes out.

By default Winshell leaves Explorer's taskbar in place. Both reserve the bottom of the screen, so the two taskbars stack, and maximized windows stop above both. To hide Explorer's taskbar and release its edge while Winshell is running, set `hideExplorerTaskbar = true` in [preferences.toml](../settings.md#where-the-choices-live). That key stays off until the file sets it, and there is no switch for it in Winshell Settings. Winshell puts the window and the edge back when it exits. If you would rather leave Explorer's taskbar visible, turn on **Automatically hide the taskbar** for it in Windows Settings > Personalization > Taskbar. See [Explorer's taskbar is missing](troubleshooting.md#explorers-taskbar-is-missing) if a killed Winshell leaves it hidden.

- **Run it without a terminal:** `winshell -d` starts it in the background and returns. From a terminal, a failure before it starts is printed there. The Start tile and the sign-in entry have no console, so that same failure is a dialog.
- **Stop it:** press Ctrl+C in its terminal, or run `winshell kill`.
- **Start it when you sign in:** right-click an empty part of the taskbar, choose **Taskbar settings**, and turn on **Run Winshell when I sign in** on the Startup page. Signing in runs the last build and does not build again.
- **See what is running:** `winshell list`. `winshell log` prints the log of the running instance.

The [command line](../reference/command-line.md) page lists every command.

## Where a configuration lives

A configuration is a .NET project. Winshell looks for a `.csproj` in this order. With none, it runs the default shell.

| What you run | The project it builds and runs |
| --- | --- |
| `winshell --path <folder or file>` | That `.csproj`, or the one `.csproj` in that folder |
| `winshell --config <name>` | The project in `%APPDATA%\winshell\<name>` |
| `winshell` | The project in `%APPDATA%\winshell`; else `%APPDATA%\winshell\default`; else the one subfolder of `%APPDATA%\winshell` that has a project. With several, Winshell asks you to pick one with `--config`. With none, it runs the default shell. |

`WINSHELL_CONFIG_DIR` replaces `%APPDATA%\winshell` in this table. The default shell keeps `preferences.toml` in that folder.

The first time Winshell uses `%APPDATA%\winshell`, it copies an old `%USERPROFILE%\.config\winshell` there, if you have one, and leaves the old folder in place.

A folder that still has a `shell.qml` and no `.csproj` is not a configuration. Winshell runs the default shell and shows a notice that QML configurations stopped working. There is no converter. Preferences that were already in the config folder stay there, as TOML. To write a shell, start a project, as the [configuration guide](configuration.md) describes.

## Make a shell of your own

```powershell
dotnet new winshell -n MyShell
```

That needs the .NET SDK. `dotnet new winshell-default -n MyShell` starts from the default shell instead of an empty panel. Run it with `winshell --path MyShell`. Winshell builds the project to `bin\shell` when a source is newer than that program, then starts the program. A build that fails does not start the last program that built.

The [configuration guide](configuration.md) is the rest of that, and the [default shell reference](../reference/default-shell.md) is what the built-in shell does.

## Reload

`winshell reload` builds the configuration. When the build fails, the running instance stays and Winshell shows a notice. When it builds, a new instance starts and takes over with `--after`. There is no file watcher. While you are writing the shell, `dotnet watch` is the fast loop.

`shell.Reload()` starts the same program again with `--after` and then quits. It does not build. False means that process did not start, and the shell keeps running.

A build error is a notice in the bottom right corner of the main screen. It does not take the keyboard. `winshell ipc call notice dismiss` closes it. The log has the same error.

Read the log with `winshell log`, or `winshell log -f` to follow it.

## Next steps

- [Configuration guide](configuration.md): a window, a service, IPC, preferences and reload.
- [Troubleshooting](troubleshooting.md): when something doesn't start, show or work.
