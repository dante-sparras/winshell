# Winshell.Updates

A portable Winshell checking GitHub Releases and installing a download. Reach it from `Shell.Current.Updates`. A configuration's own program does not use the updater `winshell.exe` passes in.

## InstalledUpdate

An update put in place as Winshell started, or, with Error, one that was not.

| Member | Type | Description |
| --- | --- | --- |
| `InstalledUpdate(string Version, string? Error)` | constructor |  |
| `Error` | string?, init |  |
| `Version` | string, init |  |

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

## Updater

Updates a portable Winshell from its repository's GitHub Releases, for the whole process: every engine's `Updates` follows the one updater. It finds the latest release, downloads the zip for this architecture, checks its SHA-256 against the digest GitHub reports, and unzips it into a folder of its own under `%LOCALAPPDATA%\winshell\updates`. UpdateInstall applies it when Winshell next starts.

| Member | Type | Description |
| --- | --- | --- |
| `Restart` | Func of string? | Starts the shell over, so the update applies, and returns null; or returns why it does not, such as another Winshell running, whose files the update must not replace. The host that runs the shell sets it. |
| `UpdatesDir` | string, static, read-only | Downloaded updates, each unzipped in a folder named for its version. |
| `Dispose()` | void |  |
| `ForRelease(string? repository, string currentVersion, [Log](winshell.md#log) log)` | [Updater](#updater)?, static | The updater for this process, or null when Winshell does not update itself: a build from source, which names no release repository; a packaged Winshell, which its installer (the Microsoft Store) updates; or a winget install, which winget updates. A self-update would rewrite files winget has hashed. |
| `InstallStaged(string? repository, string currentVersion)` | [InstalledUpdate](#installedupdate)?, static | Before a shell starts: deletes what the last update left behind and puts a downloaded update in place. Returns null when there was none to install, or the version and why it did not install, with `Error` null when it did. Nothing happens in a Winshell that does not update itself. |
| `ReportApplyFailure(string version, string error)` | void | Records an update that downloaded but did not apply when Winshell started. |

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

