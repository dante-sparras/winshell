# Winshell.Apps

`import Winshell.Apps`. The apps Start lists: the Start menu shortcuts for all users and the current user, and the packaged apps Windows has installed. A shell uses it for its own Start, for search, and to match an app window to its app with `Apps.find(window.appId)`, as the default [taskbar](../taskbar.md#apps) does.

A shortcut in the user's Programs folder replaces the all-users shortcut with the same path, and the Startup folder is not listed. An app whose windows carry an AppUserModelID that a packaged app has is listed once, as the packaged app. The list updates when apps are installed or removed. [Migrating from Quickshell](../migrating-from-quickshell.md#apps) has more on search ranking and maps the `DesktopEntries` names onto these.

## Apps

The `Apps` singleton: the Start menu's shortcuts and packaged apps. Reach it as `Apps`, for example `Apps.search("note")`.

| Member | Type | Description |
| --- | --- | --- |
| `apps` | ObjectModel of [AppEntry](#appentry), read-only | The apps that are shown in Start, sorted by name. Hidden apps are left out. |
| `recent` | ObjectModel of [AppEntry](#appentry), read-only | Visible apps Windows has a last-used time for, newest first. |
| `frequent` | ObjectModel of [AppEntry](#appentry), read-only | Visible apps with a use count, most used first. |
| `byId(id)` | method, returns [AppEntry](#appentry) | The app with that id, a shortcut's path under Programs or a packaged app's AppUserModelID, or null. Includes hidden apps. |
| `find(name)` | method, returns [AppEntry](#appentry) | Finds an app by id, then by the app id a window carries, then by display name, or returns null. Includes hidden apps. |
| `search(query)` | method, returns list of [AppEntry](#appentry) | Visible apps the query matches, best first. A blank query matches nothing. |

`search` ranks an exact name first, then a name that starts with the query, a word, initials (`vsc` for Visual Studio Code), a part of the name, a close misspelling (`notpad` for Notepad), and last the description, the target, or a folder. Recent and frequent use lifts an app among matches of the same kind.

## AppEntry

One app of Start: a shortcut or a packaged app. You get entries from `Apps`; QML cannot create one.

| Member | Type | Description |
| --- | --- | --- |
| `id` | string, read-only | A shortcut's path under Programs without `.lnk`, such as `Accessories/Notepad`, or a packaged app's AppUserModelID. |
| `name` | string, read-only | The name Start shows: the shortcut's file name, or the packaged app's display name. |
| `description` | string, read-only | The shortcut's comment, or the packaged app's description. May be empty. |
| `icon` | string, read-only | A path the icon provider draws: the shortcut, or `shell:AppsFolder\<id>` for a packaged app. Use it as an `Image` source. |
| `target` | string, read-only | The program the shortcut opens, or `shell:AppsFolder\<id>` for a packaged app. Empty when the shortcut's target does not exist. |
| `arguments` | string, read-only | The shortcut's arguments. Empty for a packaged app. |
| `commandLine` | string, read-only | A command line that describes how the app is opened: the target, quoted when it has spaces, and the arguments. |
| `startIn` | string, read-only | The shortcut's Start in folder, or the target's folder when the shortcut has none. |
| `appId` | string, read-only | The AppUserModelID the app's windows carry, or its program's name in lower case. It matches `AppWindow.appId` in [Winshell.Windows](winshell-windows.md#appwindow). |
| `folders` | list of string, read-only | The Start menu folders the shortcut is in, outermost first. Empty for a packaged app. |
| `hidden` | bool, read-only | True for an uninstaller or a document shortcut. Hidden apps stay out of `apps`, `search`, `recent`, and `frequent`. |
| `packaged` | bool, read-only | True for a packaged app, such as a Microsoft Store app. |
| `runCount` | int, read-only | How many times Windows, or `launch()`, has run the app. |
| `lastUsed` | real, read-only | When the app was last used, in Unix milliseconds, or 0 when Windows has no record. |
| `launch()` | method | Opens the app as Start does, through its shortcut or its AppUserModelID. A launch counts as a use right away, in `runCount`, `lastUsed`, `recent` and `frequent`. When it fails, a warning goes to the log. |

## Examples

Type to search Start's apps; Enter opens the best match.

```qml
import QtQuick
import QtQuick.Controls
import Winshell.Apps

Column {
    TextField {
        id: query
        placeholderText: "Search apps"
        onAccepted: {
            const best = Apps.search(text)[0]
            if (best)
                best.launch()
        }
    }
    Repeater {
        model: Apps.search(query.text)
        Button {
            required property var modelData
            text: modelData.name
            onClicked: modelData.launch()
        }
    }
}
```

The five apps used most, each with its icon.

```qml
import QtQuick
import Winshell.Apps

Row {
    Repeater {
        model: Apps.frequent.values.slice(0, 5)
        Image {
            required property var modelData
            source: modelData.icon
            sourceSize: Qt.size(32, 32)
        }
    }
}
```
