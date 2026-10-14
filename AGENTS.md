# Winshell

Winshell is the Windows 11 shell toolkit that matches Quickshell in features and behavior.

## Setup

Run `pwsh dev.ps1` after cloning. It logs NuGet in to GitHub Packages with the `gh` login, which needs the `read:packages` scope, clones Quickshell into `docs/agents/quickshell/`, and fetches its manual into `site/` there. Both are gitignored.

## Commands

Prepend the Visual Studio installer to `PATH` in every PowerShell that builds or publishes. NativeAOT finds `vswhere.exe` there.

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH
dotnet test tests/Winshell.Tests/Winshell.Tests.csproj
dotnet build src/Winshell/Winshell.csproj
dotnet build src/Winshell.Cli/Winshell.Cli.csproj
pwsh publish.ps1
pwsh pack.ps1
```

`publish.ps1` writes `Winshell.Host.exe`, `Winshell.Watch.exe`, the native `winshell.exe`, and `version.json` to `%TEMP%\winshell-publish\Winshell`, and throws when the folder is not whole. The shell runs from a copy in `%LOCALAPPDATA%\Winshell`. It loads `..\Winoma\Winoma.Desktop.dll` from its own folder, so a shell started from anywhere else loads no Winoma.

`pack.ps1` packs Winshell.Core and Winshell.Kit into `%USERPROFILE%\.nuget\local-feed`, and prints the version. Winoma's `dev.ps1 -Local` builds against that feed.

## Stack

C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors. WinUI 3 draws the surfaces. The app is an unpackaged executable, not MSIX.

Public Core members carry XML documentation. Tests use MSTest and FluentAssertions, one test at a time.

## Versions and packages

A version is `0.1.<commits on HEAD>`, printed by `version.ps1`. Each green build of main pushes Winshell.Core and Winshell.Kit to GitHub Packages and makes the release `v<version>` with `Winshell-win-x64.zip`. Its `version.json` names the Winland version this shell was built against.

## Issues

Issues are GitHub Issues on this repo, labelled with the roles in `docs/agents/triage-labels.md`. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking.

## More

- Quickshell Repo - `docs/agents/quickshell/` (https://github.com/quickshell-mirror/quickshell, tag v0.3.2).
- Quickshell Docs - `docs/agents/quickshell/site/`