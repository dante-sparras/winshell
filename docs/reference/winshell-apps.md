# Winshell.Apps

Installed apps, search, jump lists and recent documents. Reach them from `Shell.Current.Apps`, `Shell.Current.JumpLists` and `Shell.Current.RecentDocuments`.

## AppEntry

One app of the Start menu.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `AppId` | string, read-only | The AppUserModelID the app's windows carry, or its program's name in lower case. |
| `Arguments` | string, read-only | The shortcut's arguments. Empty for a packaged app. |
| `CommandLine` | string, read-only | A command line that describes how the app is opened. |
| `Description` | string, read-only | The shortcut's comment, or the packaged app's description. May be empty. |
| `Folders` | IReadOnlyList of string, read-only | The Start menu folders the shortcut is in. Empty for a packaged app. |
| `Hidden` | bool, read-only | True for an uninstaller or a document shortcut. Hidden apps stay out of `Apps`, `Search`, `Recent` and `Frequent`. |
| `Icon` | string, read-only | A path an icon is read from: the shortcut, or `shell:AppsFolder\<id>` for a packaged app. |
| `Id` | string, read-only | A shortcut's path under Programs, such as `Accessories/Notepad`, or a packaged app's AppUserModelID. |
| `LastUsed` | long, read-only | When the app was last used, in Unix milliseconds, or 0 when Windows has no record. |
| `Name` | string, read-only |  |
| `Packaged` | bool, read-only | True when the app came from PackageManager. |
| `RunCount` | int, read-only | How many times Windows, or Launch, has run the app. |
| `StartIn` | string, read-only | The shortcut's Start in folder. |
| `Target` | string, read-only | The program the shortcut opens, or `shell:AppsFolder\<id>` for a packaged app. |
| `Launch()` | void | Opens the app as the Start menu does, through its shortcut or its AppUserModelID. A packaged app starts in the background, so this returns before it is up. |

## AppJumpList

One read of an app's jump list.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Available` | bool, read-only | False when the recent and frequent lists cannot be read. |
| `Destinations` | IReadOnlyList of string, read-only | Document names, recent first. Empty when the list cannot be read, or when it was read and has none. |

## AppsService

The Start menu's shortcuts and packaged apps. Find finds the app of a window, and Search ranks a typed query.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Apps` | ObservableCollection of [AppEntry](#appentry), read-only | The apps that are shown in Start, by name. |
| `Frequent` | ObservableCollection of [AppEntry](#appentry), read-only | Visible apps with a use count, most used first. |
| `Recent` | ObservableCollection of [AppEntry](#appentry), read-only | Visible apps Windows has a last-used time for, newest first. |
| `ById(string id)` | [AppEntry](#appentry)? | An entry by its id: a shortcut's path under Programs, or a packaged app's AppUserModelID. Includes hidden entries. |
| `Dispose()` | void |  |
| `Find(string name)` | [AppEntry](#appentry)? | Finds an app by id, then by the app id a window carries, then by display name. Includes hidden entries. |
| `Search(string query)` | IReadOnlyList of [AppEntry](#appentry) | Visible apps the query matches, best first. A blank query matches nothing. |

## JumpListService

Reads the recent and frequent documents Windows will give for an app. It does not open them, pin them, or change the list.

| Member | Type | Description |
| --- | --- | --- |
| `ForApp(string appId)` | [AppJumpList](#appjumplist) | The recent and frequent documents for an AppUserModelID. A blank id is not read. The same object is returned for an id, with its properties updated from the latest read. |

## RecentDocument

A shortcut in the Recent folder: its name without `.lnk`, and its path.

| Member | Type | Description |
| --- | --- | --- |
| `RecentDocument(string Name, string Path)` | constructor |  |
| `Name` | string, init |  |
| `Path` | string, init |  |

## RecentDocuments

Recent documents Windows already keeps as shortcuts in the Recent folder. This reads those names. It does not index files, and a blank query does not open the folder.

| Member | Type | Description |
| --- | --- | --- |
| `Search(string query)` | IReadOnlyList of [RecentDocument](#recentdocument) | Shortcuts whose name contains every word of query, newest first, at most Limit. A blank query matches nothing and does not read a folder. The path is not searched. |

