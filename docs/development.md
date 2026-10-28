# Development

## Prerequisites

- Git
- PowerShell 7
- .NET 10 SDK
- Node.js, for `npx` when updating agent skills

## First checkout

```sh
git clone https://github.com/dante-sparras/winshell.git
cd winshell
pwsh scripts/dev.ps1
```

## Commands

| Command | What it does |
| --- | --- |
| `pwsh scripts/dev.ps1` | Clones Quickshell v0.3.2 into `docs/agents/quickshell/` and fetches its manual into `site/` there. |
| `pwsh scripts/dev.ps1 -Refresh` | Downloads the Quickshell manual again. |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before opening a PR, run the checks CI runs: `TODO: add the check command once the stack exists`.

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release and publishes it.

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.
