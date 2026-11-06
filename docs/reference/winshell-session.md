# Winshell.Session

Locking the Windows session, and running at sign-in. Reach them from `Shell.Current.Session` and `Shell.Current.Startup`.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## SessionService

The signed-in Windows session the shell runs in. Lock asks Windows to lock it, and Windows' own lock screen takes over. Winshell never draws a lock screen or asks for the Windows password.

| Member | Type | Description |
| --- | --- | --- |
| `Lock()` | bool | Asks Windows to lock the session, as Win+L does. Returns whether Windows took the request; the lock itself happens a moment later, in Windows. |

## StartupService

Whether this configuration runs when the current user signs in. An unpackaged Winshell is a `Winshell` value under the user's `Run` key; the value starts this install's `winshell.exe` running this configuration. A packaged Winshell is the package's startup task. Windows' Startup apps page lists either and can turn it off. Nothing is registered until the shell sets RunAtSignIn, and Winshell never changes what that page chose.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Available` | bool, read-only | Whether this shell can register itself to run at sign-in. False when a packaged shell has no startup task, and false when this process is a configuration program that no `winshell.exe` is supervising. |
| `RegisteredElsewhere` | bool, read-only | True when the entry is there but starts another install or another configuration: a `winshell.exe` in another folder, as after an update unzipped somewhere new, or a different configuration. RunAtSignIn is false until it is set. |
| `RunAtSignIn` | bool | Whether this configuration is registered to run at sign-in. Setting it adds or removes the entry, and setting it when the entry starts another install or another configuration points the entry here: this install's `winshell.exe run` of this configuration. |
| `TurnedOffInWindows` | bool, read-only | True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on. |
| `Refresh()` | void | Reads the entry again, after the user may have changed it in Windows. |
