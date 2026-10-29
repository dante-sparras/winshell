# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Stack

- Windows 11. The host and the platform target `net10.0-windows10.0.22621.0`. The parser and the runtime target `net10.0`.
- .NET 10 SDK, pinned in `global.json`. `Directory.Build.props` treats warnings as errors.
- C# for the shell. `Winshell.Qml` parses QML. JavaScript runs on Jint. Items are drawn with Skia. Windows are DirectComposition surfaces with DWM backdrops.
- xUnit for `tests/`.
- PowerShell 7 for `scripts/dev.ps1`, `scripts/check.ps1`, `scripts/package.ps1`, `scripts/update-snapshots.ps1`, and the file-based apps `scripts/fetch-reference.cs` and `scripts/render-logo.cs`.

## Project Structure

```text
.
├── .github/                         # CI, issue and PR templates, Dependabot
├── docs/
│   ├── adr/                         # Decisions already made
│   ├── agents/                      # How the engineering skills use this repo
│   │   └── quickshell/              # Quickshell v0.3.2 checkout and manual, where the API started; gitignored, made by scripts/dev.ps1
│   ├── guide/                       # The docs site's guide section
│   ├── development.md               # Setup, environment, and every command
│   ├── docfx.json                   # Builds docs/ into the docs site
│   ├── index.md                     # The docs site's home page
│   └── migrating-from-quickshell.md # Quickshell names and their Winshell names
├── packaging/
│   ├── logo.svg                     # The app logo; scripts/render-logo.cs renders its PNGs
│   ├── msix/                        # The Microsoft Store package's manifest and assets
│   ├── signpath/                    # The artifact configuration SignPath signs releases with
│   └── store/                       # The Store listing text and its app tile
├── scripts/
│   ├── check.ps1                    # Build with warnings as errors, build the docs, then test
│   ├── dev.ps1                      # Prepares a fresh clone
│   ├── fetch-reference.cs           # Fetches the Quickshell manual as markdown
│   ├── package.ps1                  # Builds the portable zips, winget manifests and Store package a release ships
│   ├── render-logo.cs               # Renders packaging/logo.svg into the package's PNGs
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
│   └── Winshell.Tests/              # Runs winshell.exe instances side by side
├── winshell.slnx                    # The solution the check builds
├── dotnet-tools.json                # DocFX, pinned
├── .agents/skills/                  # Agent skills; a copy in .claude/skills/
├── CONTRIBUTING.md                  # Branches, commit and PR titles, PR rules
└── GLOSSARY.md                      # The words this repo uses
```

## Before you finish

- Run `pwsh scripts/check.ps1`. Everything must pass.
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
