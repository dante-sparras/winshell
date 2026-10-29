# Troubleshooting

Start with the log. Winshell writes what it does, and every error with its file and line, to the terminal it runs in and to the instance's log file:

```powershell
winshell log          # the running instance's log, or the last stopped one's
winshell log -f       # keep printing new lines
winshell list         # running instances and where their logs are
winshell -v           # run with debug messages too
```

Logs are in `%LOCALAPPDATA%\winshell\run\<instance id>\log.txt`. A shell that exits keeps that folder for a day, so `winshell log` can print it after the shell has stopped. A later start then deletes the folder. Copy the log before that day is up if you need to keep it.

## Winshell doesn't start

| It says | What to do |
| --- | --- |
| `No shell.qml at <path>` | `--path` names a folder without a `shell.qml`, or a file that isn't there. |
| `No config named "<name>" in <folder>` | `--config` names a folder of the config folder that has no `shell.qml`. |
| `Several configs in <folder>; pick one with -c: …` | The config folder has no `shell.qml` of its own and several subfolders with one. Run `winshell -c <name>`. To make one the default, move its files up into the config folder, so the folder has a `shell.qml` of its own, or leave only one subfolder with a `shell.qml`. |
| Nothing, and it exits at once | `--no-duplicate` found this configuration already running, or a second start overlapped the first. `winshell list` shows the one that is running. |
| `Unknown command "<name>". Run winshell --help.` | The first word isn't a command. Options start with `-`. |

Nothing printed and nothing on screen? Run `winshell` from a terminal, not with `-d`, so you see the log.

## My configuration has an error

A notice in the bottom right corner of the main screen says **Your configuration has an error**, with the file, the line and the column. **Open file** opens that file. **Dismiss** closes the notice, as does Escape once you have clicked it. The log has an `ERROR` line with the same error:

```text
ERROR scene: Failed to load configuration: C:\Users\you\AppData\Roaming\winshell\shell.qml[12:5]: Cannot assign to non-existent property "colour"
```

Winshell keeps running. Fix the file and save it, and it loads again and the notice goes away. When a reload fails, the shell you had keeps running until a reload works. Errors in a binding or a signal handler don't stop the load: they are logged where they happen, and the rest of the shell goes on.

Check names against the [reference](../reference/index.md). A Quickshell name, such as `import Quickshell` or `PwNode`, has a Winshell name in [Migrating from Quickshell](../migrating-from-quickshell.md).

## Saving a file doesn't reload

