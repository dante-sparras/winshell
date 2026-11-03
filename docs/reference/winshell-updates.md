# Winshell.Updates

A portable Winshell checking GitHub Releases and installing a download. Reach it from `Shell.Current.Updates`. A configuration's own program does not use the updater `winshell.exe` passes in.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## UpdateStatus

Where an update is: the `UpdateStatus` enum of `Updates.status`.

| Value | Number | Description |
| --- | --- | --- |
| `Idle` | 0 | Not checked yet in this run. |
| `Checking` | 1 |  |
| `UpToDate` | 2 |  |
| `Downloading` | 3 |  |
| `Ready` | 4 | Downloaded, its hash checked and unzipped. It applies when Winshell next starts. |
| `Failed` | 5 |  |

## UpdatesService

Winshell's own updates. A portable Winshell from GitHub Releases checks its releases, downloads a newer one and applies it when it next starts. A build from source, a packaged Winshell and a winget install never update themselves; Available is false for them.

Has the members of [ObservableObject](winshell.md#observableobject).

| Member | Type | Description |
| --- | --- | --- |
| `Automatic` | bool | Whether Winshell checks by itself, shortly after it starts and once a day, and downloads what it finds. Off until the shell turns it on; Check works either way. |
| `Available` | bool, read-only | Whether this Winshell updates itself. |
| `Error` | string, read-only | Why the last check, download or install failed, while Status is `Failed`; or, while it is `Ready`, why Restart did not restart. |
| `Packaged` | bool, read-only | True when Winshell runs from an MSIX package, such as the Microsoft Store's. The package's installer updates it. |
| `Status` | [UpdateStatus](#updatestatus), read-only |  |
| `Version` | string, read-only | The release found: the newest one when up to date, the one downloading or ready. Empty before a check. |
| `Winget` | bool, read-only | True when this copy lives under winget's Packages or Links folder. winget updates it, and Winshell does not. |
| `Check()` | void | Checks now, and downloads a newer release. |
| `Dispose()` | void |  |
| `Restart()` | void | Starts Winshell over with the ready update. Does nothing while no update is `Ready`. |
