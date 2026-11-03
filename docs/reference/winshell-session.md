# Winshell.Session

Locking the Windows session, and running at sign-in. Reach them from `Shell.Current.Session` and `Shell.Current.Startup`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## SessionService

The signed-in Windows session the shell runs in. Lock asks Windows to lock it, and Windows' own lock screen takes over. Winshell never draws a lock screen or asks for the Windows password (ADR-0008).

| Member | Type | Description |
| --- | --- | --- |
| `Lock()` | bool | Asks Windows to lock the session, as Win+L does. Returns whether Windows took the request; the lock itself happens a moment later, in Windows. |

## StartupService

Whether Winshell runs when the current user signs in. An unpackaged Winshell is a `Winshell` value under the user's `Run` key. A packaged Winshell is the package's startup task. Windows' Startup apps page lists either and can turn it off. Nothing is registered until the shell sets RunAtSignIn, and Winshell never changes what that page chose.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Available` | bool, read-only | Whether this shell can register itself: false when it does not run as `winshell.exe`. |
| `RegisteredElsewhere` | bool, read-only | True when the entry is there but starts Winshell another way: a `winshell.exe` in another folder, as after an update unzipped somewhere new, or other arguments. RunAtSignIn is false until it is set. |
| `RunAtSignIn` | bool | Whether this Winshell is registered to run at sign-in. Setting it adds or removes the entry, and setting it when the entry starts another `winshell.exe` points the entry here. |
| `TurnedOffInWindows` | bool, read-only | True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on. |
| `Refresh()` | void | Reads the entry again, after the user may have changed it in Windows. |
