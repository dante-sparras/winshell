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
| `Building a configuration needs the .NET SDK…` | A build needs the SDK. `--no-build` of a program that is already built does not. |
| A file `was downloaded from the internet` | The `.csproj`, or a `.props` or `.targets` beside it, has a `Zone.Identifier` stream. Unblock it if you trust it. Nothing is built until then. |
| Nothing, and it exits at once | There is no configuration, so there is nothing to run: make one as the [configuration guide](configuration.md#create-one) shows. Or `--no-duplicate` found this shell id already running, or a second start overlapped the first. `winshell list` shows the one that is running. |
| `This shell is Winshell … and this winshell is …` | The CLI and the shell are different releases. Use the `winshell` that matches, or build the shell with this one. `kill` still works. |
| `Unknown command "<name>". Run winshell --help.` | The first word isn't a command. Options start with `-`. |

Started from Start or at sign-in, and Winshell cannot start? It shows a dialog with the same text, because those starts have no console window. **Open config folder** opens the folder the message is about. A console, including `winshell run -d` from a terminal, prints the text and shows no dialog. Nothing in the dialog and nothing on screen means the shell is already running (`--no-duplicate`) or it started and the failure is in the log.

## The build failed

A notice says the configuration did not build, with the compiler's lines. **Open file** opens the project. **Dismiss** closes the notice, as does Escape once you have clicked it. The notice does not take the keyboard when it appears, so from the keyboard, without clicking it, run `winshell ipc call notice dismiss`.

`winshell reload` leaves the running instance up when the build fails. Fix the project and reload again. [`winshell dev`](configuration.md#develop-a-shell) is the loop while you are editing. Check names against the [reference](reference/index.md).

## There are two taskbars

Winshell does not hide or replace Explorer's taskbar unless your shell does, and it never restarts Explorer. A panel that reserves the bottom of the screen stacks with Explorer's taskbar. To leave Explorer's taskbar where it is and only get it out of the way, turn on **Automatically hide the taskbar** in Windows Settings > Personalization > Taskbar.

To make your shell the only bar, set `shell.ExplorerTaskbar.Hidden = true`. Winshell then hides Explorer's taskbar window and releases the screen edge it reserved. It puts both back when it exits: quitting, Ctrl+C, `winshell kill`, or a crash. A crash that skips Winshell's own exit is caught by `winshell run`, which [puts the taskbar back](reference/command-line.md#crashes) and starts the shell again. Windows keeps a few hotkeys for its own taskbar while Explorer runs, such as `Win+T`, `Win+S`, `Win+A`, `Win+N`, and `Win+1` through `Win+9`.

## Explorer's taskbar is missing

A shell that set `ExplorerTaskbar.Hidden` hid it. While that shell is still running, quit it with `winshell kill`. It puts Explorer's taskbar window back and reserves its edge again.

If `winshell run` was ended too, or the shell ran without it, as a configuration's program started directly does, the window stays hidden and the edge stays released. Bring the taskbar back yourself:

1. Press Ctrl+Shift+Esc to open Task Manager.
2. Find **Windows Explorer**, right-click it and choose **Restart**.

Explorer builds its taskbar again. Signing out and back in also starts Explorer again, which brings the taskbar back.

Ending `winshell run` and the shell together does that, such as `taskkill /F /T` on `winshell run`. Ending only the shell, a native crash such as a stack overflow, and `winshell kill --force` leave `winshell run` to put the taskbar back.

## A change to a TOML file does nothing

A [`TomlFile`](reference/winshell.md#tomlfile) keeps the values it has when it can't use what the file says, and writes why to the log, which `winshell log` shows. Only under `winshell dev` does a file that doesn't parse also show in a notice.

- **`TomlFile: failed to parse <file>[line:column]: …`** The file is not valid TOML, so none of it is read until you fix that line and save.
- **`<file>[line:column]: <key> should be <what it takes>, not <value>, so it was left unchanged`**, or another reason in place of "should be …". The file is read, but that one value isn't used. A switch is `true` or `false`, and a word is in quotes.
- **`<file>[line:column]: <key> was ignored; did you mean <key>?`** Keys are case-sensitive and start with a lowercase letter. A key that differs from one the class reads only in case, by a letter, or by two in a longer key is named this way, and stays in the file.
- **Nothing in the log.** A key unlike any the class reads stays in the file and is ignored without a word, since another reader may use it.

## A hotkey doesn't work

The `Hotkey`'s `Taken` and `Error` say why:

- **Taken.** Windows or another app already holds that chord, and Winshell doesn't take it away. Pick another one, or close the app that holds it. Another Winshell instance counts as another app.
- **Not a hotkey.** `Sequence` uses `Win`, `Ctrl`, `Alt` and `Shift`. `Super` and `Meta` are rejected, and so are left or right `Ctrl`, `Alt` and `Shift`. A comma separates chords, so the comma key is `Comma`.
- **The Windows key opens Windows' Start.** A tap of the Windows key reaches your shell only while a `Hotkey` with `Sequence` of `Win` is enabled.

See [hotkeys](reference/winshell-input.md).

## Notifications don't appear

A shell sees no notifications until you allow access: it asks with `shell.Notifications.RequestAccess()` from a button, and you allow it when Windows asks. If you said no, allow it in Windows Settings > Privacy & security > Notifications. Some Windows setups don't give notification access to a program that isn't installed as a package; then `AccessStatus` says access is unavailable. Toast banners are always Windows' own. See [notifications](reference/winshell-notifications.md).

## Wi-Fi networks or Bluetooth devices are missing

- **Wi-Fi:** scanning needs Windows' Wi-Fi access, which also needs location services on (Windows Settings > Privacy & security > Location). A shell asks for it with `Networking.RequestAccess()`. Without access, the adapters and connection still show, and the networks in range don't. See [networking](reference/winshell-network.md).
- **Bluetooth:** nearby devices show only while the shell is discovering, and the radio has to be on. A configuration can set `BluetoothAdapter.Enabled`. See [Bluetooth](reference/winshell-bluetooth.md).

## Media controls show nothing

If something is playing and the media controls stay empty, look for `media: Windows' media session service (Now Playing Session Manager) is not answering` in the log. Windows' own media session service has stopped answering, and no app can list media sessions until it does. Sign out and back in, or restart the per-user `NPSMSvc_*` service. Winshell keeps asking, less often the longer the service stays silent (at most an hour apart), and lists the sessions once it answers.

## Tray icons are missing

- Only one instance in the Windows session hosts the tray icons. A second instance logs that it is waiting, and takes over within half a second when the first one exits.
- Some apps add their icon only when they start, and never again when a new taskbar asks. NVIDIA Settings is one. Its icon stays on Explorer's taskbar and shows in Winshell once the app starts again while Winshell runs.
- The volume, network and battery icons are Explorer's own, not tray icons. Winshell shows its own from `shell.Audio`, `shell.Networking` and `shell.Power`.
- An icon Windows Settings hides is in the overflow behind the chevron. Choose which icons show in Windows Settings > Personalization > Taskbar > Other system tray icons.

See [the notification area](reference/winshell-tray.md).

## Winshell doesn't start when I sign in

The sign-in entry starts your configuration. With none, it starts nothing. `install.ps1 -AutoStart` adds the entry, and the Sign-in check of `winshell doctor` says what is wrong with it:

- **Windows' Startup apps page turned it off.** Only that page (Windows Settings > Apps > Startup) can turn it back on. A Windows policy that keeps a packaged Winshell off is the same.
- **The unpackaged entry starts another `winshell.exe`**, as after unzipping an update into a new folder, or other options. Install Winshell again with `-AutoStart` to point the entry at the one you use. A packaged Winshell has no path to rewrite: Windows starts the package.

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
- The smallest configuration that shows the problem.

For a security problem, don't open an issue; follow the [security policy](https://github.com/enpaze/winshell/blob/main/.github/SECURITY.md).
