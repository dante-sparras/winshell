# Getting started

Winshell runs on Windows 11. This page installs it, runs the default shell, and shows where your configuration lives and how it reloads.

## Install

Winshell runs on Windows 11. The portable zip and the PowerShell command install the same self-contained build from the latest release. winget and the Microsoft Store are not available yet. Building from source is for working on Winshell.

### Portable zip

A release's [GitHub release page](https://github.com/dante-sparras/winshell/releases) carries `winshell-<version>-win-x64.zip` and `winshell-<version>-win-arm64.zip`, with `SHA256SUMS.txt` for their hashes. The zip needs no .NET install. Unzip one into a folder of its own and run `winshell.exe`.

The portable build updates itself from GitHub Releases, as [Winshell updates](../updates.md) explains. The default shell updates with Winshell. A copy you made your own doesn't, as [making it your own](#make-the-default-shell-your-own) explains.

Releases will be signed through SignPath Foundation once its project for Winshell is approved. Until then a published zip is unsigned. See the [Code signing policy](../code-signing-policy.md).

### Install with PowerShell

No administrator account. This downloads the portable build for the current user, checks the zip against `SHA256SUMS.txt`, installs into `%LOCALAPPDATA%\Programs\winshell`, adds the `bin` folder to your user PATH, and creates a Start menu shortcut. It will not replace an existing install while that Winshell is running. A later install removes a leftover `bin.old-*` folder once nothing from that install is running. From `irm | iex`, run `winshell` in that same window. A window that only launched the script needs a new terminal.

It needs `install.ps1` and the zip on that release.

```powershell
irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
```

`iex` cannot take parameters. Set these first, or run the downloaded script as a script block:

```powershell
$env:WINSHELL_VERSION = 'x.y.z'  # optional. Omit it for the latest release.
$env:WINSHELL_AUTOSTART = '1'    # optional. Run Winshell at sign-in.
irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
```

```powershell
& ([scriptblock]::Create((irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1))) -Version x.y.z -AutoStart
```

Remove that install with:

```powershell
irm https://github.com/dante-sparras/winshell/releases/latest/download/uninstall.ps1 | iex
```

That removes the install folder, the PATH entry, the shortcut, and the sign-in entry. It leaves your configuration in `%APPDATA%\winshell`. From `irm | iex`, that same window drops the `bin` folder and keeps every other PATH entry.

### winget, coming

`winget install DanteSparras.Winshell` once winget has accepted the package. It is not available yet. It will install the same portable build. winget updates that copy; Winshell does not.

### Microsoft Store, coming

The Store package is built for Partner Center and is not published. Publishing waits on that setup. The Store would sign that package and update it. It is not an install option today.

### Build from source

You need Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
git clone https://github.com/dante-sparras/winshell.git
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

With no configuration of your own, Winshell runs its default shell: a Windows 11 [taskbar](../taskbar.md) on every screen, with Start, quick settings, the notification center and calendar, the on-screen display, and [Winshell Settings](../settings.md). It writes the log to the terminal and to a file.

Winshell doesn't hide or replace Explorer's taskbar. Both reserve the bottom of the screen, so the two taskbars stack, and maximized windows stop above both. If you want only Winshell's, turn on **Automatically hide the taskbar** for Explorer's in Windows Settings > Personalization > Taskbar.

- **Run it without a terminal:** `winshell -d` starts it in the background and returns. From a terminal, a failure before it starts is printed there. The Start tile and the sign-in entry have no console, so that same failure is a dialog.
- **Stop it:** press Ctrl+C in its terminal, or run `winshell kill`.
- **Start it when you sign in:** right-click an empty part of the taskbar, choose **Taskbar settings**, and turn on **Run Winshell when I sign in** on the Startup page.
- **See what is running:** `winshell list`. `winshell log` prints the log of the running instance.

The [command line](../reference/command-line.md) page lists every command.

## Where your configuration lives

A configuration is a folder with a `shell.qml` in it. Winshell looks for one in this order:

| What you run | The configuration it loads |
| --- | --- |
| `winshell --path <folder or file>` | That `shell.qml`, or that folder's |
| `winshell --config <name>` | `%APPDATA%\winshell\<name>\shell.qml` |
| `winshell` | `%APPDATA%\winshell\shell.qml`; else `%APPDATA%\winshell\default\shell.qml`; else the one subfolder of `%APPDATA%\winshell` with a `shell.qml`. With several, Winshell asks you to pick one with `--config`. With none, it runs the default shell. |

`WINSHELL_CONFIG_DIR` replaces `%APPDATA%\winshell` in this table. Winshell Settings keeps its choices in `preferences.toml` next to the `shell.qml` it runs from, or in that folder itself for the default shell.

The first time Winshell uses `%APPDATA%\winshell`, it copies an old `%USERPROFILE%\.config\winshell` there, if you have one, and leaves the old folder in place.

## Make the default shell your own

Winshell writes the default shell to `%LOCALAPPDATA%\winshell\default-shell` when it runs it and no other instance is running it, so changes made there are overwritten. A `.qml` or `.js` file the current build does not ship is removed. Other files, such as `launcher.toml`, stay. To change the shell, copy it into your configuration folder and edit the copy:

```powershell
New-Item -ItemType Directory -Force "$env:APPDATA\winshell" | Out-Null
Copy-Item "$env:LOCALAPPDATA\winshell\default-shell\*" "$env:APPDATA\winshell" -Recurse
```

Run `winshell` again and it loads your copy. It keeps the default shell's `//@ pragma ShellId winshell-default`, so your taskbar pins and quick settings tiles carry over. A newer Winshell doesn't change your copy. After an update, compare it with the new `default-shell` folder to pick up what changed.

The [configuration guide](configuration.md) explains what is in those files, and the [default shell reference](../reference/default-shell.md) lists the settings and hooks of each surface.

## Reload

Winshell watches the folders your configuration was loaded from. When you save a `.qml` or `.js` file or a `qmldir` there, it reloads the configuration a moment later, without restarting. The new load takes over the old one's windows where it can, and [`PersistentProperties`](../reference/winshell.md#persistentproperties) keep their values.

If the new version has an error, Winshell keeps running the old one. A notice in the bottom right corner of the main screen, drawn like a Windows 11 notification, says **Your configuration has an error**, with the file, the line and the column, and **Open file** opens that file. The log has the same error:

```text
ERROR scene: Failed to reload configuration: C:\Users\you\AppData\Roaming\winshell\shell.qml[12:5]: Cannot assign to non-existent property "colour"
```

Fix the file and save it again: Winshell reloads, and the notice goes away. If the configuration fails when Winshell starts, there is nothing to keep running: the notice says so, and Winshell stays open with no windows until you save a fix.

Read the log with `winshell log`, or `winshell log -f` to follow it as you edit. To leave the notice out, set `WINSHELL_NO_RELOAD_POPUP=1`, or call `Shell.inhibitReloadPopup()` in your configuration's `Shell.onReloadFailed`.

To stop reloading on save, set `settings.watchFiles: false` on your `ShellRoot`, or set the `WINSHELL_DISABLE_FILE_WATCHER` environment variable. A configuration can still reload itself with `Shell.reload()`.

## Next steps

- [Configuration guide](configuration.md): how a configuration is put together, with examples.
- [Troubleshooting](troubleshooting.md): when something doesn't start, show or work.
- [Migrating from Quickshell](../migrating-from-quickshell.md): Quickshell's names and their Winshell names.
