# Development

## Prerequisites

- Git
- PowerShell 7
- .NET 10 SDK
- Node.js, for `npx` when updating agent skills
- The Windows SDK, for `makeappx.exe`, only to run `scripts/package.ps1`
- GitHub CLI (`gh`), only when publishing a draft release outside GitHub Actions

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
| `pwsh scripts/check.ps1` | Builds `winshell.slnx` with warnings as errors, and the docs site with `--warningsAsErrors`. Then runs the main test stage with `scripts/run-tests.ps1`, which leaves out the tests in `tests/quarantine.txt` and reruns a failed test once, as "Flaky tests" in `CONTRIBUTING.md` describes. A test that passes only on its rerun is written to the file `WINSHELL_FLAKY_REPORT` names, when that is set. The reload leak test (`Category=Leak`) runs alone, because it counts handles and USER objects for the whole process. The QML parser corpus (`Category=Corpus`) runs alone after that, in Release on every core, because parsing every prefix of the shipped files would starve the UI tests. The Jint performance budgets (`Category=Performance`) run last and alone, and print what they measured. The media session test (`Category=Media`) isn't run here; the `media` job runs it. When Pester 5.6.1 can be imported, it then runs `Install.Tests.ps1`, `Uninstall.Tests.ps1`, `PublishRelease.Tests.ps1`, `FlakyTests.Tests.ps1` and `ReleaseGate.Tests.ps1`. Otherwise it prints that those five were skipped. `-Quick` builds and runs only the main test stage. The required `ci` job runs that on a pull request, and the full check on a push to `main` and on the release pull request. `scripts/ReleaseGate.ps1` holds a release until the check on its commit passes. `check.ps1` does not run the link check, workflow audit, package build, or the Windows PowerShell 5.1 pass of the installer tests. |
| `pwsh scripts/package.ps1` | Builds what a release ships into `artifacts/`, then checks it: a self-contained, ReadyToRun `winshell-<version>-win-x64.zip` and `-win-arm64.zip`, `SHA256SUMS.txt`, and the winget manifests in `winget/<version>/` and `winget-manifests.zip`. The same commit packs to the same bytes. The check matches the checksums, the architecture of each zip's binaries, the version the x64 `winshell.exe` reports, and the manifests against the validator the pinned `wingetcreate` uses, downloaded from NuGet into `artifacts/tools/`. It also packs `winshell-<version>.msixbundle` for the Microsoft Store and checks each architecture's identity, version and executables; that needs `makeappx.exe` from the Windows SDK. `-Publish`, `-Pack` and `-Verify` run one step. `-Version` overrides the release from `.release-please-manifest.json`, `-Tag v1.2.3` stops unless the checkout is that release, `-Runtime` picks `win-x64`, `win-arm64` or both, `-Out` the folder. `-IdentityName`, `-Publisher` and `-PublisherDisplayName` give the Store package the identity Partner Center reserved; without them it has a test identity. |
| `pwsh scripts/install.ps1` | Installs the portable build for the current user from a GitHub release into `%LOCALAPPDATA%\Programs\winshell\bin`, adds that folder to the user PATH, and creates a Start menu shortcut. `-Version x.y.z` picks a release; `-AutoStart` registers sign-in. Under `irm \| iex` those are `WINSHELL_VERSION` and `WINSHELL_AUTOSTART`. A leftover `bin.old-*` folder is removed on a later install once nothing from that install is running. It also deletes a staged update in `%LOCALAPPDATA%\winshell\updates`, so that copy cannot replace this install on the next start. If a Microsoft Store copy is also installed, it says so and still finishes, because that copy's WindowsApps alias usually comes first on PATH. `irm \| iex`, and running the file with the call operator, also add that folder to the current window's PATH and tell you to run `winshell`. This command starts a process that exits, so it tells you to open a new terminal. The Package workflow uploads this script next to the zip. See the [README](https://github.com/dante-sparras/winshell/blob/main/README.md#install-with-powershell). |
| `pwsh scripts/uninstall.ps1` | Removes that install folder, that PATH entry, that shortcut, that sign-in entry, and a staged update in `%LOCALAPPDATA%\winshell\updates`. It leaves `%APPDATA%\winshell`. Logs and state stay in `%LOCALAPPDATA%\winshell`. Under `irm \| iex` it also removes that folder from the current window's PATH and leaves every other entry. The Package workflow uploads this script next to `install.ps1`. |
| `dotnet run scripts/render-logo.cs` | Renders `packaging/logo.svg` into the package's PNGs (the manifest sizes, their scale-125, scale-150, and scale-200 variants, and the unplated target sizes), the Store listing's PNG, and `packaging/winshell.ico`. Commit them after changing the logo. |
| `pwsh scripts/publish-release.ps1 -Tag v1.2.3 -PackageDirectory <packages> -Repository dante-sparras/winshell` | Normally called by CI: uploads the six required files to an existing draft, verifies GitHub's digests, then publishes it. Requires an authenticated `gh`. Refuses an incomplete published release, never replaces an existing asset, and leaves a failed upload/verification as a draft. Use only for an approved release. |
| `Invoke-Pester -Path scripts/PublishRelease.Tests.ps1 -CI` | Tests draft publication with a mocked `gh`; makes no GitHub changes. Requires Pester 5.6.1 and PowerShell 7. This runs in CI's installer job. |
| `pwsh scripts/update-snapshots.ps1 -FromRun <id>` | Downloads the `snapshot-diffs` artifact from a CI run and imports its actual renders as [snapshot](#visual-snapshots) baselines. Requires `gh` authenticated to GitHub. `-Surface taskbar` imports one surface; `-Surface notice` imports the notice baselines. Local renders are never used. |
| `dotnet tool restore`, then `dotnet docfx docs/docfx.json --serve` | Builds the docs site into `docs/_site/` and serves it at `http://localhost:8080`. See [Docs](#docs). |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before opening a PR, build with warnings as errors and run the focused tests for what you changed. Add `dotnet test .\tests\Winshell.Qml.Tests -c Release --filter Category=Corpus` when you touch the parser, `dotnet test .\winshell.slnx --filter Category=Leak` when you touch reload, and `dotnet test .\tests\Winshell.Runtime.Tests --filter Category=Performance` when you touch Jint. The required `ci` job runs `check.ps1 -Quick` on a PR. The ruleset also requires `installer`, `actionlint`, `pr-title`, and the docs `build` job. Tests of sliding and fading surfaces need Windows "Animation effects" on, which CI turns on for itself.

`check.ps1` runs the PowerShell 7 Pester suites when Pester 5.6.1 is installed. The installer job also runs the Windows PowerShell 5.1 pass and PSScriptAnalyzer, which `check.ps1` does not. Of the jobs below, `installer` and `actionlint` are required:

| Job | When it runs | How to run it locally |
| --- | --- | --- |
| `installer` | Every pull request, in `ci.yml`. That job downloads the Pester 5.6.1 and PSScriptAnalyzer 1.22.0 nupkgs and checks their SHA-256 before it imports them. | `check.ps1` runs the five Pester suites when Pester 5.6.1 imports. This job also runs them, then Windows PowerShell 5.1 runs the install, uninstall and release gate files again. PSScriptAnalyzer 1.22.0 must report no warning or error on `scripts/install.ps1` and `scripts/uninstall.ps1` (the `PSUseShouldProcessForStateChangingFunctions` rule is excluded, because prompting would break `irm \| iex`). |
| `media` | Every push to `main`, in `ci.yml` | The media session test (`Category=Media`), on its own: `dotnet test .\tests\Winshell.Platform.Windows.Tests --filter 'Category=Media'`. Runners often don't publish Windows media sessions, so a failure here shows in the log and blocks nothing. |
| `quarantine` | Every push to `main`, in `ci.yml` | `pwsh scripts/run-tests.ps1 -Quarantined`, which runs the tests in `tests/quarantine.txt`, or nothing when the list is empty. A failure shows in the log and blocks nothing. |
| `flaky-report` | After `ci`, when a test passed only on its rerun | `pwsh scripts/report-flaky.ps1 -ReportPath <file> -RunUrl <url>` files or updates a `flaky` issue, so run it only on a real report. It needs `gh` signed in. |
| `links` | Every pull request, in `docs.yml` | lychee 0.24.2 over `docs/**/*.md`, `README.md`, `CONTRIBUTING.md`, `GLOSSARY.md` and `.github/*.md`, excluding `docs/agents` and `https://about.signpath.io`. |
| `actionlint` | Every pull request and every push to `main` | actionlint on `.github/workflows`. |
| `zizmor` | Every pull request and every push to `main` | zizmor on `.github`. |
| `package` | Pushes to `main`, and pull requests that change packaging or the package workflow | `pwsh scripts/package.ps1`. It publishes both architectures and needs the Windows SDK for the MSIX check. The `arm64` job then runs that zip's `winshell.exe --version` on `windows-11-arm`. |

The docs `build` job is DocFX with warnings as errors, which `check.ps1` also runs and `-Quick` leaves out. `pr-title` checks the pull request title. CodeQL analyzes the languages it finds.

A push to `main` that changes only `docs/**` or Markdown does not run `package`. A release still calls that workflow. The `store-package` artifact is kept for 14 days.

## Run one test

```powershell
dotnet test tests/Winshell.Runtime.Tests --filter FullyQualifiedName~DirectoryWatchTests
```

`--filter` takes a class or a test name. `Winshell.Qml.Tests` and `Winshell.Runtime.Tests` don't open shell windows, so they're safe to run while you work. `Winshell.Platform.Windows.Tests` and `Winshell.Tests` put windows and shells on your desktop.

## Run the shell from source

```powershell
dotnet run --project src/Winshell -- run -p src/Winshell/DefaultShell
```

`-p` takes any folder with a `shell.qml`, or the file. Saving a `.qml` file reloads the shell. The shell keeps `launcher.toml` and `preferences.toml` next to its `shell.qml`, and git ignores those two in `src/Winshell/DefaultShell`. From another terminal:

```powershell
dotnet run --project src/Winshell -- list
dotnet run --project src/Winshell -- log -t 20
dotnet run --project src/Winshell -- kill -i <id>
```

## Environment variables

A running shell reads these. None is needed to build or test.

| Variable | What it does |
| --- | --- |
| `WINSHELL_CONFIG_DIR` | Folder named configs are loaded from, instead of `%APPDATA%\winshell`. This run does not copy an old `~/.config/winshell`. |
| `WINSHELL_LOCAL_DIR` | Folder used instead of `%LOCALAPPDATA%\winshell` for cache, state, data, the default shell, instance logs, crash reports and downloaded updates. A child process inherits it. |
| `WINSHELL_APP_ID` | Sets `Shell.appId` when the configuration has no `//@ pragma AppId`. |
| `WINSHELL_DISABLE_FILE_WATCHER` | Any value stops the shell from reloading when its files change. |
| `WINSHELL_NO_RELOAD_POPUP` | `1` hides Winshell's notice for a configuration that failed to load. A shell can hide it for one failed reload with `Shell.inhibitReloadPopup()` in `Shell.onReloadFailed`. The notice for a window that failed still shows. |
| `WINLAND_PIPE` | The named pipe `Winshell.Winland` connects to, instead of `winland-<session id>` and then `winland`, which it tries in that order when the variable isn't set. |

`WINSHELL_TEST_CRASH` is read only by a Debug build, for `FatalCrashTests`: it names a folder for crash reports, and the shell crashes once its config has loaded, so the test sees the report a real crash writes. A Release build ignores it. Never set it otherwise.

### Installer

`scripts/install.ps1` and `scripts/uninstall.ps1` read these. A normal install sets none of them. `WINSHELL_VERSION` and `WINSHELL_AUTOSTART` are the `irm | iex` forms of `-Version` and `-AutoStart`; they are described with those commands above.

| Variable | What it does |
| --- | --- |
| `WINSHELL_RELEASE_BASE` | Base URL `install.ps1` downloads from, instead of `https://github.com/dante-sparras/winshell/releases`. A trailing slash is ignored. Any other value must be `http://127.0.0.1` or `http://localhost`, with an optional port and path. Anything else stops the install. Installer tests point this at a local server. `uninstall.ps1` does not read it. |
| `WINSHELL_INSTALL_TEST` | `1` marks an installer test. Both scripts then refuse the real `%LOCALAPPDATA%` and `%APPDATA%`, and they require `WINSHELL_USER_PATH_FILE` and `WINSHELL_AUTOSTART_FILE`. Any other value, including unset, is a normal install. |
| `WINSHELL_USER_PATH_FILE` | File used instead of the user `Path` value under `HKCU\Environment`. The scripts read and write that file as UTF-8 without a BOM. A missing file reads as an empty path. Set only from installer tests. |
| `WINSHELL_AUTOSTART_FILE` | File used instead of the `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The scripts read and write that file as UTF-8 without a BOM. An empty or missing file means sign-in is off, and an empty command deletes the file. Set only from installer tests. |

## Where files live

| What | Where |
| --- | --- |
| User configurations | `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` when it is set |
| Cache | `%LOCALAPPDATA%\winshell\cache\by-shell\<shell id>` |
| State | `%LOCALAPPDATA%\winshell\state\by-shell\<shell id>` |
| Per-shell data | `%LOCALAPPDATA%\winshell\data\by-shell\<shell id>` |
| The default shell | `%LOCALAPPDATA%\winshell\default-shell`, written when Winshell runs it and no other instance is running that shell. A `.qml` or `.js` file this build does not ship is removed. |
| Instance logs | `%LOCALAPPDATA%\winshell\run\<id>\log.txt` |
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one `crash-<UTC time>Z-<process>-<where>.txt` each. Nothing is sent anywhere. A window whose native window cannot be made or placed, or throws while drawing or handling input, writes one, closes, and opens again after 1, 2, 4, 8 and 16 seconds; a sixth failure within five minutes leaves it closed until the next reload. A frame the graphics device cannot draw is skipped and reported once per window. Work on the event loop that throws, or a background task that fails, writes one per exception and stack, and a crash that ends the process writes one before the log closes. Reports leave out what scripts printed to the log and take out the passwords, tokens, keys and URL queries Winshell recognizes. The newest 50 reports are kept by their UTC time; only files named and headed as reports are deleted, and nothing is deleted when the folder is a link or junction. |
| Downloaded updates | `%LOCALAPPDATA%\winshell\updates\<version>`, until the next start installs them. `install.ps1` and `uninstall.ps1` delete this folder. See [Winshell updates](updates.md) |
| Winshell Settings choices | `preferences.toml` next to a config's `shell.qml`, or in the user configurations folder for the default shell; see [Winshell Settings](settings.md) |
| Start pins | `launcher.toml` next to the default shell |
| Quick settings tiles | `quick-settings.toml` in the default shell's state folder |
| Run at sign-in | An unpackaged Winshell writes the `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only when a shell sets `Startup.runAtSignIn`. It runs this `winshell.exe` under `conhost.exe --headless`, so no console window opens. A failure before the shell starts shows a dialog. The entry uses the arguments the shell was started with and `--no-duplicate`. A `--path` is made absolute. Environment variables are not copied: `WINSHELL_CONFIG_DIR` applies at sign-in only when it is set for the user. Windows' Startup apps page can turn the entry off; Winshell reads that and never changes it. `winshell.exe` and `winshellw.exe` in the same folder are one install: an entry for either reads as this Winshell. An entry that starts a Winshell in another folder, or other arguments, reads as `Startup.registeredElsewhere`, and setting `runAtSignIn` rewrites it for this one. A packaged Winshell does not write that value. It enables the package's `windows.startupTask` (task id `Winshell`, declared off) which starts `winshellw.exe --no-duplicate`. Windows owns that launch, so a Store update does not leave a path, and `registeredElsewhere` stays false. |

The first launch that uses the default config folder copies `%USERPROFILE%\.config\winshell` into `%APPDATA%\winshell` when the old folder exists. The copy leaves the old folder in place, and it does not replace a file already in the new folder.

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release and creates a **draft**. `draft` and `force-tag-creation` in `release-please-config.json` keep the release unpublished while making its tag available for packaging. The released version is `.release-please-manifest.json`. A build stamps that into the assembly unless `-p:Version=` overrides it, so `winshell --version`, `Shell.hasVersion`, and `//@ if hasVersion(...)` report that release. The printed version can also carry a `+` build id. A build that cannot read the manifest is `0.0.0-dev`.

The release workflow then runs the Package workflow (`.github/workflows/package.yml`) on the tagged commit. Its `release-gate` job waits up to 40 minutes for `ci` on that commit, and nothing is built or published unless `ci` succeeded. It runs `scripts/package.ps1`, then `scripts/publish-release.ps1` attaches both portable zips, `SHA256SUMS.txt`, `winget-manifests.zip`, `install.ps1` and `uninstall.ps1` to the draft. Every file must exist locally, and every remote asset must have the expected SHA-256 before the draft is published. An existing asset is never replaced: matching bytes are skipped and a different digest stops the upload. Signing notes are also added before publication. This order supports [GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases), whose assets are frozen when published. The same workflow builds and checks the packages, without publishing, on a push to `main` that changes more than docs and Markdown, and on PRs that change packaging or the release workflow, and keeps them as the run's `packages` artifact. The Store bundle from that run is kept for 14 days.

With a `WINGET_TOKEN` secret, the release workflow sets `WINGET_CREATE_GITHUB_TOKEN` for a pinned `wingetcreate` and submits the manifests to [winget-pkgs](https://github.com/microsoft/winget-pkgs). That secret is not set. Before it is added, it has to be a token from a dedicated bot account whose only repository is that account's fork of winget-pkgs. A token from the maintainer account is not used: a classic `public_repo` personal access token can write every public repository that account can access, including this one. Without the secret, submit the manifests by hand: unzip `winget-manifests.zip`, set `WINGET_CREATE_GITHUB_TOKEN`, and run `wingetcreate submit <folder>`. winget's moderators review every PR, and the first one for `DanteSparras.Winshell` before any other. That submission is not done. `winget install DanteSparras.Winshell` is not available yet.

A release's own binaries are signed through [SignPath Foundation](https://signpath.org) once its project is set up. That project is not set up, so published zips are unsigned. See the [Code signing policy](code-signing-policy.md). The Package workflow then uploads the published folders as the `unsigned` artifact, asks SignPath to sign them with the `parameters` version the binaries carry, waits up to 110 minutes for an approver, and packs and checks the signed files (`scripts/package.ps1 -Verify -Signed`). It needs:

- the repository variable `SIGNPATH_ORGANIZATION_ID`, which turns signing on. Without it, releases are not signed.
- the secret `SIGNPATH_API_TOKEN`, from a SignPath CI user that may submit to the project.
- `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` when they are not `winshell` and `release-signing`.
- the signing policy's allowed refs to include `refs/heads/main` and `refs/tags/v*`, since a repair runs from the tag.
- the project's artifact configuration, [packaging/signpath/artifact-configuration.xml](https://github.com/dante-sparras/winshell/blob/main/packaging/signpath/artifact-configuration.xml), pasted into SignPath, and GitHub set up as its trusted build system.

PRs and pushes to `main` are never signed. Every binary already carries the product name `Winshell` and the release's version, which `-Verify` checks with or without signing.

When a draft has missing files:

- **`release-gate` stopped the release:** `ci` failed on the tagged commit, or never finished. Re-run `ci` on that commit, and once it succeeds, run Package by hand from the tag, as below.
- **Packaging or the upload failed:** open the Release Please run and choose **Re-run failed jobs**. The release stays a draft until every asset verifies.
- **Packaging never ran**, because the whole Release Please run was re-run after the draft existed: run Package by hand (**Actions > Package > Run workflow**). Under **Use workflow from**, choose **Tags** and the draft's tag, and give the same tag. The tagged commit must contain this draft-publication workflow and its scripts. A signed release must be built from the commit the run is for, so starting its repair from `main` stops. The workflow checks the manifest version, resumes matching draft assets and publishes only after verification.

A **published** release with missing files is not repaired by this workflow. In particular, v0.6.0 was published immutable before its assets were uploaded ([#166](https://github.com/dante-sparras/winshell/issues/166)). Rerunning its workflow cannot add those assets, and disabling repository immutability does not unlock that release. Leave its tag and assets alone. Recovery requires a later approved release, created as a draft, packaged, populated and verified before publication.

### Microsoft Store

The Store package is not published. Publishing waits on Partner Center setup: the identity variables below. Until then it is not an install option.

The Package workflow also builds `winshell-<version>.msixbundle` (x64 and arm64) from `packaging/msix/AppxManifest.xml` and keeps it as the run's `store-package` artifact. It is not attached to the release: it is unsigned, and the Store signs it. Each run also test-signs the x64 package with a certificate only that runner trusts, installs it there and runs the `winshell` alias.

- The Store build reads and writes the same `%APPDATA%\winshell` config folder as every other build. The manifest turns off file and registry write virtualization (`unvirtualizedResources`), so those writes are the real ones. Sign-in is the package's startup task, declared off, not the user's `Run` key: Windows starts `winshellw.exe --no-duplicate`, which keeps working after a Store update. The package declares `userNotificationListener`, `wiFiControl`, `bluetooth`, `location` and `radios`. Start runs `winshellw.exe --no-duplicate`, so a second click starts no second shell; `winshellw.exe` is a copy of `winshell.exe` that opens no console, made when the build is published, and the portable zips have it too. `winshell` in a terminal is the command line.
- The Store takes no 0 as the first part of a package version and installs the highest, so Winshell x.y.z is package version (x+1).y.z.0.
- The repository variables `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER` and `MSIX_PUBLISHER_DISPLAY_NAME` are the identity Partner Center shows under **Product identity**. Without them, the package is `Winshell.Test` by `CN=Winshell Test`, which the Store refuses.
- To submit a release:
  1. Download `store-package` from the release's Package run and upload the `.msixbundle` in Partner Center.
  2. Paste the listing from `packaging/store/listing.md`. Its privacy policy is [docs/privacy.md](privacy.md).

Other release tasks:

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.

## Docs

The docs site is built from `docs/` by [DocFX](https://dotnet.github.io/docfx/), a .NET tool pinned in `dotnet-tools.json`. `docs/agents/` is left out. Each section has a `toc.yml` that lists its pages: `docs/toc.yml` is the top bar, and `guide/toc.yml`, `reference/toc.yml` and `adr/toc.yml` are the sidebars. A page in another folder can be listed with a relative path, such as `../taskbar.md`.

- **A new page** goes into a `toc.yml`. `DocsTests` fails for a page no table of contents lists.
- **Links between pages** are relative `.md` links. DocFX fails the check on a link to a page or a heading that isn't there. Link to files outside `docs/`, such as `CONTRIBUTING.md`, with their GitHub URL.
- **QML examples** in a `qml` code block are checked by `DocsTests`: each one parses, and every module, type, property and signal handler it uses exists. An example without imports is a fragment and can use any module's types. The default shell's files count as types, as they do in a copy of that folder.
- **The reference** in `docs/reference/` has a page per `Winshell` module, `winshell-<module>.md`, with a `## Type` section for each type and a table whose first column names each member: `` `name` `` for a property, `` `name(args)` `` for a method or signal. `DocsTests` fails when a type, a member or an enum value is missing, or when a table names a member the type doesn't have. A type with a registered base type lists only what it adds. `reference/qt-quick.md` names every Qt Quick type, and each row of its "Where Winshell differs from Qt" table is a real `Type.member`; remove a row when that member starts working. Add a new type or member to its page in the PR that adds it.
- **Links to other sites** are checked by the `links` job of the Docs workflow, on each PR and every Monday. That job isn't part of `check.ps1`.

The Docs workflow builds the site on every PR and on `main`. It does not publish that site. GitHub Pages is off.

## Network permissions

Reading connection profiles needs no setup. Wi-Fi scanning/connecting requires Windows Wi-Fi/location access and an explicit `Networking.requestAccess()` call; packaged deployments also need `wiFiControl`. Setting `wifiEnabled` is a separate radio-control request, and a packaged app declares the `radios` device capability. Automated tests use a fake backend and never call `Radio.SetStateAsync` or `Radio.RequestAccessAsync`. Native smoke tests only read state. See [Windows networking](network.md).

## Bluetooth permissions

Reading Bluetooth state uses Windows adapter and device-enumeration properties, plus `BluetoothIsDiscoverable` and `BluetoothIsConnectable`, and does not request control of radios or open remote device handles. Packaged deployments need the `bluetooth` capability. Setting `BluetoothAdapter.enabled` requests radio control, and a packaged app also needs the `radios` capability. Setting `discoverable` or `pairable` calls `BluetoothEnableDiscovery` or `BluetoothEnableIncomingConnections`. Explicit pairing relies on Windows' pairing UI and deployment/policy support; errors are exposed to QML. Automated native validation only reads. Scanning, device actions, radio changes and these visibility changes use a fake backend and never call `Radio.SetStateAsync`, `BluetoothEnableDiscovery` or `BluetoothEnableIncomingConnections`. See [Windows Bluetooth](bluetooth.md).

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

`StartupTests` use a fake, or a key of their own under `HKCU\Software\Winshell\Tests` that they delete. They never write the user's `Run` or `StartupApproved` keys. The startup task is a fake as well: the tests never call `StartupTask.GetAsync`, and they do not install a package.

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

`WINSHELL_NOTIFICATION_EVIDENCE` is read only by the tests, never by a shell instance. Tests write PNGs to that explicit folder. CI attaches those PNGs as the `notification-surface` workflow artifact; no images are committed. A renderer scale is not a change to the PC's display settings. Real mixed-DPI movement, Acrylic composition, Narrator speech, native toast placement, and listener permission with a deployed package still need an interactive Windows session. The unpackaged listener's deployment limitations remain as described in [notifications](notifications.md).

## Settings surface validation

To save the synthetic renders of Winshell Settings outside the repo:

```powershell
$env:WINSHELL_SETTINGS_EVIDENCE = Join-Path $env:TEMP 'winshell-settings-evidence'
dotnet test tests/Winshell.Platform.Windows.Tests --filter FullyQualifiedName~SettingsTests
Remove-Item Env:WINSHELL_SETTINGS_EVIDENCE
```

`WINSHELL_SETTINGS_EVIDENCE` is read only by the tests, never by a shell instance. Tests write PNGs to that folder, and CI attaches them as the `settings-surface` workflow artifact; no images are committed. The tests run each shell with no config folder or one of their own, so they never write the user's `preferences.toml`, and they never change a Windows setting. The checks only a live session can do are listed in [Winshell Settings](settings.md#checks-only-a-live-session-can-do).

## Visual snapshots

`SnapshotTests` render every surface of the default shell (the taskbar, Start, quick settings, the notification center, the on-screen display and Winshell Settings) and compare each with a PNG in `tests/Winshell.Platform.Windows.Tests/Snapshots/Baselines`. Each surface is rendered at 100%, 150% and 200% scale, in light and dark, with the Windows default blue accent. They run in the default check. `NoticeSnapshotTests` do the same for Winshell's own notice (`notice-…`), for a configuration error and for a window that failed. `-Surface notice` replaces those baselines.

The whole default shell runs on a fake 1920×1080 screen, against fake services, with its folders in a temporary folder. The surfaces are opened as a user opens them, and each window is painted offscreen the way its composition surface is. A solid color stands in for the Mica or Acrylic that Windows draws behind it. To keep the renders the same on every PC, the time is Thursday 29 October 2026, 09:41, the date and time patterns are en-US, animations are off, and app icons are drawn from shapes. Text is drawn with the Segoe fonts Windows ships and with this PC's text settings, so a PC with font smoothing turned off, or with ClearType Text Tuner settings other than the defaults, renders different text. Baselines are the renders from the CI runner, `windows-2025`. On 29 October 2026 that was the same image as `windows-latest` (`windows-2025-vs2026`). Change the label only in a pull request that refreshes the baselines. Do not regenerate the baselines on another machine: `update-snapshots.ps1` requires a CI run id and imports that run's pixels. When a PR's snapshots fail, download the `snapshot-diffs` artifact from that run and review those PNGs.

A pixel differs when one of its channels moves by more than 2, and a render matches only when no pixel differs. That is as tight as the renders allow: CI renders the baselines pixel for pixel, and a Fluent hairline border moves a channel by only about 15. Loosen it only when a Windows update makes renders differ, and link the CI run that shows it. When one does not match, the test writes `<name>.expected.png`, `<name>.actual.png` and `<name>.diff.png`, with the differing pixels in magenta, to the folder in `WINSHELL_SNAPSHOT_DIFFS`, or to `%TEMP%\winshell-snapshot-diffs`. CI uploads them as the `snapshot-diffs` workflow artifact.

When a change to a surface is intended, import the actual renders from that failed CI run and commit them with the change. The script requires the run id and never renders on this PC:

```powershell
pwsh scripts/update-snapshots.ps1 -FromRun 123456789                  # every surface in the artifact
pwsh scripts/update-snapshots.ps1 -FromRun 123456789 -Surface taskbar # one surface
pwsh scripts/update-snapshots.ps1 -FromRun 123456789 -Surface notice  # the notice baselines
```

`-Surface` takes one of `taskbar`, `start`, `quick-settings`, `notification-center`, `osd`, `settings` and `notice`. Only the artifact's `<name>.actual.png` files are imported, as `<name>.png`; expected images and diffs are ignored. Review the changed PNGs before committing them. `WINSHELL_UPDATE_SNAPSHOTS` remains a test-only variable and is ignored when `CI` is set. The import script does not set it or render locally.
