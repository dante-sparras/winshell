# Winshell

A shell toolkit for Windows 11 that matches Quickshell.

## Stack

- PowerShell 7 for `scripts/dev.ps1`
- .NET 10 SDK for the file-based app `scripts/fetch-reference.cs`
- TODO: add the app's stack

## Project Structure

```text
.
├── .github/                   # CI, issue and PR templates, Dependabot
├── docs/
│   ├── agents/                # How the engineering skills use this repo
│   │   └── quickshell/        # Quickshell v0.3.2 checkout and manual; gitignored, made by scripts/dev.ps1
│   └── development.md         # Setup, environment, and every command
├── scripts/
│   ├── dev.ps1                # Prepares a fresh clone
│   └── fetch-reference.cs     # Fetches the Quickshell manual as markdown
├── .agents/skills/            # Agent skills; a copy in .claude/skills/
├── CONTRIBUTING.md            # Branches, commit and PR titles, PR rules
└── # TODO: add source directories once code exists
```

## Before you finish

- Run `TODO: add the check command once the stack exists`. Everything must pass.
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
