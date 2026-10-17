# Project instructions

Run `pwsh scripts/dev.ps1` after cloning. It clones Quickshell into `docs/agents/quickshell/` and fetches the v0.3.2 manual into `site/` there. `pwsh scripts/dev.ps1 -Refresh` downloads the manual again.

- Issues: `docs/agents/issue-tracker.md`.
- Triage labels: `docs/agents/triage-labels.md`.

# Conventions

- Branch names, commit messages, pull-request approval, evidence, and how a pull request lands are in `CONTRIBUTING.md`. Open a pull request only when the maintainer asks.
- Commands: `pwsh -NoProfile -File scripts/check.ps1` lints and builds. `dotnet test` the test project that covers the change. CI runs `scripts/test.ps1`. `scripts/mutate.ps1` is the release lane.
- Update a document when this change makes it false, and edit it in place. When to leave a document alone is in `CONTRIBUTING.md`.
- The Quickshell checkout and its manual are gitignored.
- `scripts/fetch-reference.cs` reads <https://quickshell.org/docs/v0.3.2/> and writes markdown under the manual folder. Leave those pages as the fetcher wrote them.
- Domain docs: `docs/agents/domain.md`.

# Architecture

This repo keeps a local copy of Quickshell v0.3.2 and its documentation, and a hello-world app under `src/`. It has no tests yet.

- Quickshell source: `docs/agents/quickshell/` (<https://github.com/quickshell-mirror/quickshell>, tag v0.3.2).
- Quickshell manual: `docs/agents/quickshell/site/`.
