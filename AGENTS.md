# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Stack

- Windows 11. The host and the platform target `net10.0-windows10.0.22621.0`. The parser and the runtime target `net10.0`.
- .NET 10 SDK, pinned in `global.json`. `Directory.Build.props` treats warnings as errors.
- C# for the shell. `Winshell.Qml` parses QML. JavaScript runs on Jint. Items are drawn with Skia. Windows are DirectComposition surfaces with DWM backdrops.
- xUnit v3 for `tests/`, through VSTest.
- PowerShell 7 for `scripts/dev.ps1`, `scripts/check.ps1`, `scripts/package.ps1`, `scripts/publish-release.ps1`, `scripts/update-snapshots.ps1`, and the file-based apps `scripts/fetch-reference.cs` and `scripts/render-logo.cs`. `scripts/install.ps1` and `scripts/uninstall.ps1` also run on Windows PowerShell 5.1. `scripts/PublishRelease.Tests.ps1` tests the publisher with a mocked `gh`. `scripts/run-tests.ps1` and `scripts/report-flaky.ps1` run on PowerShell 7, and `scripts/FlakyTests.Tests.ps1` tests them with mocks.

## Project Structure

```text
.
├── .github/                         # CI, issue and PR templates, Dependabot
├── docs/
│   ├── adr/                         # Decisions already made
│   ├── agents/                      # How the engineering skills use this repo
│   │   └── quickshell/              # Quickshell v0.3.2 checkout and manual, where the API started; gitignored, made by scripts/dev.ps1
│   ├── guide/                       # The docs site's guide section
│   ├── reference/                   # A page per module, checked against the code by DocsTests
│   ├── release-notes/               # Notes for a release, once that release exists
│   ├── security/                    # The threat model
│   ├── development.md               # Setup, environment, and every command
│   ├── docfx.json                   # Builds docs/ into the docs site
│   ├── index.md                     # The docs site's home page
│   └── migrating-from-quickshell.md # Quickshell names and their Winshell names
├── packaging/
│   ├── logo.svg                     # The app logo; scripts/render-logo.cs renders its PNGs and winshell.ico
│   ├── msix/                        # The Microsoft Store package's manifest and assets
│   ├── signpath/                    # The artifact configuration SignPath signs releases with
│   └── store/                       # The Store listing text and its app tile
├── scripts/
│   ├── check.ps1                    # Build with warnings as errors, build the docs, then test; -Quick is a PR's part of it
│   ├── dev.ps1                      # Prepares a fresh clone
│   ├── fetch-reference.cs           # Fetches the Quickshell manual as markdown
│   ├── install.ps1                  # Per-user install from a GitHub release; published on the release
│   ├── Install.Tests.ps1            # Pester tests for the installer, including a local fake release
│   ├── Install.TestHelpers.ps1      # Temp profile and fake-release helpers for those tests
│   ├── package.ps1                  # Builds the portable zips, winget manifests and Store package a release ships
│   ├── publish-release.ps1          # Uploads the six release assets to a draft and publishes it after the hashes match
│   ├── PublishRelease.Tests.ps1     # Pester tests for that publisher, with a mocked gh
│   ├── ReleaseGate.ps1              # Decides whether a release may publish from the ci result on its commit
│   ├── ReleaseGate.Tests.ps1        # Pester tests for that gate
│   ├── uninstall.ps1                # Removes the per-user install; published on the release
│   ├── Uninstall.Tests.ps1          # Pester tests for the uninstaller
│   ├── render-logo.cs               # Renders packaging/logo.svg into the package's PNGs and winshell.ico
│   ├── report-flaky.ps1             # Files or updates a flaky issue for each test in a report
│   ├── run-tests.ps1                # The main test stage: quarantine list, then one rerun of failed tests
│   ├── FlakyTests.Tests.ps1         # Pester tests for those two scripts
│   └── update-snapshots.ps1         # Replaces the visual snapshot baselines on purpose
├── src/
│   ├── Winshell/                    # The winshell CLI and the process that runs a shell
│   ├── Winshell.Qml/                # The QML parser
│   ├── Winshell.Runtime/            # The QML runtime, the item tree, and the Winshell modules
│   └── Winshell.Platform.Windows/   # Windows 11 windows, composition, and services
├── tests/
│   ├── Winshell.Qml.Tests/
│   ├── Winshell.Runtime.Tests/
│   ├── Winshell.Platform.Windows.Tests/
│   ├── Winshell.Tests/              # Runs winshell.exe instances side by side
│   ├── TestSupport/                 # Source files every test project links instead of copying
│   ├── Directory.Build.props        # What every test project shares
│   └── quarantine.txt               # Flaky tests ci leaves out and the quarantine job runs
├── winshell.slnx                    # The solution the check builds
├── dotnet-tools.json                # DocFX, pinned
├── .agents/skills/                  # Agent skills; a copy in .claude/skills/
├── CONTRIBUTING.md                  # Branches, commit and PR titles, PR rules
└── GLOSSARY.md                      # The words this repo uses
```

## Before you finish

- Run `pwsh scripts/check.ps1`, the full check, and it must pass with 0 warnings. The required `ci` job runs only its `-Quick` part on a PR, and all of it on a push to `main` and on the release PR. When Pester 5.6.1 is installed it also runs `Install.Tests.ps1`, `Uninstall.Tests.ps1`, `PublishRelease.Tests.ps1`, `FlakyTests.Tests.ps1` and `ReleaseGate.Tests.ps1`, and otherwise prints that they were skipped. It does not run PSScriptAnalyzer, lychee, actionlint, zizmor or `package.ps1`. The ruleset also requires the `installer` job, `pr-title`, and the docs `build` job.
- If you touched `scripts/*.ps1` or `.github/`, also run the jobs [docs/development.md](docs/development.md#commands) lists for that change.
- Name the branch, title the PR, and fill in the PR template as [CONTRIBUTING.md](CONTRIBUTING.md#open-a-pr) describes.
- Say what you couldn't verify.
- Added or changed a command, prerequisite, environment variable, or top-level folder? Update [docs/development.md](docs/development.md) or Project Structure.
- Don't edit `.agents/skills/` or `.claude/skills/`; the `skills` CLI overwrites them.
- Never commit `.env` files or credentials.

## Agent skills

### Issue tracker

Issues live in GitHub Issues for dante-sparras/winshell, used through `gh`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Notes

I like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. Try to honor the dev's intent in both a minimal and realistic fashion.
