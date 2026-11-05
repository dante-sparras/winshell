# Winshell

Winshell is a toolkit for writing a Windows 11 shell. It ships no shell of its own. A configuration is a .NET project that builds into one program and describes the windows it shows.

## The shell

**Shell**:
The desktop shell one configuration describes, running as one process.
_Avoid_: app, host, session

**Configuration**:
The .NET project that defines a shell, built into one program.
_Avoid_: theme, skin, script

**Shell id**:
The name that identifies a configuration across runs.
_Avoid_: instance id, path

**Instance**:
One running process of a shell. A reload replaces it. The CLI lists, logs, and kills instances.
_Avoid_: generation, session

**Windows session**:
The signed-in Windows user session the shell runs in. Windows locks and unlocks it; the shell can only ask it to lock.
_Avoid_: logon session, user session

**Reload**:
Building the configuration and replacing the running instance with the result.
_Avoid_: restart, refresh, generation

**Notice**:
Winshell's own window, drawn like a Windows 11 notification, for a configuration that did not build or a failure Winshell reports. It is Winshell's, not the configuration's.
_Avoid_: toast, popup, flyout

**Crash report**:
A text file Winshell writes on this PC when the shell itself fails: where, the exception, and the end of the log. A project that did not build is a build error, not a crash.
_Avoid_: dump, telemetry

## What it shows

**Window**:
A top-level surface the shell places on the desktop.
_Avoid_: form, view

**Panel**:
A window anchored to an edge of a screen. It can reserve that edge.
_Avoid_: bar, taskbar, dock, layer surface

**Popup window**:
A frameless window placed next to another window.
_Avoid_: popup, flyout

**Flyout**:
A control shown over a window, in that window's overlay. It is not a window of its own.
_Avoid_: popup, popup window, dialog

**Taskbar**:
Explorer's own bar along the bottom of each screen, the Windows taskbar. A shell's bar is a panel.
_Avoid_: bar, dock, task list

**Preview**:
The live image of an app window the taskbar shows over its button.
_Avoid_: thumbnail (that is the DWM API it uses), peek

**Preferences**:
A configuration's own choices, kept in a TOML file it reads through `TomlFile`.
_Avoid_: settings (that is Windows Settings)

**Screen**:
One monitor on the desktop.
_Avoid_: display, output

**Work area**:
The part of a screen that windows maximize into: the screen without the taskbar and the edges panels reserve.
_Avoid_: available geometry, usable area

**Scale**:
How many physical pixels a screen draws for one logical pixel, from its DPI. 144 DPI is a scale of 1.5.
_Avoid_: zoom, DPI factor

**Backdrop**:
What shows through the transparent parts of a window: nothing, or a Windows 11 material.
_Avoid_: blur, wallpaper, effect

**Control**:
A WinUI control.
_Avoid_: widget, item

**Theme**:
Light or dark, whether a contrast theme is on, the accent color and the system colors, as Windows reports them. A shell can choose light or dark and its own accent on top; a contrast theme still wins.
_Avoid_: skin, style, palette

## What it reads from Windows

**App window**:
A top-level window of another program, as the taskbar and Alt+Tab list it.
_Avoid_: toplevel, client

**Virtual desktop**:
A desktop Task View lists and can switch to. An app window is on one of them.
_Avoid_: workspace

**App**:
A program Start lists: a shortcut or a packaged app.
_Avoid_: desktop entry, application

**Start**:
The launcher opened from the Windows key or the taskbar Start button.
_Avoid_: start menu, app drawer

**Playback device**:
A Windows audio endpoint that plays sound, such as speakers or headphones.
_Avoid_: sink, audio sink, render device

**Recording device**:
A Windows audio endpoint that records sound, such as a microphone.
_Avoid_: source, audio source, capture device

**Audio session**:
One program's volume and mute on a playback or recording device, as the volume mixer lists it.
_Avoid_: stream, sink input

**Media session**:
A program playing media that Windows lets the shell see and control.
_Avoid_: player, MPRIS player

**Power mode**:
The slider Windows offers for Best power efficiency, Balanced, or Best performance.
_Avoid_: power profile, overlay scheme, power plan

**Monitor brightness**:
The brightness setting of a connected monitor.
_Avoid_: backlight

**Tray icon**:
An app's icon in the notification area, which the app adds, changes and removes itself.
_Avoid_: status notifier item, SNI, indicator

**Overflow**:
The hidden icons flyout behind the taskbar's chevron. Settings chooses which tray icons the taskbar shows; the rest are in the overflow.
_Avoid_: hidden icons (an app can also hide its own icon), drawer

**Hotkey**:
A key chord or a short sequence of chords that works anywhere in Windows, such as `Ctrl+Shift+K` or `Win`.
_Avoid_: global shortcut, keybind, accelerator, bind

## The language

**Module**:
A Winshell namespace.
_Avoid_: package, library
