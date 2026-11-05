# Development

The [architecture rules](architecture.md) define the target assembly boundaries, feature ownership and migration order.

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

The first build of `winshell.slnx` sets `core.hooksPath` to `.githooks`, unless the clone already sets a hooks path. The `pre-commit` hook then runs `dotnet format` over the staged C# files, so a commit fails on the formatting, code style and analyzer errors that would fail CI. It reads the files as they are in the working tree, and takes about 20 seconds. `git commit --no-verify` skips it, and `dotnet format winshell.slnx` fixes most of what it finds.

## Build from source

You need Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
git clone https://github.com/enpaze/winshell.git
cd winshell
dotnet publish src\Winshell -c Release -o "$env:LOCALAPPDATA\winshell-dev"
```

That puts `winshell.exe` in `%LOCALAPPDATA%\winshell-dev`, away from the folder the PowerShell installer uses. Add that folder to your `PATH` to run `winshell` from anywhere. Like a release, the build carries its own .NET runtime. Unlike one, it does not update itself.

To update a source build, close Winshell, pull the newer source and publish it over the same folder. To uninstall one, turn off **Run Winshell when I sign in** in Winshell Settings if you turned it on, close Winshell, and delete its folder. Your configuration is in `%APPDATA%\winshell`, and logs, state and crash reports are in `%LOCALAPPDATA%\winshell`; delete them too if you don't want to keep them.

## Agent skills

Install every skill `skills-lock.json` records, for Codex and Claude Code. Agents do this first in every session:

```sh
pwsh scripts/install-skills.ps1
```

It installs each source at the ref the lock pins, as copies. The CLI's `experimental_install` links instead, and on Windows that leaves the external skills linked to no agent.

Installed copies in `.agents/skills/` and `.claude/skills/` are ignored by Git; keep `skills-lock.json` tracked. Every skill installs from the source repository the lock records, `test-audit` included, which comes from OpenClaw. Skills become available in a new agent session.

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

### Pull request stacks

Prefer small, focused PR stacks for related work. Each layer still has its own issue, branch, worktree and PR. Create a dependent layer's worktree from its predecessor's branch, then adopt the existing branches in bottom-to-top order with `gh stack init`.

Install the extension once with `gh extension install github/gh-stack`.

| Command | When to use it |
| --- | --- |
| `gh stack init <bottom-branch> [<next-branch> ...]` | Start a stack or adopt existing branches, ordered from the branch targeting `main` to the top layer. |
| `gh stack add <next-branch>` | Create the next branch on top of the stack. Return the current worktree to its previous branch, then use `git worktree add <folder> <next-branch>` for the new layer before editing. |
| `gh stack view` | Inspect the layers and their PR status. |
| `gh stack submit` | Fill in each PR's title and description in the interactive editor, then push and publish the stack. Follow the [PR template](../CONTRIBUTING.md#pr-description). Run the required checks in each affected worktree before this command, since it pushes. |
| `gh stack rebase` | Fetch the latest trunk and rebase each layer onto its updated parent. Resolve conflicts by intent, as [AGENTS.md](../AGENTS.md#conflicts-and-merging) describes. |
| `gh stack push` | Push updated layers together with explicit leases. Run the [checks for the change](#checks-by-change) in each affected worktree after rebasing and before pushing. |
| `gh stack merge <stack-number> --squash` | Merge the ready stack in one operation, with one squash commit per PR. Every layer must meet its required checks and reviews. This avoids intermediate rebases and repeated CI runs between individual merges. |

For automation without the interactive editor, create each PR with `gh pr create --base <parent-branch> --head <branch> --title <title> --body-file <file>`, using `main` for the bottom layer. Then link the PRs in bottom-to-top order with `gh stack link <bottom-pr> <next-pr> ...`. To extend an existing stack, use `gh stack link <stack-number> <new-pr>`.

## Checks by change

Choose checks from the complete change against the PR's base, including renamed and deleted files. Run them again after rebasing.

| Change | Before pushing |
| --- | --- |
| Only Markdown or files under `docs/`, without compiled examples or generated reference changes | `pwsh -NoProfile scripts/check.ps1 -Lint`. No application build, formatting or test suite is needed. |
| A page in `docs/` (outside `docs/agents/`) or `README.md` containing or removing a C# example fence, or a generated `docs/reference/winshell*.md` page | Lint, then `dotnet test tests/Winshell.Shell.Tests -warnaserror --nologo --filter FullyQualifiedName~Winshell.ShellTests.DocsTests`. This builds the samples and checks the generated reference, without unrelated tests or windows. |
| Other changes, including source, scripts, workflows and build configuration | `pwsh scripts/check.ps1`, followed by the focused checks listed below for that change. |

Pages that still contain C# fences get the targeted documentation checks even when the edit changes only their prose. Deleting the last fence or a whole example page also needs those checks. The existing documentation tests own this contract; no application tests are needed just for wording changes.

CI uses `scripts/ci-changes.ps1` to make the same selection for PRs and pushes to `main`. A missing patch for a page covered by `DocsTests` conservatively selects documentation tests; an empty or potentially truncated file list selects the full suite. A rename includes its previous path, so moving a source file into `docs/` still selects full checks.

## Commands

| Command | What it does |
| --- | --- |
| `pwsh scripts/pr-rules.ps1 -Title <title> -Body <body> -Path <file>` | What the `pr-rules` job runs, given a file of changed paths. It prints each problem and exits 1. |
| `pwsh scripts/pr-screenshot.ps1 <png>` | Publishes a `winshell inspect` screenshot on the `pr-screenshots` branch, which never merges, and prints the Markdown to paste into a PR. |
| `pwsh scripts/check.ps1 [-Tier Fast\|Slow\|All] [-Seed <seed>] [-NoBuild]` | Builds with warnings as errors, verifies formatting and runs fast .NET tests plus every PowerShell 7 suite in `tests/Scripts/`. Fast is the default and opens no windows. `-Tier Slow` runs only the tests that open windows, without formatting or Pester; `All` runs both tiers and Pester. `-Seed` replays the randomized .NET test order. `-NoBuild` uses an existing build. Missing Pester 5.6.1 fails with an installation command; it is never silently skipped or downloaded. The packed shell tests (`Category=Package`) are separate. |
| `pwsh -NoProfile scripts/check.ps1 -Lint` | Runs the existing offline Markdown link/fragment check, actionlint and offline zizmor, stopping on the first failure. Run immediately after Markdown or workflow edits. Needs the [pinned lint tools](#local-lint-setup) on PATH; does not build, run tests, require Pester, or download tools. |
| `pwsh scripts/ci-changes.ps1 -Files <json> [-MaxFiles <limit>] [-Draft]` | Classifies GitHub changed-file JSON from the checkout root and prints the flags used by `ci.yml`. PR file lists have a 3000-file limit; push comparisons have a 300-file limit. CI fetches this input; local contributors normally choose the checks above directly. |
| `pwsh scripts/pack.ps1` | Packs the `Winshell` library a configuration references, and the `Winshell.Templates` package, into `artifacts/packages/`. The library carries `Winshell.Native`, `Winshell.Core`, `Winshell.Services` and `Winshell.Shell`, and depends on `Microsoft.WindowsAppSDK.WinUI` and `Microsoft.WindowsAppSDK.Foundation` at the versions in `Directory.Packages.props`; the templates reference only `Winshell` and get WindowsAppSDK through it. CsWin32 generates Native's declarations at build time and is not a package consumer dependency. The template package is `dotnet new winshell` (one panel) and `dotnet new winshell-default` (the default shell, generated from `src/Winshell.DefaultShell` and `templates/winshell-default/Program.cs` when this runs). Neither package is published. The Package workflow runs the `Category=Package` tests, which call this, on PRs that change the templates. `-Out` chooses the folder and `-Version` overrides the release from `.release-please-manifest.json`. |
| `pwsh scripts/package.ps1` | Builds what a release ships into `artifacts/`, then checks it: a self-contained, trimmed, ReadyToRun `winshell-<version>-win-x64.zip` and `-win-arm64.zip`, `SHA256SUMS.txt`, and the winget manifests in `winget/<version>/` and `winget-manifests.zip`. The same commit packs to the same bytes. The check matches the checksums, the architecture of each zip's binaries, the version the x64 `winshell.exe` reports, starts the default shell from that zip and opens Winshell Settings, so with Winland running use the [live UI procedure](#live-ui-with-winland-running), and matches the manifests against the validator the pinned `wingetcreate` uses, downloaded from NuGet into `artifacts/tools/`. It also packs `winshell-<version>.msixbundle` for the Microsoft Store and checks each architecture's identity, version and executables; that needs `makeappx.exe` from the Windows SDK. `-Publish`, `-Pack` and `-Verify` run one step. `-Version` overrides the release from `.release-please-manifest.json`, `-Tag v1.2.3` stops unless the checkout is that release, `-Runtime` picks `win-x64`, `win-arm64` or both, `-Out` the folder. `-IdentityName`, `-Publisher` and `-PublisherDisplayName` give the Store package the identity Partner Center reserved; without them it has a test identity. |
| `pwsh scripts/install.ps1` | Installs the portable build for the current user from a GitHub release into `%LOCALAPPDATA%\Programs\winshell\bin`, adds that folder to the user PATH, and creates a Start menu shortcut. `-Version x.y.z` picks a release; `-AutoStart` registers sign-in. Under `irm \| iex` those are `WINSHELL_VERSION` and `WINSHELL_AUTOSTART`. A leftover `bin.old` folder is removed on a later install once nothing from that install is running. It also deletes a staged update in `%LOCALAPPDATA%\winshell\updates`, so that copy cannot replace this install on the next start. If a Microsoft Store copy is also installed, it says so and still finishes, because that copy's WindowsApps alias usually comes first on PATH. `irm \| iex`, and running the file with the call operator, also add that folder to the current window's PATH and tell you to run `winshell`. This command starts a process that exits, so it tells you to open a new terminal. The Package workflow uploads this script next to the zip. See the [README](https://github.com/enpaze/winshell/blob/main/README.md#install-with-powershell). |
| `pwsh scripts/uninstall.ps1` | Removes that install folder, that PATH entry, that shortcut, that sign-in entry, and a staged update in `%LOCALAPPDATA%\winshell\updates`. It leaves `%APPDATA%\winshell`. Logs and state stay in `%LOCALAPPDATA%\winshell`. Under `irm \| iex` it also removes that folder from the current window's PATH and leaves every other entry. The Package workflow uploads this script next to `install.ps1`. |
| `dotnet run scripts/render-logo.cs` | Renders `packaging/logo.svg` into the package's PNGs (the manifest sizes, their scale-125, scale-150, and scale-200 variants, and the unplated target sizes), the Store listing's PNG, and `packaging/winshell.ico`. Commit them after changing the logo. |
| `dotnet run scripts/generate-reference.cs` | Builds `Winshell.Shell`, `Winshell.Services` and `Winshell.Core`, then writes `docs/reference/winshell*.md` from their public types and `///` comments. Each page keeps what is above its `<!-- generated below -->` line. Commit the pages after changing a public member; `DocsTests` fails when a page is not what this writes. |
| `pwsh scripts/publish-release.ps1 -Tag v1.2.3 -PackageDirectory <packages> -Repository enpaze/winshell` | Normally called by CI: uploads the six required files to an existing draft, verifies GitHub's digests, then publishes it. Requires an authenticated `gh`. Refuses an incomplete published release, never replaces an existing asset, and leaves a failed upload/verification as a draft. Use only for an approved release. |
| `Invoke-Pester -Path tests/Scripts/Package.Tests.ps1` | Tests that `package.ps1` writes a zip's entries in ordinal order under a culture that sorts differently, dated the release day. It reads `New-Zip` from the script's syntax tree and builds nothing. Requires Pester 5.6.1 and PowerShell 7. This runs in CI's main check. |
| `Invoke-Pester -Path tests/Scripts/PublishRelease.Tests.ps1` | Tests draft publication with a mocked `gh`; makes no GitHub changes. Requires Pester 5.6.1 and PowerShell 7. This runs in CI's main check. |
| `lychee --offline --include-fragments '**/*.md' '.github/**/*.md'` | Checks every link between the repo's Markdown files, and the headings they point at, as the `ci` job does. It needs [lychee](https://github.com/lycheeverse/lychee) 0.24.2 on PATH, and reads no network. See [Docs](#docs). |
| `pwsh scripts/install-skills.ps1` | Installs every skill in `skills-lock.json` for Codex and Claude Code, each at its pinned ref, as copies. See [Agent skills](#agent-skills). |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before every push, run the [checks for the change](#checks-by-change), then its focused checks. `dotnet test .\tests\Winshell.Package.Tests --filter Category=Package` packs the library and templates, builds each shell and runs it; it is slow and separate. The Package workflow runs it on PRs that change `templates/`, `src/Winshell.Package/`, `Directory.Packages.props` or the release files, excluding Markdown-only changes; it is not required. The required `ci` job always checks local Markdown links and selects the relevant tests. The ruleset also requires `slow`, `installer`, `actionlint`, `pr-title` and `pr-rules`. Tests of sliding and fading surfaces need Windows "Animation effects" on, which CI enables.

`check.ps1` requires Pester 5.6.1 and runs the PowerShell 7 suites. CI installs hash-verified modules through `.github/actions/powershell`; the installer job additionally runs the combined installer/uninstaller tests on Windows PowerShell 5.1. Workflow and link checks remain separate:

| Job | When it runs | How to run it locally |
| --- | --- | --- |
| `installer` | PRs and pushes to `main` that change `scripts/`, `tests/Scripts/` or workflows/actions. | Windows PowerShell 5.1 runs `tests/Scripts/Install.Tests.ps1` with Pester 5.6.1 against temporary profiles and releases served on loopback. PowerShell 7 tooling suites run once in `ci`. |
| `slow` | Non-draft PRs and pushes to `main` that change more than docs or Markdown, in `ci.yml` | `pwsh scripts/check.ps1 -Tier Slow`, on an empty Winland workspace. |
| `pr-size` | Every pull request, from `pr-size.yml` on the base branch. Not required. | Nothing to run. It labels the pull request `size:XS` to `size:XL`, as "PR description" in `CONTRIBUTING.md` says. |
| `trim-caches` | Every push to `main`, after `ci`, in `ci.yml` | Nothing to run. It deletes all but the two newest CodeQL databases of each language from the Actions cache, which would otherwise pass the 10 GB GitHub keeps. |
| `links` | Pushes to `main` that change docs or Markdown, and every Monday, in `links.yml` | lychee 0.24.2 over `docs/**/*.md`, `README.md`, `CONTRIBUTING.md`, `GLOSSARY.md` and `.github/*.md`, excluding `docs/agents`. |
| `actionlint` | PRs and pushes to `main` that change workflows or actions, in `ci.yml` | actionlint on `.github/workflows`, then zizmor on `.github`. |
| `package` | Non-documentation pushes to `main`, and PRs that change packaging or the package workflow, excluding Markdown-only changes | `pwsh scripts/package.ps1`. It publishes both architectures and needs the Windows SDK for the MSIX check. The workflow's `publish` job builds each architecture on its own runner, `windows-2025` and `windows-11-arm`, and `package` packs both. The `arm64` job then runs that zip's `winshell.exe --version` on `windows-11-arm`. |

The `changes` job selects checks for PRs and pushes to `main`; a skipped job counts as a passed required check. A draft skips `ci` and `slow`, and marking it ready runs them. Otherwise, `ci` always checks the links of every Markdown file. Prose-only changes stop there; compiled documentation changes run only `DocsTests`; other changes run the build, formatting, fast .NET tests and PowerShell suites. Documentation-only changes skip `slow`. A newer push to a PR cancels its older run. When `ci` can't build, the `stop` job cancels the rest of the run, so `slow` doesn't hold a runner to prove nothing. The required `slow` job runs `Category=Slow`: tests that start `winshell.exe` or open a real window. A test class that does either carries `[Trait("Category", "Slow")]`.

`ci` runs lychee offline over the repo's Markdown, so a link to a file or a heading that isn't there fails the required check even when no build is needed. `pr-title` checks the pull request title. `pr-rules` checks that each type of change carries its proof, as "PR description" in `CONTRIBUTING.md` lists. CodeQL uses GitHub's separately managed default setup; its security-analysis triggers are independent of this workflow's file selection.

`main` takes squash merges of pull requests. `gh pr merge <number> --squash --auto` merges one once its required checks pass. A pull request need not be up to date with `main`, so its checks ran on its branch and not on what lands; the push to `main` selects checks again for the merged change.

The short `pr-title` and `pr-rules` workflows finish every run, including successive PR edits. They have no concurrency cancellation: a cancelled required check can block a pull request even after a newer passing run. The main CI workflow still cancels superseded PR runs and stops work after a failed build. To recover an already cancelled metadata check, open its Actions run and choose **Re-run all jobs**, or run `gh run rerun <run-id>`. Only **Merged** confirms the change is on `main`.

A push to `main` that changes only `docs/**` or Markdown does not run `package`. A release still calls that workflow. The `store-package` artifact is kept for 14 days.

### Local lint setup

Install these versions, matching `.github/workflows/ci.yml`, and put their executables on PATH:

- [lychee 0.24.2](https://github.com/lycheeverse/lychee/releases/tag/lychee-v0.24.2): extract the Windows archive and add the folder containing `lychee.exe`.
- [actionlint 1.7.12](https://github.com/rhysd/actionlint/releases/tag/v1.7.12): extract the Windows archive and add the folder containing `actionlint.exe`.
- [zizmor 1.31.1](https://github.com/zizmorcore/zizmor/releases/tag/v1.31.1): `uv tool install zizmor==1.31.1`, then `uv tool update-shell` if its executable folder is not on PATH. This setup command needs [uv](https://docs.astral.sh/uv/getting-started/installation/).

Run `pwsh -NoProfile scripts/check.ps1 -Lint` from the repo root. T3 Code also exposes it as **Check docs and workflows**. Routine linting uses installed tools and reads no network; dependency installation is explicit.

## Run one test

```powershell
dotnet test tests/Winshell.Core.Tests --filter FullyQualifiedName~DirectoryWatchTests
```

`--filter` takes a class or a test name. `Winshell.Core.Tests`, `Winshell.Services.Tests` and `Winshell.DefaultShell.Tests` don't open shell windows, so they're safe to run while you work. `Winshell.Shell.Tests` and `Winshell.Tests` put windows and shells on your desktop.

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

Look at the screenshot, and check values in the tree rather than trusting that the code ran. The tree marks the element keyboard focus is on with `"focused": true`. A name finds only elements that are not collapsed, as Narrator does, so a control kept collapsed beside a visible twin does not make the name ambiguous. Open what a test or run needs under "Live UI with Winland running". For a shell of your own, run it with private `WINSHELL_CONFIG_DIR` and `WINSHELL_LOCAL_DIR`, and pass `--pid`.

`winshell inspect hover` shows a control's hover look only: a `PointerEntered` handler needs a real pointer, or a method of its own that a test can call.

## Run the shell from source

```powershell
dotnet run --project src/Winshell -- run
```

That runs the default shell in a child process and supervises it. `-p` is a configuration project: a folder with a `.csproj`, or the project file. `--no-build` starts the last build and does not compile. From another terminal:

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

`winshell run` sets `WINSHELL_SUPERVISED`, and `WINSHELL_UPDATE_FAILURE` when an update did not install, on the process that runs the default shell for it. That process clears both. Never set them.

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
| Crash reports | `%LOCALAPPDATA%\winshell\crashes`, one `crash-<UTC time>Z-<process>-<where>.txt` each. Nothing is sent anywhere. A window whose native window cannot be made or placed, or throws while drawing or handling input, writes one, closes, and opens again after 1, 2, 4, 8 and 16 seconds; a sixth failure within five minutes leaves it closed until the next reload. A frame the graphics device cannot draw is skipped and reported once per window. Work on the event loop that throws, or a background task that fails, writes one per exception and stack, and a crash that ends the process writes one before the log closes. A report holds the last 40 log lines. It takes out the passwords, tokens, keys and URL queries Winshell recognizes. The newest 50 reports are kept by their UTC time; only files named and headed as reports are deleted, and nothing is deleted when the folder is a link or junction. |
| Downloaded updates | `%LOCALAPPDATA%\winshell\updates\<version>`, until the next start installs them. `install.ps1` and `uninstall.ps1` delete this folder. See [Winshell updates](reference/winshell-updates.md) |
| Winshell Settings choices | `preferences.toml` in the user configurations folder; see [the default shell](reference/default-shell.md#winshell-settings) |
| Start pins | `launcher.toml` in the user configurations folder |
| Quick settings tiles | `quick-settings.toml` in the default shell's state folder |
| Run at sign-in | An unpackaged Winshell writes the `Winshell` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only when a shell sets `Startup.runAtSignIn`. It runs this `winshell.exe` under `conhost.exe --headless`, so no console window opens. A failure before the shell starts shows a dialog. The entry uses the arguments the shell was started with and `--no-duplicate`. A `--path` is made absolute. Environment variables are not copied: `WINSHELL_CONFIG_DIR` applies at sign-in only when it is set for the user. Windows' Startup apps page can turn the entry off; Winshell reads that and never changes it. `winshell.exe` and `winshellw.exe` in the same folder are one install: an entry for either reads as this Winshell. An entry that starts a Winshell in another folder, or other arguments, reads as `Startup.registeredElsewhere`, and setting `runAtSignIn` rewrites it for this one. A packaged Winshell does not write that value. It enables the package's `windows.startupTask` (task id `Winshell`, declared off) which starts `winshellw.exe --no-duplicate`. Windows owns that launch, so a Store update does not leave a path, and `registeredElsewhere` stays false. |

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release and creates a **draft**. `draft` and `force-tag-creation` in `release-please-config.json` keep the release unpublished while making its tag available for packaging. The released version is `.release-please-manifest.json`. A build stamps that into the assembly unless `-p:Version=` overrides it, so `winshell --version`, `Shell.hasVersion`, and `//@ if hasVersion(...)` report that release. The printed version can also carry a `+` build id. A build that cannot read the manifest is `0.0.0-dev`.

The release workflow then runs the Package workflow (`.github/workflows/package.yml`) on the tagged commit. It runs `scripts/package.ps1`, then `scripts/publish-release.ps1` attaches both portable zips, `SHA256SUMS.txt`, `winget-manifests.zip`, `install.ps1` and `uninstall.ps1` to the draft. Every file must exist locally, and every remote asset must have the expected SHA-256 before the draft is published. An existing asset is never replaced: matching bytes are skipped and a different digest stops the upload. Signing notes are also added before publication. This order supports [GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases), whose assets are frozen when published. The same workflow builds and checks the packages, without publishing, on a push to `main` that changes more than docs and Markdown, and on PRs that change packaging or the release workflow, and keeps them as the run's `packages` artifact. The Store bundle from that run is kept for 14 days.

With a `WINGET_TOKEN` secret, the release workflow sets `WINGET_CREATE_GITHUB_TOKEN` for a pinned `wingetcreate` and submits the manifests to [winget-pkgs](https://github.com/microsoft/winget-pkgs). That secret is not set. Before it is added, it has to be a token from a dedicated bot account whose only repository is that account's fork of winget-pkgs. A token from the maintainer account is not used: a classic `public_repo` personal access token can write every public repository that account can access, including this one. Without the secret, submit the manifests by hand: unzip `winget-manifests.zip`, set `WINGET_CREATE_GITHUB_TOKEN`, and run `wingetcreate submit <folder>`. winget's moderators review every PR, and the first one for `DanteSparras.Winshell` before any other. That submission is not done. `winget install DanteSparras.Winshell` is not available yet.

A release's own binaries are signed through [SignPath Foundation](https://signpath.org) once its project is set up. That project is not set up, so published zips are unsigned. The Package workflow then uploads the published folders as the `unsigned` artifact, asks SignPath to sign them with the `parameters` version the binaries carry, waits up to 110 minutes for an approver, and packs and checks the signed files (`scripts/package.ps1 -Verify -Signed`). It needs:

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
  2. Paste the listing from `packaging/store/listing.md`.

Other release tasks:

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.

## Docs

The docs are plain Markdown, read on GitHub or in an editor. `docs/agents/` is for agents working on this repo.

- **A new reference page** goes into the list in `docs/reference/index.md`.
- **Links between pages** are relative `.md` links, also to files outside `docs/`, such as `../CONTRIBUTING.md`. The `ci` job runs lychee offline over every Markdown file, with `--include-fragments`, and fails on a link to a file or a heading that isn't there.
- **C# examples** in a `csharp` code block are checked by `DocsTests`: each one is text from `tests/Winshell.Shell.Tests/GuideSamples.cs`, which the test project builds.
- **The reference** in `docs/reference/` has a page per namespace of the `Winshell` package (the public types of `Winshell.Shell`, `Winshell.Services` and `Winshell.Core`), `winshell.md` for `Winshell` and `winshell-<module>.md` for the others, starting with `# <namespace>` and an intro written by hand. `dotnet run scripts/generate-reference.cs` writes the rest, a `## Type` section for each public type with its members, from the `///` comments, and `DocsTests` fails when a page is not what it writes.
- **Links to other sites** are checked by `links.yml`, on `main` and every Monday. That job isn't part of `check.ps1`.

## Network permissions

Reading connection profiles needs no setup. Wi-Fi scanning/connecting requires Windows Wi-Fi/location access and an explicit `NetworkingService.RequestAccess()` call; packaged deployments also need `wiFiControl`. Setting `WifiEnabled` is a separate radio-control request, and a packaged app declares the `radios` device capability. Automated tests use a fake backend and never call `Radio.SetStateAsync` or `Radio.RequestAccessAsync`. Native smoke tests only read state. See [networking](reference/winshell-network.md).

## Bluetooth permissions

Reading Bluetooth state uses Windows adapter and device-enumeration properties, plus `BluetoothIsDiscoverable` and `BluetoothIsConnectable`, and does not request control of radios or open remote device handles. Packaged deployments need the `bluetooth` capability. Setting `BluetoothAdapter.Enabled` requests radio control, and a packaged app also needs the `radios` capability. Setting `Discoverable` or `Pairable` calls `BluetoothEnableDiscovery` or `BluetoothEnableIncomingConnections`. Explicit pairing relies on Windows' pairing UI and deployment/policy support; errors are reported on the service. Automated native validation only reads. Scanning, device actions, radio changes and these visibility changes use a fake backend and never call `Radio.SetStateAsync`, `BluetoothEnableDiscovery` or `BluetoothEnableIncomingConnections`. See [Bluetooth](reference/winshell-bluetooth.md).

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
├── .githooks/                       # The pre-commit hook; building the solution points Git at it
├── .github/                         # CI, issue and PR templates, Dependabot
├── docs/
│   ├── agents/                      # How the engineering skills use this repo
│   ├── reference/                   # A page per module
│   ├── *.md                         # The configuration guide and troubleshooting
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
│   ├── Winshell.Native/             # Shared Win32/COM declarations, process/hook support and session locking; migration in progress
│   ├── Winshell.Core/               # The event loop, the log, ObservableObject, TOML files, crash reports, and the file helpers they use
│   ├── Winshell.Services/           # Windows services, instances, and the control pipe
│   ├── Winshell.Shell/              # Shell.Run and WinUI windows
│   ├── Winshell.DefaultShell/       # The default shell, in XAML and C#
│   └── Winshell.Package/            # Packs Native, Core, Services and Shell as the single Winshell package
├── tests/
│   ├── Winshell.Core.Tests/         # The shared infrastructure: the event loop, the log, TOML files, crash reports and file helpers
│   ├── Winshell.DefaultShell.Tests/ # The default shell's logic, with no window
│   ├── Winshell.Services.Tests/     # The services, with no window
│   ├── Winshell.Shell.Tests/        # Boots a real shell and opens real windows
│   ├── Winshell.Tests/              # Runs winshell.exe instances side by side
│   ├── Winshell.Package.Tests/      # Packs the templates and runs each shell; Category=Package, left out of the main run
│   ├── Scripts/                     # PowerShell tooling tests and their fixtures
│   ├── TestSupport/                 # Source files every test project links instead of copying
│   └── Directory.Build.props        # What every test project shares
├── winshell.slnx                    # The solution the check builds
├── Directory.Build.targets          # Seals interop into Winshell.Native: bans interop attributes elsewhere, and unsafe code outside WinUI projects
├── BannedSymbols.Interop.txt        # The interop APIs no project but Winshell.Native may use
├── Directory.Solution.targets       # Points Git at .githooks when the solution builds
├── CONTRIBUTING.md                  # Test rules, branch names, PR titles and descriptions
└── GLOSSARY.md                      # The words this repo uses
```
