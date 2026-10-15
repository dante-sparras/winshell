# Quickshell reference

This repo keeps a local copy of Quickshell v0.3.2 and its documentation.

## Setup

Run `pwsh scripts/dev.ps1` after cloning. It clones Quickshell into `docs/agents/quickshell/` and fetches the v0.3.2 manual into `site/` there. Both are gitignored. `pwsh scripts/dev.ps1 -Refresh` downloads the manual again.

The fetcher is `docs/fetch_reference.py`. It reads <https://quickshell.org/docs/v0.3.2/> and writes markdown under the manual folder. Leave those pages as the fetcher wrote them.

## Docs

- Quickshell source: `docs/agents/quickshell/` (<https://github.com/quickshell-mirror/quickshell>, tag v0.3.2).
- Quickshell manual: `docs/agents/quickshell/site/`.
- Glossary: `CONTEXT.md`.
- Decisions: `docs/adr/README.md`.
- Issues: `docs/agents/issue-tracker.md`.
- Domain docs: `docs/agents/domain.md`.
