# Troubleshooting

Start with the log. Winshell writes what it does, and every error, to the terminal it runs in and to the instance's log file:

```powershell
winshell log          # the running instance's log, or the last stopped one's
winshell log -f       # keep printing new lines
winshell list         # running instances and where their logs are
winshell run -v       # run with debug messages too
```

Logs are in `%LOCALAPPDATA%\winshell\run\<instance id>\log.txt`. A shell that exits keeps that folder for a day, so `winshell log` can print it after the shell has stopped. A later start then deletes the folder. Copy the log before that day is up if you need to keep it.

## Winshell doesn't start

| It says | What to do |
| --- | --- |
| `No .csproj at <path>.` | `--path` names a folder without a `.csproj`, or a file that isn't there. |
| `No config named "<name>" in <folder>.` | `--config` names a folder of the config folder that has no project. |
| `Several configs in <folder>; pick one with -c: …` | The config folder has no project of its own and several subfolders with one. Run `winshell run -c <name>`. |
| `Several projects in <folder>; name one with -p: …` | The folder has more than one `.csproj`. Pass the file with `-p`. |
| `<name> did not build.` | The build failed. The last good build is not started. The notice and the log have the compiler's lines. |
| `<name> did not build.` and `NU1101: Unable to find package Winshell` | The `Winshell` package is not published yet. Pack it and add a package source for it, as [Create one](configuration.md#create-one) shows. |
| `<name> has not been built. Run winshell run once to build it.` | `--no-build` found no program in `bin\shell`. |
| `Building a configuration needs the .NET SDK…` | A build needs the SDK. Running the default shell, or `--no-build` of a program that is already built, does not. |
| A file `was downloaded from the internet` | The `.csproj`, or a `.props` or `.targets` beside it, has a `Zone.Identifier` stream. Unblock it if you trust it. Nothing is built until then. |
| Nothing, and it exits at once | `--no-duplicate` found this shell id already running, or a second start overlapped the first. `winshell list` shows the one that is running. |
| `This shell is Winshell … and this winshell is …` | The CLI and the shell are different releases. Use the `winshell` that matches, or build the shell with this one. `kill` still works. |
| `Unknown command "<name>". Run winshell --help.` | The first word isn't a command. Options start with `-`. |

Started from Start or at sign-in, and Winshell cannot start? It shows a dialog with the same text, because those starts have no console window. **Open config folder** opens the folder the message is about. A console, including `winshell run -d` from a terminal, prints the text and shows no dialog. Nothing in the dialog and nothing on screen means the shell is already running (`--no-duplicate`) or it started and the failure is in the log.

## The build failed

A notice says the configuration did not build, with the compiler's lines. **Open file** opens the project. **Dismiss** closes the notice, as does Escape once you have clicked it. The notice does not take the keyboard when it appears, so from the keyboard, without clicking it, run `winshell ipc call notice dismiss`.

`winshell reload` leaves the running instance up when the build fails. Fix the project and reload again. [`winshell dev`](configuration.md#develop-a-shell) is the loop while you are editing. Check names against the [reference](reference/index.md).

## There are two taskbars

By default Winshell does not hide or replace Explorer's taskbar, and it never restarts Explorer. Both reserve the bottom of the screen, so they stack. To leave Explorer's taskbar where it is and only get it out of the way, turn on **Automatically hide the taskbar** in Windows Settings > Personalization > Taskbar. `Alt+Tab`, `Win+Tab`, `Win+D` and `Win+X` stay with Windows unless `desktopChords` is true in `preferences.toml`. While Explorer holds one it can still report `Taken`.

To make Winshell the only bar, set this in `preferences.toml` and save:

```toml
hideExplorerTaskbar = true
```

There is no switch for it in Winshell Settings. Winshell then hides Explorer's taskbar window and releases the screen edge it reserved. It puts both back when it exits: quitting, Ctrl+C, `winshell kill`, or a crash that still runs Winshell's exit. Windows keeps a few hotkeys for its own taskbar while Explorer runs, such as `Win+T`, `Win+S`, `Win+A`, `Win+N`, and `Win+1` through `Win+9`.

## Explorer's taskbar is missing

This is `hideExplorerTaskbar`. While Winshell is still running, set the key back to `false` and save, or quit Winshell. It puts Explorer's taskbar window back and reserves its edge again.

If Winshell was killed before that code could run, the window stays hidden and the edge stays released. Winshell cannot run anything in that case. Bring the taskbar back yourself:

1. Press Ctrl+Shift+Esc to open Task Manager.
2. Find **Windows Explorer**, right-click it and choose **Restart**.

Explorer builds its taskbar again. Set `hideExplorerTaskbar` to `false` before you start Winshell if you want that taskbar to stay. Signing out and back in also starts Explorer again, which brings the taskbar back.

A kill that skips the exit code is Task Manager's **End task**, `taskkill /F`, or `winshell kill --force` when the shell did not exit on its own. The same is true of a native crash that never reaches Winshell's handler, such as a stack overflow. A normal exit, `winshell kill` without `--force`, Ctrl+C, and a managed crash that still runs the exit or the unhandled-exception handler all put the taskbar back.

## A change to preferences.toml does nothing

Winshell keeps the values it has when it can't use what the file says, and writes why to the log, which `winshell log` shows. Only under `winshell dev` does a file that doesn't parse also show in a notice. The same goes for `launcher.toml` and `quick-settings.toml`.

- **`TomlFile: failed to parse <file>[line:column]: …`** The file is not valid TOML, so none of it is read until you fix that line and save. Winshell Settings also says its changes won't be saved meanwhile, and `winshell doctor` warns with the same line and column.
- **`<file>[line:column]: <key> should be <what it takes>, not <value>, so it was left unchanged`**, or another reason in place of "should be …", such as `is not "acrylic" or "mica"`. The file is read, but that one value isn't used. A switch is `true` or `false`, and a word such as `"mica"` is in quotes. The [preferences table](settings.md#where-the-choices-live) lists every key and its values.
- **`<file>[line:column]: <key> was ignored; did you mean <key>?`** Keys are case-sensitive and start with a lowercase letter, such as `taskbarCentered`. A key that differs from one Winshell reads only in case, by a letter, or by two in a longer key is named this way, and stays in the file.
- **Nothing in the log.** A key unlike any Winshell reads stays in the file and is ignored without a word, since your own configuration may read it. A `colorScheme`, `accentColor` or `taskbarAppsOn` that isn't one of its values leaves the default in use, also without a word.

## A hotkey doesn't work

Winshell Settings, or the `Hotkey`'s `Taken` and `Error`, say why:

- **Taken.** Windows or another app already holds that chord, and Winshell doesn't take it away. Pick another one, or close the app that holds it. Another Winshell instance counts as another app.
- **Not a hotkey.** `Sequence` uses `Win`, `Ctrl`, `Alt` and `Shift`. `Super` and `Meta` are rejected, and so are left or right `Ctrl`, `Alt` and `Shift`. A comma separates chords, so the comma key is `Comma`.
- **The Windows key opens Windows' Start.** Winshell's Start opens on a tap of the Windows key only while a `Hotkey` with `Sequence` of `Win` is enabled. In the default shell it is the Open Start hotkey on Winshell Settings' Hotkeys page.

See [hotkeys](hotkeys.md).

## Notifications don't appear

The notification center is empty until you allow access: click **Allow notification access**, and allow it when Windows asks. If you said no, allow it in Windows Settings > Privacy & security > Notifications. Some Windows setups don't give notification access to a program that isn't installed as a package; then the notification center says access is unavailable. [Windows notifications](notifications.md) explains this. Toast banners are always Windows' own.

## Wi-Fi networks or Bluetooth devices are missing

- **Wi-Fi:** scanning needs Windows' Wi-Fi access, which also needs location services on (Windows Settings > Privacy & security > Location). A shell asks for it with `Networking.RequestAccess()`. Without access, the adapters and connection still show, and the networks in range don't. See [networking](network.md).
- **Bluetooth:** nearby devices show only while the shell is discovering, and the radio has to be on. A configuration can set `BluetoothAdapter.Enabled`. The quick settings tile does not. See [Bluetooth](bluetooth.md).
- **Quick settings tiles** for Wi-Fi, Bluetooth and Airplane mode open their page in Windows Settings. A configuration can set `Networking.WifiEnabled` and `BluetoothAdapter.Enabled`. These tiles do not.

## Media controls show nothing

If something is playing and the media controls stay empty, look for `media: Windows' media session service (Now Playing Session Manager) is not answering` in the log. Windows' own media session service has stopped answering, and no app can list media sessions until it does. Sign out and back in, or restart the per-user `NPSMSvc_*` service. Winshell keeps asking, less often the longer the service stays silent (at most an hour apart), and lists the sessions once it answers.

## Tray icons are missing

- Only one instance in the Windows session hosts the tray icons. A second instance logs that it is waiting, and takes over within half a second when the first one exits.
- Some apps add their icon only when they start, and never again when a new taskbar asks. NVIDIA Settings is one. Its icon stays on Explorer's taskbar and shows in Winshell once the app starts again while Winshell runs.
- The volume, network and battery icons are Explorer's own, not tray icons. Winshell shows its own from `shell.Audio`, `shell.Networking` and `shell.Power`.
- An icon Windows Settings hides is in the overflow behind the chevron. Choose which icons show in Windows Settings > Personalization > Taskbar > Other system tray icons.

See [the notification area](tray.md).

## Winshell doesn't start when I sign in

Winshell Settings' Startup page shows the switch as off when:

- **Windows' Startup apps page turned it off.** Only that page (Windows Settings > Apps > Startup) can turn it back on. A Windows policy that keeps a packaged Winshell off is the same: the switch stays off.
- **The unpackaged entry starts another `winshell.exe`**, as after unzipping an update into a new folder, or other options. Turn the switch on to point it at the Winshell you are running. A packaged Winshell has no path to rewrite: Windows starts the package.

The sign-in entry runs the last build. It does not build, so a broken edit and a missing SDK do not block signing in. `WINSHELL_CONFIG_DIR` applies at sign-in only when it is set for your user account.

## Winshell crashed

When Winshell itself fails, as opposed to a project that did not build, it writes a crash report to `%LOCALAPPDATA%\winshell\crashes`, and the log says where. Nothing is sent anywhere. Reports leave out the passwords, tokens and keys Winshell recognizes.

A notice in the bottom right corner says what failed, and **Open report** opens the crash report. A crash notice goes away after 25 seconds. A build error stays until you dismiss it or the next build works. The rest of the shell keeps running when it can.

A crash is a bug in Winshell. Please report it.

## Report a bug

[Open a bug report](https://github.com/enpaze/winshell/issues/new/choose) with:

- What you did, what you expected, and what happened.
- `winshell --version`, and the Windows version from `winver`.
- The log (`winshell log > log.txt`) and the crash report, if there is one. Look through them first: they have file paths and the names of the apps you run.
- The smallest configuration that shows the problem, or say that it's the default shell.

For a security problem, don't open an issue; follow the [security policy](https://github.com/enpaze/winshell/blob/main/.github/SECURITY.md).
