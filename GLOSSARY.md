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
_Avoid_: bar, taskbar, dock

**Popup window**:
A frameless window placed next to another window.
_Avoid_: popup, flyout

**Popup**:
A control shown in the overlay of the window that holds it. It is not a window of its own.
_Avoid_: popup window, flyout, dialog

**Screen**:
One monitor on the desktop.
_Avoid_: display, output

**Backdrop**:
What shows through the transparent parts of a window: nothing, or a Windows 11 material.
_Avoid_: blur, wallpaper, effect

**Item**:
A node in a window's visual tree.
_Avoid_: widget, element, control

**Control**:
An item with the behavior of a standard control, drawn in the Windows 11 style.
_Avoid_: widget

## The language

**Module**:
A named group of QML types a document imports.
_Avoid_: package, library, namespace

**Binding**:
A property whose value is the result of an expression, updated when the values that expression reads change.
_Avoid_: watcher, computed property

**Singleton**:
The one object of a type for a shell.
_Avoid_: global, service
