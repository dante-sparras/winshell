# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Stack

- Windows 11. The host and the platform target `net10.0-windows10.0.22621.0`. The parser and the runtime target `net10.0`.
- .NET 10 SDK, pinned in `global.json`. `Directory.Build.props` treats warnings as errors.
- C# for the shell. `Winshell.Qml` parses QML. JavaScript runs on Jint. Items are drawn with Skia. Windows are DirectComposition surfaces with DWM backdrops.
- xUnit v3 for `tests/`, through VSTest.
- PowerShell 7 for `scripts/`. `scripts/install.ps1` and `scripts/uninstall.ps1` also run on Windows PowerShell 5.1.

## Project Structure

```text
.
├── .github/                         # CI, issue and PR templates, Dependabot
├── docs/
│   ├── adr/                         # Decisions already made
│   ├── agents/                      # How the engineering skills use this repo
│   │   ├── quickshell/              # Quickshell v0.3.2 checkout and manual, where the API started; gitignored, made by scripts/dev.ps1
│   │   └── release-notes/           # Notes for a release, once that release exists
│   ├── guide/                       # The docs site's guide section
│   ├── reference/                   # A page per module, checked against the code by DocsTests
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
├── scripts/                         # Check, test, package, install and release scripts; docs/development.md lists them
├── src/
│   ├── BannedSymbols.txt            # Clock calls that don't build; time comes through a TimeProvider
│   ├── Winshell/                    # The winshell CLI and the process that runs a shell
│   ├── Winshell.Qml/                # The QML parser
│   ├── Winshell.Runtime/            # The QML runtime, the item tree, and the Winshell modules
│   ├── Winshell.Services/           # The Windows services and the code under them, with no QML, Jint or Skia
│   └── Winshell.Platform.Windows/   # Windows 11 windows, composition, and the QML face of the services
├── tests/
│   ├── Winshell.Qml.Tests/
│   ├── Winshell.Runtime.Tests/
│   ├── Winshell.Services.Tests/     # The services, with no window and no QML
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

- Build with `dotnet build .\winshell.slnx -warnaserror`, which must pass with 0 warnings, then run the focused tests for what you changed. Add `dotnet test .\tests\Winshell.Qml.Tests -c Release --filter Category=Corpus` when you touch `src/Winshell.Qml`, `dotnet test .\winshell.slnx --filter Category=Leak` when you touch reload, and `dotnet test .\tests\Winshell.Runtime.Tests --filter Category=Performance` when you touch Jint. The required `ci` job runs the build and the main test stage on a PR, and all of `pwsh scripts/check.ps1` on a push to `main` and on the release PR. `check.ps1` runs the Pester suites when Pester 5.6.1 is installed, and does not run PSScriptAnalyzer, lychee, actionlint, zizmor or `package.ps1`. The ruleset also requires the `installer` job, `actionlint`, `pr-title`, and the docs `build` job.
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
