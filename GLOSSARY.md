# Winshell

Winshell is a Windows 11 shell. A configuration written in QML describes the windows and items it shows.

## The shell

**Shell**:
The desktop shell one configuration describes, running as one process.
_Avoid_: app, host, session

**Configuration**:
The QML that defines a shell, loaded from one directory.
_Avoid_: theme, skin, project

**Shell id**:
The name that identifies a configuration across runs.
_Avoid_: instance id, path

**Instance**:
One running process of a shell. The CLI lists, logs, and kills instances.
_Avoid_: generation, session

**Generation**:
One load of a configuration. A reload retires it and starts the next.
_Avoid_: instance, session

**Reload**:
Replacing the running generation with a new load of the same configuration.
_Avoid_: restart, refresh

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

**Popup**:
A control shown in the overlay of the window that holds it. It is not a window of its own.
_Avoid_: popup window, flyout, dialog

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

**Item**:
A node in a window's visual tree.
_Avoid_: widget, element, control

**Control**:
An item with the behavior of a standard control, drawn in the Windows 11 style.
_Avoid_: widget

**Theme**:
Light or dark, whether a contrast theme is on, the accent color and the system colors, as Windows reports them.
_Avoid_: skin, style, palette

**Token**:
A named color, corner radius, font or timing of the Windows 11 look, made from the theme. It has the name of its WinUI 3 theme resource.
_Avoid_: brush, resource, variable

## What it reads from Windows

**App window**:
A top-level window of another program, as the taskbar and Alt+Tab list it.
_Avoid_: toplevel, client

**App**:
A program the Start menu lists: a shortcut or a packaged app.
_Avoid_: desktop entry, application

**Media session**:
A program playing media that Windows lets the shell see and control.
_Avoid_: player, MPRIS player

## The language

**Module**:
A named group of QML types a document imports. Winshell's own are `Winshell` and `Winshell.*`; the Qt Quick ones are `QtQuick` and `QtQuick.*`.
_Avoid_: package, library, namespace

**Binding**:
A property whose value is the result of an expression, updated when the values that expression reads change.
_Avoid_: watcher, computed property

**Singleton**:
The one object of a type for a shell.
_Avoid_: global, service
