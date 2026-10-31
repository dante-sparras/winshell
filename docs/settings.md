# Winshell Settings

The default shell has its own settings window, `SettingsWindow.qml`, laid out like Windows 11 Settings: the pages in a pane on the left under "Find a setting", and the page on the right under a breadcrumb, as cards. It changes Winshell only. Where a Windows setting is involved, it reads it at most and links to its page in Windows Settings.

## Opening it

- Right-click an empty part of the taskbar and choose **Taskbar settings**, as on Windows 11.
- `winshell ipc call settings open <page>`, where `<page>` is a page (`personalization`, `taskbar`, `start`, `quickSettings`, `notifications`, `hotkeys`, `startup`, `about`) or a setting from `settings.js`, such as `taskbarAutoHide`. An empty page opens it where it was, with the keyboard in the search box.
- The **Open Winshell Settings** hotkey, once one is set on the Hotkeys page.

Closing the window hides it. The next open brings it back on the page it was on.

## Pages

| Page | What it changes |
| --- | --- |
| Personalization | Light or dark, the accent color (same as Windows, or one of the Windows accent colors), and the material behind the taskbar and flyouts (Acrylic or Mica). A contrast theme still wins. |
| Taskbar | Alignment, auto-hide, a taskbar on every display or the main one only, and where apps show with several displays. With one taskbar, every app shows on it. See [the taskbar](taskbar.md). |
| Start | Recommended apps on or off. **Pinned apps** opens a page under it to move or unpin pins. |
| Quick settings | The tiles in order: move, remove and add back. |
| Notifications | Do not disturb as Windows reports it, and per app whether Winshell's notification center shows its notifications. **Clear all** in the notification center leaves an app that is turned off alone. Windows still decides banners and sounds. |
| Hotkeys | Open Start (`Win` until changed) and Open Winshell Settings (none until set). Empty turns one off. A hotkey starts with Win, Ctrl or Alt, or is `Win` alone, so it never takes a key other apps type with; the two hotkeys cannot be the same. A chord Windows or another app holds says so and is not taken. |
| Startup | Run Winshell when you sign in, for your account only, off until you turn it on. If Windows' Startup apps page turned it off, the switch shows off and only that page turns it back on. An entry that starts a `winshell.exe` in another folder, or with other options, also shows off; turning it on points it at this one. |
| About | The version; Winshell's updates, with **Check for updates** or **Restart now** and **Get Winshell updates automatically** (see [Winshell updates](updates.md)); the config folder and this instance's log, each with a button to open it; and **Run diagnostics**, which runs the checks of [winshell doctor](doctor.md) and lists them under the card, a fix under each problem. Opening a config folder that does not exist yet makes it, with a `preferences.toml` of defaults, each under a comment saying what it does. |

Search matches every word typed against each setting's title, description and a few keywords. Titles that start with what was typed come first. Enter or a click opens the setting's page with the keyboard on it.

## Where the choices live

Everything the window chooses applies at once, without a reload, and is kept in `preferences.toml`. A config in the config folder (`%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR`) keeps it next to its `shell.qml`, so two configs running at once each have their own choices and hotkeys. The default shell keeps it in the config folder itself. A change made to that file by hand applies as it is saved, even when its folder did not exist when Winshell started. When Winshell changes a value it rewrites only that value: comments, blank lines and keys it does not know stay, and a key it adds goes at the end under a comment saying what it does. A file that is not valid TOML is not read, so the values in use stay. Winshell shows the line and column in a notice, with a button that opens the file, and Settings says that changes will not be saved until the file parses. The same text is written to the instance log. The next choice reads the file again: if that read parses, the choice is written into it and every key that was not changed stays as that read has it; if it does not, or the read fails, nothing is written and `saveFailed` is raised. A key the file does not have keeps its default when Winshell starts; removing a key by hand while it runs keeps the value in use. A mode, accent, material or taskbar apps value Winshell cannot read falls back to its default, which for mode and accent is following Windows. A hidden app without an `id` is skipped. A file larger than 1 MiB is left unread the same way, and so is TOML nested more than 64 levels or holding more than 100,000 values. The choices in use stay, the file stays, and the notice names the limit. A preferences file is a dozen keys and one or two levels deep, so it sits far under those limits.

| Key | Default | Values |
| --- | --- | --- |
| `colorScheme` | `"system"` | `"system"`, `"light"`, `"dark"` |
| `accentColor` | `""` | `""` for Windows' accent, or `"#rrggbb"` |
| `backdrop` | `"acrylic"` | `"acrylic"`, `"mica"` |
| `taskbarCentered` | `true` | |
| `taskbarAutoHide` | `false` | |
| `taskbarAllScreens` | `true` | `false` shows a taskbar on the main display only |
| `taskbarAppsOn` | `"all"` | `"all"`, `"main"`, `"where"`, as `Taskbar.appsOn` |
| `startRecommended` | `true` | |
| `calendarWeekNumbers` | `false` | `true` shows week numbers beside the calendar |
| `clockSeconds` | `false` | `true` shows seconds on the taskbar clock. It wakes the shell every second, so it is off by default. Not in Winshell Settings. |
| `hiddenNotificationApps` | `[]` | `{ id = "<AppUserModelID>", name = "<app name>" }` for each app turned off |
| `startHotkey` | `"Win"` | a sequence as `Hotkey.sequence` takes it, or `""` |
| `settingsHotkey` | `""` | the same |
| `autoUpdate` | `true` | `false` stops automatic checks and downloads. Only a portable Winshell updates itself. |

Start pins and quick settings tiles stay in the files those surfaces already keep: `launcher.toml` next to the shell, with the app ids in order as `pins = [...]`, and `quick-settings.toml` in its state folder, with the tile keys in order as `pinned = [...]`. Start and quick settings follow those files as they change, and keep their comments the same way. The sign-in entry is the `Winshell` value under the user's `Run` key; see [where files live](development.md#where-files-live).

In QML, `Preferences` is the singleton that reads and writes `preferences.toml`, through a `TomlAdapter`. Surfaces bind to it: `centered: Preferences.taskbarCentered`, `WindowEffects.backdrop: Preferences.material`. It sets `Theme.colorScheme`, `Theme.accentColor` and `Updates.automatic`, and `Shell.configRoot`, `Shell.logPath` and `Shell.version` fill the About page.

## Checks only a live session can do

1. Run the default shell and right-click an empty part of the taskbar. Choose **Taskbar settings**. The window opens on the Taskbar page.
2. Turn **Automatically hide the taskbar** on. Every taskbar slides away at once.
3. Pick **Light** while Windows is dark. The taskbar, flyouts and the Settings title bar turn light; Windows' own windows do not.
4. Pick **Mica**. The taskbar and the flyouts show Mica instead of Acrylic.
5. Turn on Narrator and move through the pane and a page with Tab and the arrow keys. Each page is read as a list item, selected or not, and each control by its setting.
6. Turn on **Run Winshell when I sign in**, sign out and in again. Winshell starts without a console window, and Windows' Startup apps page lists it.
7. In a portable Winshell older than the latest release, choose **Check for updates** on the About page. It downloads the release, the notification area shows the update icon, and **Restart now** brings Winshell back at the new version.
