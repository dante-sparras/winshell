# Development

## Prerequisites

- Git
- PowerShell 7
- .NET 10 SDK
- Node.js, for `npx` when updating agent skills

Reading Windows notifications additionally requires listener permission and a package identity declaring `userNotificationListener`; native toast sending requires an installed Windows AppUserModelID. Neither is needed to build or run automated tests. The unpackaged CLI does not provision these identities. See [Windows notifications](notifications.md) for deployment requirements, permission behavior and platform limits.

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
| `pwsh scripts/check.ps1` | Builds `winshell.slnx` with warnings as errors, then runs `dotnet test` on that build. The Jint performance budgets (`Category=Performance`) run last and alone, and print what they measured. This is the check CI runs. |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before opening a PR, run the checks CI runs: `pwsh scripts/check.ps1`.

## Environment variables

A running shell reads these. None is needed to build or test.

| Variable | What it does |
| --- | --- |
| `WINSHELL_APP_ID` | Sets `Shell.appId` when the configuration has no `//@ pragma AppId`. |
| `WINSHELL_DISABLE_FILE_WATCHER` | Any value stops the shell from reloading when its files change. |
| `WINSHELL_NO_RELOAD_POPUP` | `1` hides the popup that reports a reload. |

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release and publishes it.

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.

## Network permissions

Reading connection profiles needs no setup. Wi-Fi scanning/connecting requires Windows Wi-Fi/location access and an explicit `Networking.requestAccess()` call; packaged deployments also need `wiFiControl`. No network or radio changes are made by the automated tests: actions use a fake backend, and native smoke tests only read state. See [Windows networking](network.md).