- Winshell watches the folders the configuration loaded files from, not their subfolders, and only `.qml`, `.js` and `qmldir` files. A file in a folder nothing has imported yet is seen once something imports it and you save again.
- `settings.watchFiles: false` on the `ShellRoot`, or the `WINSHELL_DISABLE_FILE_WATCHER` environment variable, turns reloading off.
- You are editing `%LOCALAPPDATA%\winshell\default-shell`. Winshell writes that folder again when it runs the default shell and no other instance is running it; [copy it](getting-started.md#make-the-default-shell-your-own) and edit the copy.

## There are two taskbars

Winshell doesn't hide or replace Explorer's taskbar, and never restarts Explorer. Both reserve the bottom of the screen, so they stack. To see only Winshell's, turn on **Automatically hide the taskbar** in Windows Settings > Personalization > Taskbar for Explorer's taskbar. Windows keeps a few hotkeys for its own taskbar while Explorer runs, such as `Win+T`, `Win+S`, `Win+A`, `Win+N`, and `Win+1` through `Win+9`.

## A hotkey doesn't work

Winshell Settings, or the `Hotkey`'s `taken` and `error`, say why:

- **Taken.** Windows or another app already holds that chord, and Winshell doesn't take it away. Pick another one, or close the app that holds it. Another Winshell instance counts as another app.
- **Not a hotkey.** `sequence` uses `Win`, `Ctrl`, `Alt` and `Shift`. `Super` and `Meta` are rejected, and so are left or right `Ctrl`, `Alt` and `Shift`. A comma separates chords, so the comma key is `Comma`.
- **The Windows key opens Windows' Start.** Winshell's Start opens on a tap of the Windows key only while a `Hotkey` with `sequence: "Win"` is enabled. In the default shell it is the Open Start hotkey on Winshell Settings' Hotkeys page.

See [hotkeys](../hotkeys.md).

## Notifications don't appear

The notification center is empty until you allow access: click **Allow notification access**, and allow it when Windows asks. If you said no, allow it in Windows Settings > Privacy & security > Notifications. Some Windows setups don't give notification access to a program that isn't installed as a package; then the notification center says access is unavailable. [Windows notifications](../notifications.md) explains this. Toast banners are always Windows' own.

## Wi-Fi networks or Bluetooth devices are missing

- **Wi-Fi:** scanning needs Windows' Wi-Fi access, which also needs location services on (Windows Settings > Privacy & security > Location). A shell asks for it with `Networking.requestAccess()`. Without access, the adapters and connection still show, and the networks in range don't. See [networking](../network.md).
- **Bluetooth:** nearby devices show only while the shell is discovering, and the radio has to be on. Winshell doesn't turn radios on or off; use Windows Settings. See [Bluetooth](../bluetooth.md).
- **Quick settings tiles** for Wi-Fi, Bluetooth and Airplane mode open their page in Windows Settings, because Winshell doesn't change radios.

## Tray icons are missing

- Only one instance in the Windows session hosts the tray icons. A second instance logs that it is waiting, and takes over within half a second when the first one exits.
- The volume, network and battery icons are Explorer's own, not tray icons. Winshell shows its own from `Winshell.Audio`, `Winshell.Network` and `Winshell.Power`.
- An icon Windows Settings hides is in the overflow behind the chevron. Choose which icons show in Windows Settings > Personalization > Taskbar > Other system tray icons.

See [the notification area](../tray.md).

## Winshell doesn't start when I sign in

Winshell Settings' Startup page shows the switch as off when:

- **Windows' Startup apps page turned it off.** Only that page (Windows Settings > Apps > Startup) can turn it back on.
- **The entry starts another `winshell.exe`**, as after unzipping an update into a new folder, or other options. Turn the switch on to point it at the Winshell you are running.

The entry runs Winshell with the options it was started with. `WINSHELL_CONFIG_DIR` applies at sign-in only when it is set for your user account.

## Winshell crashed, or a surface closed

When Winshell itself fails, as opposed to a mistake in the configuration, it writes a crash report to `%LOCALAPPDATA%\winshell\crashes`, and the log says where. Nothing is sent anywhere; reports leave out what scripts printed and the passwords, tokens and keys Winshell recognizes.

A notice in the bottom right corner says which window stopped working and what Winshell did about it, and **Open report** opens the crash report. It goes away after 25 seconds. A window that fails while handling input closes and opens again after 1, 2, 4, 8 and 16 seconds. If it fails a sixth time within five minutes, it stays closed until the configuration reloads; save a file or run `Shell.reload()` to bring it back. A window that fails while drawing stays up, and the notice says the panel could not draw. If Winshell itself closed, the next start shows that notice once. The rest of the shell keeps running throughout.

A crash is a bug in Winshell. Please report it.

## Report a bug

[Open a bug report](https://github.com/dante-sparras/winshell/issues/new/choose) with:

- What you did, what you expected, and what happened.
- `winshell --version`, and the Windows version from `winver`.
- The log (`winshell log > log.txt`) and the crash report, if there is one. Look through them first: they have file paths and the names of the apps you run.
- The smallest configuration that shows the problem, or say that it's the default shell.

For a security problem, don't open an issue; follow the [security policy](https://github.com/dante-sparras/winshell/blob/main/.github/SECURITY.md).
