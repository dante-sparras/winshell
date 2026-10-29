# Development

## Prerequisites

- Git
- PowerShell 7
- .NET 10 SDK
- Node.js, for `npx` when updating agent skills
- The Windows SDK, for `makeappx.exe`, only to run `scripts/package.ps1`

Reading Windows notifications additionally requires listener permission and a package identity declaring `userNotificationListener`; native toast sending requires an installed Windows AppUserModelID. Neither is needed to build or run automated tests. The unpackaged CLI does not provision these identities; the Microsoft Store package declares the listener capability (see [Releasing](#releasing)). See [Windows notifications](notifications.md) for deployment requirements, permission behavior and platform limits.

## First checkout

```sh
git clone https://github.com/dante-sparras/winshell.git
cd winshell
pwsh scripts/dev.ps1
```

## Commands

| Command | What it does |
| --- | --- |
| `pwsh scripts/dev.ps1` | Clones Quickshell v0.3.2 into `docs/agents/quickshell/` and fetches its manual into `site/` there. |
| `pwsh scripts/dev.ps1 -Refresh` | Downloads the Quickshell manual again. |
| `pwsh scripts/check.ps1` | Builds `winshell.slnx` with warnings as errors, then runs `dotnet test` on that build. The reload leak test (`Category=Leak`) runs alone, because it counts handles and USER objects for the whole process. The Jint performance budgets (`Category=Performance`) run last and alone, and print what they measured. This is the check CI runs. |
| `pwsh scripts/package.ps1` | Builds what a release ships into `artifacts/`, then checks it: a self-contained `winshell-<version>-win-x64.zip` and `-win-arm64.zip`, `SHA256SUMS.txt`, and the winget manifests in `winget/<version>/` and `winget-manifests.zip`. The same commit packs to the same bytes. The check matches the checksums, the architecture of each zip's binaries, the version the x64 `winshell.exe` reports, and the manifests against the validator the pinned `wingetcreate` uses, downloaded from NuGet into `artifacts/tools/`. It also packs `winshell-<version>.msixbundle` for the Microsoft Store and checks each architecture's identity, version and executables; that needs `makeappx.exe` from the Windows SDK. `-Publish`, `-Pack` and `-Verify` run one step. `-Version` overrides the release from `.release-please-manifest.json`, `-Tag v1.2.3` stops unless the checkout is that release, `-Runtime` picks `win-x64`, `win-arm64` or both, `-Out` the folder. `-IdentityName`, `-Publisher` and `-PublisherDisplayName` give the Store package the identity Partner Center reserved; without them it has a test identity. |
| `dotnet run scripts/render-logo.cs` | Renders `packaging/logo.svg` into the package's and the Store listing's PNGs. Commit them after changing the logo. |
| `pwsh scripts/update-snapshots.ps1` | Renders the default shell's surfaces and replaces each [snapshot](#visual-snapshots) baseline its render no longer matches. `-Surface taskbar` replaces one surface's baselines only. |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before opening a PR, run the checks CI runs: `pwsh scripts/check.ps1`.

## Environment variables

A running shell reads these. None is needed to build or test.

| Variable | What it does |
| --- | --- |
| `WINSHELL_CONFIG_DIR` | Folder named configs are loaded from, instead of `%APPDATA%\winshell`. This run does not copy an old `~/.config/winshell`. |
| `WINSHELL_APP_ID` | Sets `Shell.appId` when the configuration has no `//@ pragma AppId`. |
| `WINSHELL_DISABLE_FILE_WATCHER` | Any value stops the shell from reloading when its files change. |
| `WINSHELL_NO_RELOAD_POPUP` | `1` hides Winshell's notice for a configuration that failed to load. A shell can hide it for one failed reload with `Shell.inhibitReloadPopup()` in `Shell.onReloadFailed`. The notice for a window that failed still shows. |

`WINSHELL_TEST_CRASH` is read only for `FatalCrashTests`: it names a folder for crash reports, and the shell crashes once its config has loaded, so the test sees the report a real crash writes. Never set it otherwise.

## Where files live

| What | Where |
| --- | --- |
| User configurations | `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` when it is set |
| Cache | `%LOCALAPPDATA%\winshell\cache` |
| State | `%LOCALAPPDATA%\winshell\state` |
| Per-shell data | `%LOCALAPPDATA%\winshell\data` |
| Instance logs | `%LOCALAPPDATA%\winshell\run\<id>\log.txt` |
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one `crash-<UTC time>Z-<process>-<where>.txt` each. Nothing is sent anywhere. A window whose native window cannot be made or placed, or throws while drawing or handling input, writes one, closes, and opens again after 1, 2, 4, 8 and 16 seconds; a sixth failure within five minutes leaves it closed until the next reload. A frame the graphics device cannot draw is skipped and reported once per window. Work on the event loop that throws, or a background task that fails, writes one per exception and stack, and a crash that ends the process writes one before the log closes. Reports leave out what scripts printed to the log and take out the passwords, tokens, keys and URL queries Winshell recognizes. The newest 50 reports are kept by their UTC time; only files named and headed as reports are deleted, and nothing is deleted when the folder is a link or junction. |
| Downloaded updates | `%LOCALAPPDATA%\winshell\updates\<version>`, until the next start installs them. See [Winshell updates](updates.md) |
| Winshell Settings choices | `preferences.json` in the user configurations folder; see [Winshell Settings](settings.md) |
| Run at sign-in | The `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written only when a shell sets `Startup.runAtSignIn`. It runs this `winshell.exe` under `conhost.exe --headless`, so no console window opens, with the arguments the shell was started with and `--no-duplicate`. A `--path` is made absolute. Environment variables are not copied: `WINSHELL_CONFIG_DIR` applies at sign-in only when it is set for the user. Windows' Startup apps page can turn the entry off; Winshell reads that and never changes it. An entry that starts another `winshell.exe`, or other arguments, reads as `Startup.registeredElsewhere`, and setting `runAtSignIn` rewrites it for this one. |

The first launch that uses the default config folder copies `%USERPROFILE%\.config\winshell` into `%APPDATA%\winshell` when the old folder exists. The copy leaves the old folder in place, and it does not replace a file already in the new folder.

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release and publishes it. The released version is `.release-please-manifest.json`. A build stamps that into the assembly unless `-p:Version=` overrides it, so `winshell --version`, `Shell.hasVersion`, and `//@ if hasVersion(...)` report that release. The printed version can also carry a `+` build id. A build that cannot read the manifest is `0.0.0-dev`.

The release workflow then runs the Package workflow (`.github/workflows/package.yml`) on the tagged commit, which runs `scripts/package.ps1` and uploads its files to the GitHub release: both portable zips, `SHA256SUMS.txt` and `winget-manifests.zip`. A file the release already has is never replaced: the same bytes are skipped, and different bytes stop the upload, since winget and users may hold the first file's hash. The same workflow builds and checks those files, without uploading them, on every push to `main` and on PRs that change packaging or the release workflow, and keeps them as the run's `packages` artifact.

With a `WINGET_TOKEN` secret, the release workflow also submits the manifests to [winget-pkgs](https://github.com/microsoft/winget-pkgs) with a pinned `wingetcreate`. The token is a classic personal access token with the `public_repo` scope, from the account whose fork of winget-pkgs opens the PR. Without it, submit them by hand: unzip `winget-manifests.zip` and run `wingetcreate submit --token <token> <folder>`. winget's moderators review every PR, and the first one for `DanteSparras.Winshell` before any other.

When a release has no files:

- **Packaging or the upload failed:** open the Release Please run and choose **Re-run failed jobs**. It keeps the release it created and runs only what failed.
- **Packaging never ran**, because the whole Release Please run was re-run after the release existed, or Release Please failed after it published the release: run the Package workflow by hand (**Actions > Package > Run workflow**) with the release's tag, such as `v1.2.3`. It builds that tag, stops unless the tag is the version in `.release-please-manifest.json` there and the release exists, and uploads what the release does not have yet. It works for any release whose tagged commit has `scripts/package.ps1`.

### Microsoft Store

The Package workflow also builds `winshell-<version>.msixbundle` (x64 and arm64) from `packaging/msix/AppxManifest.xml` and keeps it as the run's `store-package` artifact. It is not attached to the release: it is unsigned, and the Store signs it. Each run also test-signs the x64 package with a certificate only that runner trusts, installs it there and runs the `winshell` alias.

- The Store build reads and writes the same `%APPDATA%\winshell` config folder as every other build, and its sign-in entry goes in the user's real `Run` key: the manifest turns off file and registry write virtualization (`unvirtualizedResources`). That entry names the package's versioned folder, so it stops working at the first Store update, until #142 moves the Store build to a startup task. The package declares `userNotificationListener`, `wiFiControl`, `bluetooth` and `location`. Start runs `winshellw.exe --no-duplicate`, so a second click starts no second shell; `winshellw.exe` is a copy of `winshell.exe` that opens no console, made when the build is published, and the portable zips have it too. `winshell` in a terminal is the command line.
- The Store takes no 0 as the first part of a package version and installs the highest, so Winshell x.y.z is package version (x+1).y.z.0.
- The repository variables `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER` and `MSIX_PUBLISHER_DISPLAY_NAME` are the identity Partner Center shows under **Product identity**. Without them, the package is `Winshell.Test` by `CN=Winshell Test`, which the Store refuses.
- To submit a release:
  1. Before the first submission, make sure #142 is merged, so **Run Winshell when I sign in** survives Store updates.
  2. Download `store-package` from the release's Package run and upload the `.msixbundle` in Partner Center.
  3. Paste the listing from `packaging/store/listing.md`. Its privacy policy is [docs/privacy.md](privacy.md).

Other release tasks:

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.

## Network permissions

Reading connection profiles needs no setup. Wi-Fi scanning/connecting requires Windows Wi-Fi/location access and an explicit `Networking.requestAccess()` call; packaged deployments also need `wiFiControl`. No network or radio changes are made by the automated tests: actions use a fake backend, and native smoke tests only read state. See [Windows networking](network.md).

## Bluetooth permissions

Reading Bluetooth state uses Windows adapter and device-enumeration properties, without requesting control of radios or opening remote device handles. Packaged deployments need the `bluetooth` capability. Explicit pairing relies on Windows' pairing UI and deployment/policy support; errors are exposed to QML. Automated native validation only reads, while discovery and device actions use a fake backend. See [Windows Bluetooth](bluetooth.md).

## Capture validation

`Winshell.Capture` uses Windows.Graphics.Capture desktop interop with the system border enabled. No additional package or borderless capability is required. Windows policy, protected sources, session state and device availability can restrict capture. See [Windows capture](capture.md) for the API and limits.

The default checks cover capture state, lifecycle, pixels, DPI, PNG export and clipboard encoding with synthetic frames. For native validation on an interactive Windows desktop, run:

```powershell
$env:WINSHELL_TEST_CAPTURE = '1'
dotnet test tests/Winshell.Platform.Windows.Tests --filter FullyQualifiedName~CaptureTests --logger 'console;verbosity=detailed'
Remove-Item Env:WINSHELL_TEST_CAPTURE
```

`WINSHELL_TEST_CAPTURE=1` opts the native capture test in; it is not read by a running shell. That test creates its own panel, captures/resizes it, reads monitor dimensions in memory, and reloads four live capture generations. Its one PNG is of the test panel, written inside a unique test temp folder and deleted in teardown. The test leaves the user's clipboard alone, never uploads pixels and changes no system settings. Without the variable, the native probe reports that it was not requested.

## Run at sign-in

`StartupTests` use a fake, or a key of their own under `HKCU\Software\Winshell\Tests` that they delete. They never write the user's `Run` or `StartupApproved` keys.

## Hotkeys

Tests may register an unused chord such as Ctrl+Alt+Shift+F24 and may install a keyboard hook while a Windows-key hotkey is enabled or a key sequence is in progress. Each test removes the registration and the hook before it ends. Tests do not inject keystrokes or change keyboard settings. See [Windows hotkeys](hotkeys.md).

## Notification surface validation

`NotificationSurfaceTests` loads the actual default QML with synthetic notifications, renders it in memory, checks accessibility and keyboard interaction, and tests simulated 100%, 150% and 200% scale. It creates no native windows, requests no permission, dismisses no real notifications and sends no toasts. `CalendarMonthTests` covers regional formats, first weekdays, week-year boundaries, leap days and month navigation. These run in the default check.

To save the synthetic render evidence outside the repo:

```powershell
$env:WINSHELL_NOTIFICATION_EVIDENCE = Join-Path $env:TEMP 'winshell-notification-evidence'
dotnet test tests/Winshell.Platform.Windows.Tests --filter FullyQualifiedName~NotificationSurfaceTests
Remove-Item Env:WINSHELL_NOTIFICATION_EVIDENCE
```

`WINSHELL_NOTIFICATION_EVIDENCE` is read only by the tests, never by a shell instance. Tests write PNGs to that explicit folder. An optional `BeforeCalendar.qml` there is rendered alongside the new surface. CI extracts that file from the preceding/base commit and attaches the PNGs as the `notification-surface` workflow artifact; no images are committed. A renderer scale is not a change to the PC's display settings. Real mixed-DPI movement, Acrylic composition, Narrator speech, native toast placement, and listener permission with a deployed package still need an interactive Windows session. The unpackaged listener's deployment limitations remain as described in [notifications](notifications.md).

## Settings surface validation

To save the synthetic renders of Winshell Settings outside the repo:

```powershell
$env:WINSHELL_SETTINGS_EVIDENCE = Join-Path $env:TEMP 'winshell-settings-evidence'
dotnet test tests/Winshell.Platform.Windows.Tests --filter FullyQualifiedName~SettingsTests
Remove-Item Env:WINSHELL_SETTINGS_EVIDENCE
```

`WINSHELL_SETTINGS_EVIDENCE` is read only by the tests, never by a shell instance. Tests write PNGs to that folder, and CI attaches them as the `settings-surface` workflow artifact; no images are committed. The tests run each shell with no config folder or one of their own, so they never write the user's `preferences.json`, and they never change a Windows setting. The checks only a live session can do are listed in [Winshell Settings](settings.md#checks-only-a-live-session-can-do).

## Visual snapshots

`SnapshotTests` render every surface of the default shell (the taskbar, Start, quick settings, the notification center, the on-screen display and Winshell Settings) and compare each with a PNG in `tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines`. Each surface is rendered at 100%, 150% and 200% scale, in light and dark, with the Windows default blue accent. They run in the default check. `NoticeSnapshotTests` do the same for Winshell's own notice (`notice-…`), for a configuration error and for a window that failed; `-Surface` cannot pick it, so update it with the script and no `-Surface`.

The whole default shell runs on a fake 1920×1080 screen, against fake services, with its folders in a temporary folder. The surfaces are opened as a user opens them, and each window is painted offscreen the way its composition surface is. A solid color stands in for the Mica or Acrylic that Windows draws behind it. To keep the renders the same on every PC, the time is Thursday 29 October 2026, 09:41, the date and time patterns are en-US, animations are off, and app icons are drawn from shapes. Text is drawn with the Segoe fonts Windows ships and with this PC's text settings, so a PC with font smoothing turned off, or with ClearType Text Tuner settings other than the defaults, renders different text.

A pixel differs when one of its channels moves by more than 2, and a render matches only when no pixel differs. That is as tight as the renders allow: CI renders the baselines pixel for pixel, and a Fluent hairline border moves a channel by only about 15. Loosen it only when a Windows update makes renders differ, and link the CI run that shows it. When one does not match, the test writes `<name>.expected.png`, `<name>.actual.png` and `<name>.diff.png`, with the differing pixels in magenta, to the folder in `WINSHELL_SNAPSHOT_DIFFS`, or to `%TEMP%\winshell-snapshot-diffs`. CI uploads them as the `snapshot-diffs` workflow artifact.

When a change to a surface is intended, update its baselines and commit them with the change:

```powershell
pwsh scripts/update-snapshots.ps1                    # every surface
pwsh scripts/update-snapshots.ps1 -Surface taskbar   # one surface; the others are still compared
```

`-Surface` takes one of `taskbar`, `start`, `quick-settings`, `notification-center`, `osd` and `settings`. The script sets `WINSHELL_UPDATE_SNAPSHOTS`, to `1` or to the surface's name, for that run only. A baseline is replaced only when its render no longer matches. The variable is ignored when `CI` is set, so CI always compares. Look at the changed PNGs before you commit them. Neither variable is read by a shell instance.
