# Winshell.Tray

The icons apps add to the notification area. Reach them from `Shell.Current.SystemTray`.

## BalloonIcon

The icon a balloon notification asked for, from its `NIIF_` flags.

| Value | Number | Description |
| --- | --- | --- |
| `None` | 0 |  |
| `Info` | 1 |  |
| `Warning` | 2 |  |
| `Error` | 3 |  |
| `App` | 4 | The app's own icon. |

## SystemTray

The notification area icons of every app, in the order they were added. An icon the app hid is left out until the app shows it again. Only one running instance in the Windows session owns the notification area; in the others Items stays empty until that one exits.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Items` | ObservableCollection of [SystemTrayItem](#systemtrayitem), read-only |  |
| `Dispose()` | void | Gives the notification area up. Call on the loop's thread. |

## SystemTrayItem

An app's notification area icon. Its menu belongs to the app, which draws it itself. Display asks the app to open it.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppName` | string, read-only | The program's description, as Task Manager shows it, or its file name. |
| `Executable` | string, read-only | The full path of the program that added the icon. |
| `Icon` | [PixelImage](winshell.md#pixelimage)?, read-only | The icon as the app set it, at the size it chose. It changes when the app changes the icon. |
| `Id` | string, read-only | The program's file name without its extension. |
| `Menu` | [SystemTrayMenu](#systemtraymenu), read-only |  |
| `Overflow` | bool, read-only | Windows shows this icon in the hidden icons flyout, not in the taskbar. Users choose in Settings. |
| `Tooltip` | string, read-only | The icon's tooltip, which may run over several lines. |
| `Balloon` | Action of string, string, [BalloonIcon](#balloonicon) |  |
| `Ready` | Action |  |
| `Click()` | void | A left click at the cursor, which most apps answer by showing their window. |
| `Display()` | void | A right click, which asks the app to open its menu at the cursor. |
| `Display(int x, int y)` | void | A right click, which asks the app to open its menu at a point on the virtual desktop, in physical pixels. |
| `DoubleClick()` | void | A double click at the cursor, which many older apps need before they show their window. |
| `IconAt(int size)` | [PixelImage](winshell.md#pixelimage)? | The icon at a size in pixels, for the screen's scale. An icon loaded from a resource is drawn again at that size. Any other is the app's own bitmap, at the size it chose. |
| `MiddleClick()` | void |  |

## SystemTrayMenu

An app's tray menu. The app draws it; Display asks the app to open it.

| Member | Type | Description |
| --- | --- | --- |
| `Display()` | void | Opens the menu at the cursor. |
| `Display(int x, int y)` | void | Opens the menu at a point on the virtual desktop, in physical pixels. |

