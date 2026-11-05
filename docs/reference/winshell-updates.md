# Winshell.Updates

Winshell's own updates. Reach them from `Shell.Current.Updates`. A configuration's own program does not update Winshell. Update it with `install.ps1`, winget or the Store.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## UpdateStatus

Where an update is, as UpdatesService.Status reports it.

| Value | Number | Description |
| --- | --- | --- |
| `Idle` | 0 | Not checked yet in this run. |
| `Checking` | 1 | Asking GitHub Releases for the newest release. |
| `UpToDate` | 2 | No release is newer than the one running. |
| `Downloading` | 3 | A newer release is downloading. |
| `Ready` | 4 | Downloaded, its hash checked and unzipped. It applies when Winshell next starts. |
| `Failed` | 5 | The last check, download or install failed. UpdatesService.Error says why. |

## UpdatesService

Winshell's own updates. A configuration's own program does not update Winshell, so Available is false there. A downloaded update that is already staged still goes in place the next time `winshell run` starts a shell.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Automatic` | bool | Whether Winshell checks by itself, shortly after it starts and once a day, and downloads what it finds. Off until the shell turns it on; Check works either way. |
| `Available` | bool, read-only | Whether this Winshell updates itself. |
| `Error` | string, read-only | Why the last check, download or install failed, while Status is `Failed`; or, while it is `Ready`, why Restart did not restart. |
| `Packaged` | bool, read-only | True when Winshell runs from an MSIX package, such as the Microsoft Store's. The package's installer updates it. |
| `Status` | [UpdateStatus](#updatestatus), read-only | Where the update is: checking, up to date, downloading, ready, or failed. |
| `Version` | string, read-only | The release found: the newest one when up to date, the one downloading or ready. Empty before a check. |
| `Winget` | bool, read-only | True when this copy lives under winget's Packages or Links folder. winget updates it, and Winshell does not. |
| `Check()` | void | Checks now, and downloads a newer release. |
| `Dispose()` | void | Stops following the updater. A check or download in progress goes on. |
| `Restart()` | void | Starts Winshell over with the ready update. Does nothing while no update is `Ready`. |
