# Winshell

A desktop shell toolkit for Windows 11. A configuration is a .NET project that builds into one program.

## Stack

- Windows 11. Projects target `net10.0-windows10.0.22621.0`. The template package targets `net10.0`.
- .NET 10 SDK, pinned in `global.json`. `Directory.Build.props` treats warnings as errors.
- C# and WinUI 3. `winshell.exe` runs the default shell and builds a configuration.
- xUnit v3 for `tests/`, through VSTest.
- PowerShell 7 for `scripts/`. `scripts/install.ps1` and `scripts/uninstall.ps1` also run on Windows PowerShell 5.1.

## Project Structure

```text
.
├── .github/                         # CI, issue and PR templates, Dependabot
├── docs/
│   ├── adr/                         # Decisions already made
│   ├── agents/                      # How the engineering skills use this repo
│   │   ├── release-notes/           # Notes for a release, once that release exists
│   │   └── test-audit/              # The gate every test passes
│   ├── guide/                       # The docs site's guide section
│   ├── reference/                   # A page per module
│   ├── security/                    # The threat model
│   ├── development.md               # Setup, environment, and every command
│   ├── docfx.json                   # Builds docs/ into the docs site
│   └── index.md                     # The docs site's home page
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

- Writing, changing or reviewing a test? It must pass the gate in [docs/agents/test-audit/SKILL.md](docs/agents/test-audit/SKILL.md).
- Before every push, `pwsh scripts/quick-check.ps1` must pass: the build with 0 warnings and the fast tests, in under two minutes, with no window opened.
- Changed what `[Trait("Category", "Slow")]` tests cover? Run them too: `pwsh scripts/run-tests.ps1 -Tier Slow`, under "Live UI with Winland running". `dotnet test .\tests\Winshell.Package.Tests --filter Category=Package` packs a shell from each template and runs it; the main stage leaves it out.
- CI runs the rest, so leave full `check.ps1`, `package.ps1` and arm64 to it. The required `ci` job runs the build and the fast tests on a PR, and all of `pwsh scripts/check.ps1 -Tier Fast` on a push to `main` and on the release PR. The required `slow` job runs the slow tests, and skips a PR that only changes docs or Markdown. The ruleset also requires the `installer` job, `actionlint`, `pr-title`, and the docs `build` job.
- If you touched `scripts/*.ps1` or `.github/`, also run the jobs [docs/development.md](docs/development.md#commands) lists for that change.
- Name the branch, title the PR, and fill in the PR template as [CONTRIBUTING.md](CONTRIBUTING.md#open-a-pr) describes.
- Say what you couldn't verify.
- Added or changed a command, prerequisite, environment variable, or top-level folder? Update [docs/development.md](docs/development.md) or Project Structure.
- Don't edit `.agents/skills/` or `.claude/skills/`; the `skills` CLI overwrites them.
- Never commit `.env` files or credentials.

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

## Agent skills

### Issue tracker

Issues live in GitHub Issues for dante-sparras/winshell, used through `gh`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Test audit

Audits and sweeps of `tests/` follow `docs/agents/test-audit/SKILL.md`. It is this repo's own, adapted from OpenClaw's skill, so it lives outside `.agents/skills/`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Notes

I like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. Try to honor the dev's intent in both a minimal and realistic fashion.
