# Configuration guide

A configuration is a folder of QML files that describes a shell. This guide builds one up from a single panel. The [reference](../reference/index.md) lists every type and member; the examples here are short, and each one is a complete `shell.qml` you can run with `winshell --path <folder>`.

## The folder

```text
%APPDATA%\winshell\
├── shell.qml          # the root, loaded first
├── Bar.qml            # a type of your own: Bar { }
├── widgets\
│   └── Clock.qml      # import "widgets", then Clock { }
├── util.js            # import "util.js" as Util
└── preferences.toml   # Winshell Settings' choices, if you use the default shell's
```

`shell.qml` is the only file Winshell looks for. Every other file is loaded because `shell.qml`, or a file it uses, refers to it:

- **A `.qml` file in the same folder is a type** named after the file: `Bar.qml` is `Bar { }`. Its root object's properties, signals and functions are the type's members.
- **A subfolder** is imported with `import "widgets"`, or with `import config.widgets`, which always means a folder in the configuration's own folder.
- **A JavaScript file** is imported with `import "util.js" as Util`, and its functions are `Util.name()`. Without `.pragma library`, each component that imports the file gets its own copy, and that copy can use the component's ids and properties. The file's own variables and functions come first. `.pragma library` shares one copy, which cannot see them. A `.import` inside the file names another `.js` file, and a path in quotes may contain a space. A `.import` of a QML module, such as `.import QtQuick 2.0 as Q`, is skipped with a warning.
- **A singleton** is a file whose first line is `pragma Singleton` and whose root is a [`Singleton`](../reference/winshell.md#singleton). Every file that uses it shares the one object, by the file's name. The default shell's `Preferences.qml` is one.

Winshell reloads when you save any of these files ([reload](getting-started.md#reload)).

## The root and the screens

The root of `shell.qml` is a [`ShellRoot`](../reference/winshell.md#shellroot). It holds the shell's windows and everything else it runs, and has the shell's `settings`. Most shells make their panels once per screen with [`Variants`](../reference/winshell.md#variants): it makes one copy of its delegate for each entry of its `model`, gives each copy its entry as `modelData`, and adds and removes copies as the model changes. [`Shell.screens`](../reference/winshell.md#shell) changes as monitors are connected and removed.

```qml
import QtQuick
import Winshell
import Winshell.Theme

ShellRoot {
    Variants {
        model: Shell.screens

        PanelWindow {
            required property var modelData
            screen: modelData
            anchors.top: true
            anchors.left: true
            anchors.right: true
            implicitHeight: 32
            color: "transparent"
            WindowEffects.backdrop: WindowEffects.Acrylic

            SystemClock { id: clock; precision: SystemClock.Minutes }
            CalendarMonth { id: format; date: clock.date }

            Text {
                anchors.centerIn: parent
                text: format.timeLabel
                color: Theme.textFillColorPrimary
                font: Theme.typography.body
            }
        }
    }
}
```

That is a bar across the top of every screen with the time in the user's regional format, on Acrylic, in the theme's text color. Because it is anchored to the top edge and both sides, it reserves its height there: maximized windows stop below it, as they stop above the taskbar.

## Windows

A shell has three kinds of [windows](../reference/winshell.md#shellwindow). Sizes are in logical pixels; Winshell scales them for each screen.

- **[`PanelWindow`](../reference/winshell.md#panelwindow)** sits on the edges of a screen. Anchored to one edge (and optionally both edges across it), it reserves space there as a Windows app bar. `exclusionMode: ExclusionMode.Ignore` makes it reserve nothing, as an overlay. `aboveWindows: false` puts it behind other windows, as a desktop widget.
- **[`PopupWindow`](../reference/winshell.md#popupwindow)** opens next to another window or an item in one: Start, quick settings and menus are popup windows. `grabFocus: true` gives it the keyboard and closes it when the user clicks elsewhere.
- **[`FloatingWindow`](../reference/winshell.md#floatingwindow)** is a normal window with a title bar, such as Winshell Settings.

A window draws its `color` first. Make it `"transparent"` and set `WindowEffects.backdrop` to `WindowEffects.Mica`, `Acrylic` or `MicaAlt` for a Windows 11 material behind it, and `WindowEffects.corners` for rounded corners. `mask` limits which part of a window takes clicks.

```qml
import QtQuick
import QtQuick.Controls
import Winshell

ShellRoot {
    PanelWindow {
        anchors.bottom: true
        anchors.left: true
        anchors.right: true
        implicitHeight: 48

        Button {
            id: button
            anchors.centerIn: parent
            text: "Open"
            onClicked: popup.visible = !popup.visible
        }

        PopupWindow {
            id: popup
            anchor.item: button
            anchor.edges: Edges.Top
            anchor.gravity: Edges.Top
            anchor.margins.top: 8
            implicitWidth: 320
            implicitHeight: 200
            grabFocus: true
            color: "transparent"
            WindowEffects.backdrop: WindowEffects.Acrylic
            WindowEffects.corners: WindowEffects.RoundCorners

            Label { anchors.centerIn: parent; text: "Hello" }
        }
    }
}
```

## The Windows 11 look

`QtQuick.Controls` draws Windows 11 controls whichever style you import, and Narrator can read and use them. [`Theme`](../reference/winshell-theme.md) has light or dark, the accent color, and the WinUI 3 tokens: colors such as `Theme.textFillColorPrimary` and `Theme.cardBackgroundFillColorDefault`, the type ramp as `Theme.typography.body` and `.subtitle`, corner radii and animation timings. Tokens follow the user's Windows settings as they change. A shell can choose light or dark and an accent of its own with `Theme.colorScheme` and `Theme.accentColor`; a contrast theme still wins.

[Qt Quick](../reference/qt-quick.md) lists the items, controls, layouts and effects there are.

## Reading Windows

The `Winshell.*` modules read Windows and let a shell change what Windows lets it change. Their singletons are ready to bind to:

```qml
import QtQuick
import QtQuick.Controls
import Winshell
import Winshell.Audio
import Winshell.Power

ShellRoot {
    PanelWindow {
        anchors.bottom: true
        anchors.right: true
        exclusionMode: ExclusionMode.Ignore
        implicitWidth: 240
        implicitHeight: 48

        Row {
            anchors.centerIn: parent
            spacing: 12

            Slider {
                width: 140
                from: 0
                to: 1
                value: Audio.defaultPlaybackDevice?.audio?.volume ?? 0
                onMoved: Audio.defaultPlaybackDevice.audio.volume = value
            }
            Label {
                visible: Power.displayDevice?.isPresent ?? false
                text: Math.round(Power.displayDevice.percentage * 100) + "%"
            }
        }
    }
}
```

| To show or change | Use |
| --- | --- |
| Volume, mute, the output device, per-app volume | [`Winshell.Audio`](../reference/winshell-audio.md) |
| What is playing, play and pause | [`Winshell.Media`](../reference/winshell-media.md) |
| Battery, power mode, brightness, idle | [`Winshell.Power`](../reference/winshell-power.md) |
| Apps to launch and search | [`Winshell.Apps`](../reference/winshell-apps.md) |
| Other programs' windows, previews, virtual desktops | [`Winshell.Windows`](../reference/winshell-windows.md) |
| Wi-Fi and network state | [`Winshell.Network`](../reference/winshell-network.md) |
| Bluetooth devices | [`Winshell.Bluetooth`](../reference/winshell-bluetooth.md) |
| Notifications and toasts | [`Winshell.Notifications`](../reference/winshell-notifications.md) |
| Tray icons | [`Winshell.Tray`](../reference/winshell-tray.md) |
| Hotkeys | [`Winshell.Input`](../reference/winshell-input.md) |
| Screenshots and live captures | [`Winshell.Capture`](../reference/winshell-capture.md) |

Reading never asks for permission or changes anything. Wi-Fi scanning, notification access and Bluetooth pairing ask Windows only when your configuration calls the method for it, from a user's action. Locking calls [`Session.lock()`](../reference/winshell.md#session), and Windows' own lock screen takes over.

## Running programs

`Shell.execDetached(["notepad.exe"])` starts a program and forgets it. To read its output or know when it ends, use a [`Process`](../reference/winshell-io.md#process):

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    Process {
        running: true
        command: ["cmd.exe", "/c", "ver"]
        stdout: StdioCollector {
            onStreamFinished: console.log("Windows says: " + text.trim())
        }
    }
}
```

`console.log`, `console.warn` and `console.error` write to the instance's log, which `winshell log` prints.

## Keeping data

- **Across reloads:** [`PersistentProperties`](../reference/winshell.md#persistentproperties) keeps its properties' values when the configuration reloads. They are lost when Winshell exits.
- **Across runs:** a [`FileView`](../reference/winshell-io.md#fileview) with a [`TomlAdapter`](../reference/winshell-io.md#tomladapter) keeps properties in a TOML file a person can edit by hand. Put it in the shell's own folders: `Shell.dataPath("choices.toml")`, `Shell.statePath(...)` or `Shell.cachePath(...)`, which are per shell under `%LOCALAPPDATA%\winshell`. Give `FileView.path` a full path like these, since a relative one depends on the working directory. [`JsonAdapter`](../reference/winshell-io.md#jsonadapter) stays for app state nobody edits by hand.

A change to `choices.compact` writes the file, and a change made to the file by hand updates `choices.compact`:

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    FileView {
        path: Shell.dataPath("choices.toml")
        blockLoading: true
        watchChanges: true
        onFileChanged: reload()
        onAdapterUpdated: writeAdapter()

        TomlAdapter {
            id: choices
            property bool compact: false
        }
    }
}
```

## Commands from outside

An [`IpcHandler`](../reference/winshell-io.md#ipchandler) lets `winshell ipc` call functions of the running shell, from a script, a keyboard tool or a scheduled task:

```qml
import QtQuick
import Winshell
import Winshell.Io

ShellRoot {
    IpcHandler {
        target: "shell"
        function reload(): void { Shell.reload(false) }
        function screens(): int { return Shell.screens.length }
    }
}
```

```powershell
winshell ipc call shell reload
winshell ipc call shell screens
```

## Hotkeys

A [`Hotkey`](../reference/winshell-input.md) works anywhere in Windows. `sequence` uses Windows names: `"Win+Shift+S"`, `"Ctrl+Alt+T"`, `"Win"` for the Windows key alone, or `"Ctrl+K, C"` for a sequence. A chord Windows or another app already holds is reported as `taken` and isn't stolen. See [hotkeys](../hotkeys.md).

## Pragmas

Lines at the very top of `shell.qml`, before any `import`, set up the shell:

| Line | What it does |
| --- | --- |
| `//@ pragma ShellId <id>` | The shell id, which names its data, state and cache folders. Without it, a config in the config folder is named after its folder: `root` for the folder itself, or the `-c` name, so moving the config folder keeps them. A shell anywhere else is named after the path of its `shell.qml`, so moving it starts with empty folders. |
| `//@ pragma AppId <id>` | `Shell.appId`, instead of `winshell` or `WINSHELL_APP_ID`. |
| `//@ pragma DataDir <path>`, `StateDir`, `CacheDir` | Other folders for `Shell.dataDir`, `stateDir` and `cacheDir`. A relative path is next to the shell file, and `%NAME%` expands. `$BASE` is the folder above `by-shell`, such as `%LOCALAPPDATA%\winshell\data`. |
| `//@ pragma Env NAME=value` | Sets an environment variable for this process, such as for the programs it starts. One per line. |

`//@ if hasVersion(0, 6)` … `//@ elif` … `//@ else` … `//@ endif` keeps lines only for a Winshell release, so one configuration can run on several. `hasQtVersion(6, 10)` checks the Qt Quick language level instead.

## Reloads

Saving a file reloads the configuration ([getting started](getting-started.md#reload)). A reload makes a new generation of every object, then hands the old generation's windows and `PersistentProperties` to the new one, matched by their place in the tree or by `reloadableId`, so windows don't flicker. `Shell.reload(true)` reloads without handing anything over, as if Winshell had just started.

`Shell.reloadCompleted` and `Shell.reloadFailed(errorString)` tell the configuration how a reload went. A failed reload keeps the old generation running. A reload does not apply a new `ShellId`, `AppId`, `DataDir`, `StateDir`, `CacheDir` or `Env`. Those stay as they were at launch, and the log warns that a restart is needed.

## Building on the default shell

The default shell is a configuration like any other. [Copy it](getting-started.md#make-the-default-shell-your-own) and change what you want: its surfaces are separate files, and `Taskbar` has hooks for your own Start, Search, tray area and clock. See [the default shell](../reference/default-shell.md) and [the taskbar](../taskbar.md#hooks).

## Coming from Quickshell

Winshell began as a port of Quickshell v0.3.2, and the QML is the same language, but its modules and types have Windows names ([ADR-0007](../adr/0007-windows-first-api.md)): `Quickshell` is `Winshell`, `PwNode` is `AudioNode`, `MprisPlayer` is `MediaSession`, `Toplevel` is `AppWindow`, `GlobalShortcut` is `Hotkey`. A Quickshell configuration doesn't load as it is: rename its imports and types, replace Linux commands and paths with Windows ones, and drop what Windows doesn't have, such as the session lock, which [Windows owns](../adr/0008-windows-owns-the-lock-screen.md).

[Migrating from Quickshell](../migrating-from-quickshell.md) has every renamed module, type and property, and what was removed.
