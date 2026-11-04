# Development

## Prerequisites

- Git
- PowerShell 7
- Pester 5.6.1, for the PowerShell tooling tests
- .NET 10 SDK
- Node.js, for `npx` when installing or updating agent skills
- The Windows SDK, for `makeappx.exe`, only to run `scripts/package.ps1`
- GitHub CLI (`gh`), only when publishing a draft release outside GitHub Actions

Reading Windows notifications additionally requires listener permission and a package identity declaring `userNotificationListener`; native toast sending requires an installed Windows AppUserModelID. Neither is needed to build or run automated tests. The unpackaged CLI does not provision these identities; the Microsoft Store package declares the listener capability (see [Releasing](#releasing)).

## First checkout

```sh
git clone https://github.com/enpaze/winshell.git
cd winshell
pwsh -NoProfile -Command "Install-Module Pester -RequiredVersion 5.6.1 -Scope CurrentUser -Force -SkipPublisherCheck"
pwsh scripts/check.ps1
```

## Agent skills

From the repo root, restore the external skills recorded in `skills-lock.json`, then install the project-owned test-audit skill for Codex and Claude Code:

```sh
npx -y skills@1.7.4 experimental_install -a codex claude-code -y
npx -y skills@1.7.4 add ./skills/test-audit -a codex claude-code --copy -y
```

Installed copies in `.agents/skills/` and `.claude/skills/` are ignored by Git. Keep `skills-lock.json` tracked for external skills. The custom test-audit source and its license live in `skills/test-audit/`; reinstall it after changing that source. Skills become available in a new agent session.

## Start a task

Fetch before creating a task's worktree. Use its issue number in the branch and folder names:

```powershell
git fetch origin main
if ($LASTEXITCODE -ne 0) { throw 'Could not fetch main.' }
git worktree add -b agent/codex/123-short-description ../winshell-123 origin/main
```

In an existing task worktree, fetch again and run `git rev-list --left-right --count HEAD...origin/main`. It prints commits unique to the task, then commits unique to `main`. A nonzero second count means the task is behind; update its base before editing. An intentionally stacked task starts from its predecessor instead.

T3 Code's Winshell project uses worktrees and automatic pulls, with a **Check task base** script that fetches and reports divergence on creation. It stops on a stale base without resetting files. Other tools should use the commands above.

For agent shell commands, use profile-free PowerShell (`pwsh -NoProfile`, or `login: false` in Codex's command tool). Search with `rg` and read the relevant file section or diff; set a bounded output limit so unrelated output does not hide failures.

## Commands

| Command | What it does |
| --- | --- |
| `pwsh scripts/pr-rules.ps1 -Title <title> -Body <body> -Path <file>` | What the `pr-rules` job runs, given a file of changed paths. It prints each problem and exits 1. |
| `pwsh scripts/pr-screenshot.ps1 <png>` | Publishes a `winshell inspect` screenshot on the `pr-screenshots` branch, which never merges, and prints the Markdown to paste into a PR. |
| `pwsh scripts/check.ps1 [-Tier Fast\|Slow\|All] [-Seed <seed>] [-NoBuild]` | Builds with warnings as errors, verifies formatting and runs fast .NET tests plus every PowerShell 7 suite in `tests/Scripts/`. Fast is the default and opens no windows. `-Tier Slow` runs only the tests that open windows, without formatting or Pester; `All` runs both tiers and Pester. `-Seed` replays the randomized .NET test order. `-NoBuild` uses an existing build. Missing Pester 5.6.1 fails with an installation command; it is never silently skipped or downloaded. The packed shell tests (`Category=Package`) are separate. |
| `pwsh -NoProfile scripts/check.ps1 -Lint` | Runs the existing offline Markdown link/fragment check, actionlint and offline zizmor, stopping on the first failure. Run immediately after Markdown or workflow edits. Needs the [pinned lint tools](#local-lint-setup) on PATH; does not build, run tests, require Pester, or download tools. |
| `pwsh scripts/pack.ps1` | Packs the `Winshell` library a configuration references, and the `Winshell.Templates` package, into `artifacts/packages/`. The library carries `Winshell.Services` and `Winshell.Shell`, and depends on `Microsoft.WindowsAppSDK.WinUI` and `Microsoft.WindowsAppSDK.Foundation` at the versions in `Directory.Packages.props`; the templates reference only `Winshell` and get WindowsAppSDK through it. The template package is `dotnet new winshell` (one panel) and `dotnet new winshell-default` (the default shell, generated from `src/Winshell.DefaultShell` and `templates/winshell-default/Program.cs` when this runs). Neither package is published. The Package workflow runs the `Category=Package` tests, which call this, on PRs that change the templates. `-Out` chooses the folder and `-Version` overrides the release from `.release-please-manifest.json`. |
| `pwsh scripts/package.ps1` | Builds what a release ships into `artifacts/`, then checks it: a self-contained, trimmed, ReadyToRun `winshell-<version>-win-x64.zip` and `-win-arm64.zip`, `SHA256SUMS.txt`, and the winget manifests in `winget/<version>/` and `winget-manifests.zip`. The same commit packs to the same bytes. The check matches the checksums, the architecture of each zip's binaries, the version the x64 `winshell.exe` reports, starts the default shell from that zip and opens Winshell Settings, so with Winland running use the [live UI procedure](#live-ui-with-winland-running), and matches the manifests against the validator the pinned `wingetcreate` uses, downloaded from NuGet into `artifacts/tools/`. It also packs `winshell-<version>.msixbundle` for the Microsoft Store and checks each architecture's identity, version and executables; that needs `makeappx.exe` from the Windows SDK. `-Publish`, `-Pack` and `-Verify` run one step. `-Version` overrides the release from `.release-please-manifest.json`, `-Tag v1.2.3` stops unless the checkout is that release, `-Runtime` picks `win-x64`, `win-arm64` or both, `-Out` the folder. `-IdentityName`, `-Publisher` and `-PublisherDisplayName` give the Store package the identity Partner Center reserved; without them it has a test identity. |
| `pwsh scripts/install.ps1` | Installs the portable build for the current user from a GitHub release into `%LOCALAPPDATA%\Programs\winshell\bin`, adds that folder to the user PATH, and creates a Start menu shortcut. `-Version x.y.z` picks a release; `-AutoStart` registers sign-in. Under `irm \| iex` those are `WINSHELL_VERSION` and `WINSHELL_AUTOSTART`. A leftover `bin.old` folder is removed on a later install once nothing from that install is running. It also deletes a staged update in `%LOCALAPPDATA%\winshell\updates`, so that copy cannot replace this install on the next start. If a Microsoft Store copy is also installed, it says so and still finishes, because that copy's WindowsApps alias usually comes first on PATH. `irm \| iex`, and running the file with the call operator, also add that folder to the current window's PATH and tell you to run `winshell`. This command starts a process that exits, so it tells you to open a new terminal. The Package workflow uploads this script next to the zip. See the [README](https://github.com/enpaze/winshell/blob/main/README.md#install-with-powershell). |
| `pwsh scripts/uninstall.ps1` | Removes that install folder, that PATH entry, that shortcut, that sign-in entry, and a staged update in `%LOCALAPPDATA%\winshell\updates`. It leaves `%APPDATA%\winshell`. Logs and state stay in `%LOCALAPPDATA%\winshell`. Under `irm \| iex` it also removes that folder from the current window's PATH and leaves every other entry. The Package workflow uploads this script next to `install.ps1`. |
| `dotnet run scripts/render-logo.cs` | Renders `packaging/logo.svg` into the package's PNGs (the manifest sizes, their scale-125, scale-150, and scale-200 variants, and the unplated target sizes), the Store listing's PNG, and `packaging/winshell.ico`. Commit them after changing the logo. |
| `dotnet run scripts/generate-reference.cs` | Builds `Winshell.Shell` and `Winshell.Services`, then writes `docs/reference/winshell*.md` from their public types and `///` comments. Each page keeps what is above its `<!-- generated below -->` line. Commit the pages after changing a public member; `DocsTests` fails when a page is not what this writes. |
| `pwsh scripts/publish-release.ps1 -Tag v1.2.3 -PackageDirectory <packages> -Repository enpaze/winshell` | Normally called by CI: uploads the six required files to an existing draft, verifies GitHub's digests, then publishes it. Requires an authenticated `gh`. Refuses an incomplete published release, never replaces an existing asset, and leaves a failed upload/verification as a draft. Use only for an approved release. |
| `Invoke-Pester -Path tests/Scripts/PublishRelease.Tests.ps1 -CI` | Tests draft publication with a mocked `gh`; makes no GitHub changes. Requires Pester 5.6.1 and PowerShell 7. This runs in CI's main check. |
| `lychee --offline --include-fragments '**/*.md' '.github/**/*.md'` | Checks every link between the repo's Markdown files, and the headings they point at, as the `ci` job does. It needs [lychee](https://github.com/lycheeverse/lychee) 0.24.2 on PATH, and reads no network. See [Docs](#docs). |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before every push, run `pwsh scripts/check.ps1`, then the focused checks for what you changed. `dotnet test .\tests\Winshell.Package.Tests --filter Category=Package` packs the library and templates, builds each shell and runs it; it is slow and separate. The Package workflow runs it on PRs that change `templates/`, `src/Winshell.Package/`, `Directory.Packages.props` or the release files; it is not required. The required `ci` job checks local Markdown links and runs the default check. The ruleset also requires `slow`, `installer`, `actionlint`, `pr-title` and `pr-rules`. Tests of sliding and fading surfaces need Windows "Animation effects" on, which CI enables.

`check.ps1` requires Pester 5.6.1 and runs the PowerShell 7 suites. CI installs hash-verified modules through `.github/actions/powershell`; the installer job additionally runs the combined installer/uninstaller tests on Windows PowerShell 5.1. Workflow and link checks remain separate:

| Job | When it runs | How to run it locally |
| --- | --- | --- |
| `installer` | PRs that change `scripts/`, `tests/Scripts/` or workflows/actions, and every push to `main`. | Windows PowerShell 5.1 runs `tests/Scripts/Install.Tests.ps1` with Pester 5.6.1 against temporary profiles and releases served on loopback. PowerShell 7 tooling suites run once in `ci`. |
| `slow` | Every pull request that changes more than docs or Markdown and is not a draft, and every push to `main`, in `ci.yml` | `pwsh scripts/check.ps1 -Tier Slow`, on an empty Winland workspace. |
| `trim-caches` | Every push to `main`, after `ci`, in `ci.yml` | Nothing to run. It deletes all but the two newest CodeQL databases of each language from the Actions cache, which would otherwise pass the 10 GB GitHub keeps. |
| `links` | Pushes to `main` that change docs or Markdown, and every Monday, in `links.yml` | lychee 0.24.2 over `docs/**/*.md`, `README.md`, `CONTRIBUTING.md`, `GLOSSARY.md` and `.github/*.md`, excluding `docs/agents` and `https://about.signpath.io`. |
| `actionlint` | Pull requests that change a workflow, and every push to `main`, in `ci.yml` | actionlint on `.github/workflows`, then zizmor on `.github`. |
| `package` | Pushes to `main`, and pull requests that change packaging or the package workflow | `pwsh scripts/package.ps1`. It publishes both architectures and needs the Windows SDK for the MSIX check. The workflow's `publish` job builds each architecture on its own runner, `windows-2025` and `windows-11-arm`, and `package` packs both. The `arm64` job then runs that zip's `winshell.exe --version` on `windows-11-arm`. |

Every check of a pull request is in `ci.yml`, except `pr-title`, which also looks at the branch name, and `pr-rules`, which runs from the base branch so that a pull request can't change its own rules. The `changes` job decides which of them a pull request needs, and a skipped job counts as a passed required check. A draft skips `ci` and `slow`, and marking it ready runs them. Otherwise, every pull request runs `ci`, which checks the links of every Markdown file, and a change to only docs or Markdown skips `slow`. Every push to `main` runs everything. A newer push to a pull request cancels its older run. When `ci` can't build, the `stop` job cancels the rest of the run, so `slow` doesn't hold a runner to prove nothing. The `ci` job runs the fast tests. The `slow` job, also required, runs `Category=Slow`: the tests that start `winshell.exe` or open a real window. A test class that does either carries `[Trait("Category", "Slow")]`.

Before it builds, `ci` runs lychee offline over the repo's Markdown, so a link to a file or a heading that isn't there fails it. `pr-title` checks the pull request title. `pr-rules` checks that each type of change carries its proof, as "PR description" in `CONTRIBUTING.md` lists. CodeQL analyzes the languages it finds.

`main` merges through a merge queue: `gh pr merge <number> --squash` adds a pull request to it, and the queue runs the required checks on the commit that will land. `ci.yml`, `pr-title.yml` and `pr-rules.yml` run on `merge_group` for that.

The short `pr-title` and `pr-rules` workflows finish every run, including successive PR edits. They have no concurrency cancellation: a cancelled required check can hold the merge queue even after a newer passing run. The main CI workflow still cancels superseded PR runs and stops work after a failed build. To recover an already cancelled metadata check, open its Actions run and choose **Re-run all jobs**, or run `gh run rerun <run-id>`. Auto-merge waits for the queue; only **Merged** confirms the change is on `main`.

A push to `main` that changes only `docs/**` or Markdown does not run `package`. A release still calls that workflow. The `store-package` artifact is kept for 14 days.

### Local lint setup

Install these versions, matching `.github/workflows/ci.yml`, and put their executables on PATH:

- [lychee 0.24.2](https://github.com/lycheeverse/lychee/releases/tag/lychee-v0.24.2): extract the Windows archive and add the folder containing `lychee.exe`.
- [actionlint 1.7.12](https://github.com/rhysd/actionlint/releases/tag/v1.7.12): extract the Windows archive and add the folder containing `actionlint.exe`.
- [zizmor 1.31.1](https://github.com/zizmorcore/zizmor/releases/tag/v1.31.1): `uv tool install zizmor==1.31.1`, then `uv tool update-shell` if its executable folder is not on PATH. This setup command needs [uv](https://docs.astral.sh/uv/getting-started/installation/).

Run `pwsh -NoProfile scripts/check.ps1 -Lint` from the repo root. T3 Code also exposes it as **Check docs and workflows**. Routine linting uses installed tools and reads no network; dependency installation is explicit.

## Run one test

```powershell
dotnet test tests/Winshell.Services.Tests --filter FullyQualifiedName~DirectoryWatchTests
```

`--filter` takes a class or a test name. `Winshell.Services.Tests` and `Winshell.DefaultShell.Tests` don't open shell windows, so they're safe to run while you work. `Winshell.Shell.Tests` and `Winshell.Tests` put windows and shells on your desktop.

## Live UI with Winland running

Winland tiles every normal window, so the windows a live run or a test opens change size and land among the user's windows. When `winland` is running, do that work on an empty workspace on a monitor the user is not using, and never swap the focused monitor's workspace:

```powershell
$monitors = winland -j monitors | ConvertFrom-Json
$focused = $monitors | Where-Object focused | Select-Object -First 1
$spare = $monitors | Where-Object { -not $_.focused } | Select-Object -First 1
if (-not $spare) { throw 'One monitor: ask before swapping the workspace the user is on.' }
$workspace = $spare.activeWorkspace.id
winland keyword windowrule 'ignore, exe:(winshell|winshellw|Winshell\..*Tests)\.exe'
winland --batch "dispatch focusmonitor $($spare.id);dispatch workspace empty;dispatch focusmonitor $($focused.id)"
```

- One run at a time on a machine. The workspace and the rule are shared, so a parallel run would note another run's empty workspace as the user's, and `winland reload` would drop its rule.
- The rule reaches windows that open after it, and a workspace dispatch acts on the focused monitor, which is why the batch moves the focus and gives it back. Test projects are their own programs, not `testhost`. Without the rule `A_floating_window_has_the_size_and_title_it_was_given` fails.
- The workspace does not contain what the rule ignores: an ignored window opens where Windows puts it, usually on the primary monitor, and a taskbar opens on every screen. Keep what a test opens small and short.
- `winland -j clients` must list no Winshell window, and the user's windows must be where they were when you finish.
- Open a real window there. Winland must tile it, it must open above the shell's windows, and no shell window may draw over it. Check again after `winland dispatch fullscreen`, where the taskbar goes behind it.
- Finish in a `finally`, so the spare monitor goes back and the rule goes:

```powershell
winland --batch "dispatch focusmonitor $($spare.id);dispatch workspace $workspace;dispatch focusmonitor $($focused.id)"
winland reload
```

## See what you changed

`winshell inspect` is how to verify a visual change. It reads the running shell from inside, so it needs no mouse and no keyboard, and it does not move the user's pointer or focus.

```powershell
winshell inspect tree                                # every open window's elements, sizes and values, as JSON
winshell inspect screenshot out.png TaskbarView      # a window as a PNG, which you can then read
winshell inspect click StartButton TaskbarView       # activate an element in a window through its automation peer
```

Look at the screenshot, and check values in the tree rather than trusting that the code ran. Open what a test or run needs under "Live UI with Winland running". For a shell of your own, run it with private `WINSHELL_CONFIG_DIR` and `WINSHELL_LOCAL_DIR`, and pass `--pid`.

`winshell inspect hover` shows a control's hover look only: a `PointerEntered` handler needs a real pointer, or a method of its own that a test can call.

## Run the shell from source

```powershell
dotnet run --project src/Winshell -- run
```

That runs the default shell in this process. `-p` is a configuration project: a folder with a `.csproj`, or the project file. `--no-build` starts the last build and does not compile. From another terminal:

```powershell
dotnet run --project src/Winshell -- list
dotnet run --project src/Winshell -- log -t 20
dotnet run --project src/Winshell -- kill -i <id>
```

## Environment variables

A running shell reads these. None is needed to build or test.

| Variable | What it does |
| --- | --- |
| `WINSHELL_CONFIG_DIR` | Folder named configs are loaded from, instead of `%APPDATA%\winshell`. |
| `WINSHELL_LOCAL_DIR` | Folder used instead of `%LOCALAPPDATA%\winshell` for cache, state, data, the default shell, instance logs, crash reports and downloaded updates. A child process inherits it. |

`WINSHELL_TEST_CRASH` is read only by a Debug build, for the crash tests: it names a folder for crash reports, and the shell crashes once its config has loaded, so the test sees the report a real crash writes. A Release build ignores it. Never set it otherwise.

### Installer

`scripts/install.ps1` and `scripts/uninstall.ps1` read these. A normal install sets none of them. `WINSHELL_VERSION` and `WINSHELL_AUTOSTART` are the `irm | iex` forms of `-Version` and `-AutoStart`; they are described with those commands above.

| Variable | What it does |
| --- | --- |
| `WINSHELL_RELEASE_BASE` | Base URL `install.ps1` downloads from, instead of `https://github.com/enpaze/winshell/releases`. A trailing slash is ignored. Any other value must be `http://127.0.0.1` or `http://localhost`, with an optional port and path. Anything else stops the install. Installer tests point this at a local server. `uninstall.ps1` does not read it. |
| `WINSHELL_INSTALL_TEST` | `1` marks an installer test. Both scripts then refuse the real `%LOCALAPPDATA%` and `%APPDATA%`, and they require `WINSHELL_USER_PATH_FILE` and `WINSHELL_AUTOSTART_FILE`. `install.ps1` then does not ask Windows for a Microsoft Store copy. Any other value, including unset, is a normal install. |
| `WINSHELL_USER_PATH_FILE` | File used instead of the user `Path` value under `HKCU\Environment`. The scripts read and write that file as UTF-8 without a BOM. A missing file reads as an empty path. While it is set, the scripts leave the session's PATH alone. Set only from installer tests. |
| `WINSHELL_AUTOSTART_FILE` | File used instead of the `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. `install.ps1 -AutoStart` writes the sign-in command there as UTF-8 without a BOM, and `uninstall.ps1` deletes the file when that command starts this install. A missing file means sign-in is off. Set only from installer tests. |

## Where files live

| What | Where |
| --- | --- |
| User configurations | `%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR` when it is set |
| Cache | `%LOCALAPPDATA%\winshell\cache\by-shell\<shell id>` |
| State | `%LOCALAPPDATA%\winshell\state\by-shell\<shell id>` |
| Per-shell data | `%LOCALAPPDATA%\winshell\data\by-shell\<shell id>` |
| Instance logs | `%LOCALAPPDATA%\winshell\run\<id>\log.txt` |
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one `crash-<UTC time>Z-<process>-<where>.txt` each. Nothing is sent anywhere. A window whose native window cannot be made or placed, or throws while drawing or handling input, writes one, closes, and opens again after 1, 2, 4, 8 and 16 seconds; a sixth failure within five minutes leaves it closed until the next reload. A frame the graphics device cannot draw is skipped and reported once per window. Work on the event loop that throws, or a background task that fails, writes one per exception and stack, and a crash that ends the process writes one before the log closes. Reports leave out what scripts printed to the log and take out the passwords, tokens, keys and URL queries Winshell recognizes. The newest 50 reports are kept by their UTC time; only files named and headed as reports are deleted, and nothing is deleted when the folder is a link or junction. |
| Downloaded updates | `%LOCALAPPDATA%\winshell\updates\<version>`, until the next start installs them. `install.ps1` and `uninstall.ps1` delete this folder. See [Winshell updates](reference/winshell-updates.md) |
| Winshell Settings choices | `preferences.toml` in the user configurations folder; see [the default shell](reference/default-shell.md#winshell-settings) |
| Start pins | `launcher.toml` in the user configurations folder |
| Quick settings tiles | `quick-settings.toml` in the default shell's state folder |
| Run at sign-in | An unpackaged Winshell writes the `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only when a shell sets `Startup.runAtSignIn`. It runs this `winshell.exe` under `conhost.exe --headless`, so no console window opens. A failure before the shell starts shows a dialog. The entry uses the arguments the shell was started with and `--no-duplicate`. A `--path` is made absolute. Environment variables are not copied: `WINSHELL_CONFIG_DIR` applies at sign-in only when it is set for the user. Windows' Startup apps page can turn the entry off; Winshell reads that and never changes it. `winshell.exe` and `winshellw.exe` in the same folder are one install: an entry for either reads as this Winshell. An entry that starts a Winshell in another folder, or other arguments, reads as `Startup.registeredElsewhere`, and setting `runAtSignIn` rewrites it for this one. A packaged Winshell does not write that value. It enables the package's `windows.startupTask` (task id `Winshell`, declared off) which starts `winshellw.exe --no-duplicate`. Windows owns that launch, so a Store update does not leave a path, and `registeredElsewhere` stays false. |

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release and creates a **draft**. `draft` and `force-tag-creation` in `release-please-config.json` keep the release unpublished while making its tag available for packaging. The released version is `.release-please-manifest.json`. A build stamps that into the assembly unless `-p:Version=` overrides it, so `winshell --version`, `Shell.hasVersion`, and `//@ if hasVersion(...)` report that release. The printed version can also carry a `+` build id. A build that cannot read the manifest is `0.0.0-dev`.

The release workflow then runs the Package workflow (`.github/workflows/package.yml`) on the tagged commit. It runs `scripts/package.ps1`, then `scripts/publish-release.ps1` attaches both portable zips, `SHA256SUMS.txt`, `winget-manifests.zip`, `install.ps1` and `uninstall.ps1` to the draft. Every file must exist locally, and every remote asset must have the expected SHA-256 before the draft is published. An existing asset is never replaced: matching bytes are skipped and a different digest stops the upload. Signing notes are also added before publication. This order supports [GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases), whose assets are frozen when published. The same workflow builds and checks the packages, without publishing, on a push to `main` that changes more than docs and Markdown, and on PRs that change packaging or the release workflow, and keeps them as the run's `packages` artifact. The Store bundle from that run is kept for 14 days.

With a `WINGET_TOKEN` secret, the release workflow sets `WINGET_CREATE_GITHUB_TOKEN` for a pinned `wingetcreate` and submits the manifests to [winget-pkgs](https://github.com/microsoft/winget-pkgs). That secret is not set. Before it is added, it has to be a token from a dedicated bot account whose only repository is that account's fork of winget-pkgs. A token from the maintainer account is not used: a classic `public_repo` personal access token can write every public repository that account can access, including this one. Without the secret, submit the manifests by hand: unzip `winget-manifests.zip`, set `WINGET_CREATE_GITHUB_TOKEN`, and run `wingetcreate submit <folder>`. winget's moderators review every PR, and the first one for `DanteSparras.Winshell` before any other. That submission is not done. `winget install DanteSparras.Winshell` is not available yet.

A release's own binaries are signed through [SignPath Foundation](https://signpath.org) once its project is set up. That project is not set up, so published zips are unsigned.  The Package workflow then uploads the published folders as the `unsigned` artifact, asks SignPath to sign them with the `parameters` version the binaries carry, waits up to 110 minutes for an approver, and packs and checks the signed files (`scripts/package.ps1 -Verify -Signed`). It needs:

- the repository variable `SIGNPATH_ORGANIZATION_ID`, which turns signing on. Without it, releases are not signed.
- the secret `SIGNPATH_API_TOKEN`, from a SignPath CI user that may submit to the project.
- `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` when they are not `winshell` and `release-signing`.
- the signing policy's allowed refs to include `refs/heads/main` and `refs/tags/v*`, since a repair runs from the tag.
- the project's artifact configuration, [packaging/signpath/artifact-configuration.xml](https://github.com/enpaze/winshell/blob/main/packaging/signpath/artifact-configuration.xml), pasted into SignPath, and GitHub set up as its trusted build system.

PRs and pushes to `main` are never signed. Every binary already carries the product name `Winshell` and the release's version, which `-Verify` checks with or without signing.

When a draft has missing files:

- **Packaging or the upload failed:** open the Release Please run and choose **Re-run failed jobs**. The release stays a draft until every asset verifies.
- **Packaging never ran**, because the whole Release Please run was re-run after the draft existed: run Package by hand (**Actions > Package > Run workflow**). Under **Use workflow from**, choose **Tags** and the draft's tag, and give the same tag. The tagged commit must contain this draft-publication workflow and its scripts. A signed release must be built from the commit the run is for, so starting its repair from `main` stops. The workflow checks the manifest version, resumes matching draft assets and publishes only after verification.

A **published** release with missing files is not repaired by this workflow. In particular, v0.6.0 was published immutable before its assets were uploaded ([#166](https://github.com/enpaze/winshell/issues/166)). Rerunning its workflow cannot add those assets, and disabling repository immutability does not unlock that release. Leave its tag and assets alone. Recovery requires a later approved release, created as a draft, packaged, populated and verified before publication.

### Microsoft Store

The Store package is not published. Publishing waits on Partner Center setup: the identity variables below. Until then it is not an install option.

The Package workflow also builds `winshell-<version>.msixbundle` (x64 and arm64) from `packaging/msix/AppxManifest.xml` and keeps it as the run's `store-package` artifact. It is not attached to the release: it is unsigned, and the Store signs it. Each run also test-signs the x64 package with a certificate only that runner trusts, installs it there and runs the `winshell` alias.

- The Store build reads and writes the same `%APPDATA%\winshell` config folder as every other build. The manifest turns off file and registry write virtualization (`unvirtualizedResources`), so those writes are the real ones. Sign-in is the package's startup task, declared off, not the user's `Run` key: Windows starts `winshellw.exe --no-duplicate`, which keeps working after a Store update. The package declares `userNotificationListener`, `wiFiControl`, `bluetooth`, `location` and `radios`. Start runs `winshellw.exe --no-duplicate`, so a second click starts no second shell; `winshellw.exe` is a copy of `winshell.exe` that opens no console, made when the build is published, and the portable zips have it too. `winshell` in a terminal is the command line.
- The Store takes no 0 as the first part of a package version and installs the highest, so Winshell x.y.z is package version (x+1).y.z.0.
- The repository variables `MSIX_IDENTITY_NAME`, `MSIX_PUBLISHER` and `MSIX_PUBLISHER_DISPLAY_NAME` are the identity Partner Center shows under **Product identity**. Without them, the package is `Winshell.Test` by `CN=Winshell Test`, which the Store refuses.
- To submit a release:
  1. Download `store-package` from the release's Package run and upload the `.msixbundle` in Partner Center.
  2. Paste the listing from `packaging/store/listing.md`. A public privacy policy URL still needs to be configured.

Other release tasks:

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.

## Docs

The docs are plain Markdown, read on GitHub or in an editor. `docs/agents/` is for agents working on this repo.

- **A new reference page** goes into the list in `docs/reference/index.md`.
- **Links between pages** are relative `.md` links, also to files outside `docs/`, such as `../CONTRIBUTING.md`. The `ci` job runs lychee offline over every Markdown file, with `--include-fragments`, and fails on a link to a file or a heading that isn't there.
- **C# examples** in a `csharp` code block are checked by `DocsTests`: each one is text from `tests/Winshell.Shell.Tests/GuideSamples.cs`, which the test project builds.
- **The reference** in `docs/reference/` has a page per namespace of the `Winshell` package (the public types of `Winshell.Shell` and `Winshell.Services`), `winshell.md` for `Winshell` and `winshell-<module>.md` for the others, starting with `# <namespace>` and an intro written by hand. `dotnet run scripts/generate-reference.cs` writes the rest, a `## Type` section for each public type with its members, from the `///` comments, and `DocsTests` fails when a page is not what it writes.
- **Links to other sites** are checked by `links.yml`, on `main` and every Monday. That job isn't part of `check.ps1`.

## Network permissions

Reading connection profiles needs no setup. Wi-Fi scanning/connecting requires Windows Wi-Fi/location access and an explicit `Networking.requestAccess()` call; packaged deployments also need `wiFiControl`. Setting `wifiEnabled` is a separate radio-control request, and a packaged app declares the `radios` device capability. Automated tests use a fake backend and never call `Radio.SetStateAsync` or `Radio.RequestAccessAsync`. Native smoke tests only read state. See Windows networking.

## Bluetooth permissions

Reading Bluetooth state uses Windows adapter and device-enumeration properties, plus `BluetoothIsDiscoverable` and `BluetoothIsConnectable`, and does not request control of radios or open remote device handles. Packaged deployments need the `bluetooth` capability. Setting `BluetoothAdapter.enabled` requests radio control, and a packaged app also needs the `radios` capability. Setting `discoverable` or `pairable` calls `BluetoothEnableDiscovery` or `BluetoothEnableIncomingConnections`. Explicit pairing relies on Windows' pairing UI and deployment/policy support; errors are reported on the service. Automated native validation only reads. Scanning, device actions, radio changes and these visibility changes use a fake backend and never call `Radio.SetStateAsync`, `BluetoothEnableDiscovery` or `BluetoothEnableIncomingConnections`. See [Bluetooth](reference/winshell-bluetooth.md).

## Capture validation

`Winshell.Capture` uses Windows.Graphics.Capture desktop interop with the system border enabled. No additional package or borderless capability is required. Windows policy, protected sources, session state and device availability can restrict capture. See [capture](reference/winshell-capture.md) for the API and limits.

## Run at sign-in

`StartupTests` use a fake, or a key of their own under `HKCU\Software\Winshell\Tests` that they delete. They never write the user's `Run` or `StartupApproved` keys. The startup task is a fake as well: the tests never call `StartupTask.GetAsync`, and they do not install a package.

## Hotkeys

Tests may register an unused chord such as Ctrl+Alt+Shift+F24 and may install a keyboard hook while a Windows-key hotkey is enabled or a key sequence is in progress. Each test removes the registration and the hook before it ends. Tests do not inject keystrokes or change keyboard settings. See [hotkeys](reference/winshell-input.md).

## Checks only a live session can do

Real mixed-DPI movement, Acrylic composition, Narrator speech, native toast placement, and listener permission with a deployed package still need an interactive Windows session.

## Project structure

```text
.
├── .github/                         # CI, issue and PR templates, Dependabot
├── docs/
│   ├── agents/                      # How the engineering skills use this repo
│   ├── reference/                   # A page per module
│   ├── *.md                         # The configuration guide, troubleshooting, and a page per topic
│   └── development.md               # Setup, environment, and every command
├── packaging/
│   ├── logo.svg                     # The app logo; scripts/render-logo.cs renders its PNGs and winshell.ico
│   ├── msix/                        # The Microsoft Store package's manifest and assets
│   ├── signpath/                    # The artifact configuration SignPath signs releases with
│   └── store/                       # The Store listing text and its app tile
├── scripts/                         # Check, test, pack, package, install and release scripts; docs/development.md lists them
├── templates/                       # dotnet new winshell and winshell-default; the default shell is generated at pack time
├── src/
│   ├── BannedSymbols.txt            # Clock calls that don't build; time comes through a TimeProvider
│   ├── Winshell/                    # winshell.exe: the CLI, and the process that runs the default shell
│   ├── Winshell.Services/           # Windows services, instances, and the control pipe
│   ├── Winshell.Shell/              # Shell.Run and WinUI windows
│   ├── Winshell.DefaultShell/       # The default shell, in XAML and C#
│   └── Winshell.Package/            # Packs Winshell.Services and Winshell.Shell as the Winshell package
├── tests/
│   ├── Winshell.DefaultShell.Tests/ # The default shell's logic, with no window
│   ├── Winshell.Services.Tests/     # The services, with no window
│   ├── Winshell.Shell.Tests/        # Boots a real shell and opens real windows
│   ├── Winshell.Tests/              # Runs winshell.exe instances side by side
│   ├── Winshell.Package.Tests/      # Packs the templates and runs each shell; Category=Package, left out of the main run
│   ├── Scripts/                     # PowerShell tooling tests and their fixtures
│   ├── TestSupport/                 # Source files every test project links instead of copying
│   └── Directory.Build.props        # What every test project shares
├── winshell.slnx                    # The solution the check builds
├── skills/                          # Project-owned skill sources; installed copies are ignored
├── CONTRIBUTING.md                  # Test rules, branch names, PR titles and descriptions
└── GLOSSARY.md                      # The words this repo uses
```
