# Winshell updates

A portable Winshell, the zip from GitHub Releases or the winget package, updates itself from that repository's releases. The release build names the repository: `scripts/package.ps1` stamps it into `winshell.exe`. Two kinds of Winshell never update themselves:

- A build from source has no repository stamped in it.
- A packaged Winshell, such as the Microsoft Store's, is updated by whatever installed it.

## How an update arrives

1. Winshell asks `https://api.github.com/repos/<repository>/releases/latest` for the latest release. A tag that is not newer than this Winshell is up to date.
2. It downloads `winshell-<version>-win-x64.zip`, or `-win-arm64.zip` on an Arm PC. While downloading, it computes the file's SHA-256 and compares it with the `digest` GitHub reports for that file. A file without a digest, or whose hash differs, is deleted and never installed.
3. It unzips the file into `%LOCALAPPDATA%\winshell\updates\<version>`. The update is now ready.
4. The next `winshell.exe` that starts a shell while no other Winshell runs installs the update before the shell runs. While another shell runs, it would keep loading files from the folder as it needs them and mix versions, so the update waits, still ready, until a start finds no other shell. Two starts at the same moment, before either is listed as running, share a named mutex so they do not install over each other. The start that gets the mutex installs. The other does not wait for it: it skips the install, and the update stays ready. Each file the update replaces moves into `.winshell-old` in Winshell's folder, and the new one is copied in. If a file cannot be replaced, every file goes back and the update stays ready for the next start. The new `winshell.exe` then runs the shell, in the same console, with the same arguments. A later start that finds no other shell deletes `.winshell-old` and older or unfinished downloads.

**Restart to update** starts `winshell.exe` again with no console window. It drops `-d` and `--daemonize`, keeps the other arguments this shell was started with, and adds `--after` with this process's id, then closes this shell. The new process waits up to 30 seconds for this one to exit, so the taskbar, the hotkeys and the tray can come free, and then installs the update as in step 4. While another Winshell runs, the restart could not install the update, so **Restart now** does not restart: the update stays ready and the About page says "Another Winshell is running. Close it, then restart this one."

Winshell checks for itself only while `Updates.automatic` is on: 30 seconds after it is turned on, and once a day after that. The default shell turns it on unless `autoUpdate` is `false` in `preferences.toml`, which **Get Winshell updates automatically** on the About page of Winshell Settings changes. **Check for updates** works either way. Winshell must be able to write to its own folder; if it cannot, it says so and downloads nothing.

## The `Updates` singleton

`import Winshell` provides `Updates` and the `UpdateStatus` enum.

| Member | What it is |
| --- | --- |
| `available` | Whether this Winshell updates itself. |
| `packaged` | Whether Winshell runs from an MSIX package, which its installer updates. |
| `automatic` | Check by itself and download what it finds. Off until a shell turns it on. |
| `status` | `UpdateStatus.Idle`, `Checking`, `UpToDate`, `Downloading`, `Ready` or `Failed`. An update downloaded before this run started is `Ready` from the start. |
| `version` | The release found: the latest one when up to date, or the one downloading or ready. |
| `error` | Why the last check, download or install failed, while `status` is `Failed`; or, while it is `Ready`, why `restart()` did not restart. |
| `check()` | Checks now, and downloads a newer release. |
| `restart()` | Starts Winshell over to install a `Ready` update. Does nothing otherwise. |

The default shell shows the status on the About page. While an update is `Ready`, an icon in the taskbar's notification area opens that card, like Windows Update's restart icon.

## Not checked by the automated tests

The tests download from a fake GitHub into temporary folders. They never contact GitHub or replace an installed Winshell. A real release has not been downloaded yet: the first will be the release after this feature ships. Installing on start, running the new `winshell.exe` and waiting for the old process were checked by hand, with two published builds of different versions in a temporary folder.
