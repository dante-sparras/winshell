# Project instructions

Run `pwsh scripts/dev.ps1` after cloning. It clones Quickshell into `docs/agents/quickshell/` and fetches the v0.3.2 manual into `site/` there. `pwsh scripts/dev.ps1 -Refresh` downloads the manual again.

- Issues: `docs/agents/issue-tracker.md`.
- Triage labels: `docs/agents/triage-labels.md`.

# Conventions

- The Quickshell checkout and its manual are gitignored.
- `scripts/fetch-reference.cs` reads <https://quickshell.org/docs/v0.3.2/> and writes markdown under the manual folder. Leave those pages as the fetcher wrote them.
- Domain docs: `docs/agents/domain.md`.

# Architecture

This repo keeps a local copy of Quickshell v0.3.2 and its documentation, and a hello-world app under `src/`. It has no tests yet.

- Quickshell source: `docs/agents/quickshell/` (<https://github.com/quickshell-mirror/quickshell>, tag v0.3.2).
- Quickshell manual: `docs/agents/quickshell/site/`.
