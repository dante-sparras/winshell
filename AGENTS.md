# Winshell

Winshell is the Windows 11 shell toolkit that matches Quickshell: the Kit, the plugin host, and the surfaces a plugin draws on (Bar, Panel, Toast, Pill). It ships no widgets. It runs on Winland, and it loads Winoma as an extension. Use the terms in `CONTEXT.md`. `docs/adr/README.md` lists every decision by area: read it before changing how an area behaves, then the ADRs it names. The CLI is `docs/user/cli.md`, and the settings are `docs/user/config.md`.

Winland (https://github.com/dante-sparras/winland), Winshell, and Winoma (https://github.com/dante-sparras/winoma) are three repos (Winoma ADR 0177). This repo holds only Winshell.

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

`winshell summon <id> [<command>]`, `winshell dismiss`, `winshell plugin run <id> <command> [down|up]`, `winshell state [-j]`, and `winshell stop` ask the running shell.

## Stack

C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors. WinUI 3 draws the surfaces. The app is an unpackaged executable, not MSIX.

| Project | Holds |
| --- | --- |
| `src/Winshell.Core` | Plugin host, manifests, the surfaces a plugin draws on, and the extension contract. References only the Winland.Ipc package. A package |
| `src/Winshell.Kit` | The Kit: controls and tokens every surface draws with (ADR 0045). References only Winshell.Core. A package |
| `src/Winshell` | `Winshell.Host.exe`, the shell. One WinUI application, the Kit brushes, the bar, panel, toast, and pill (with an extension and without one), the plugin host, the Winland client, the taskbar setting, and the extensions named in settings |
| `src/Winshell.Cli` | The `winshell` client |
| `tests/Winshell.Tests` | Winshell.Core only |

Public Core members carry XML documentation. Tests use MSTest and FluentAssertions, one test at a time.

## Versions and packages

A version is `0.1.<commits on HEAD>`, printed by `version.ps1`. Each green build of main pushes Winshell.Core and Winshell.Kit to GitHub Packages and makes the release `v<version>` with `Winshell-win-x64.zip`. Its `version.json` names the Winland version this shell was built against.

`WinlandVersion` in `Directory.Build.props` pins Winland.Ipc. The handshake refuses a Winland built from another Winland.Ipc, so move the pin to take a Winland protocol change. `pwsh dev.ps1 -Local` packs the `winland` checkout beside this one and builds against it, to try a Winland fix before it is released. `pwsh dev.ps1 -Pinned` goes back to the pin. Never commit `local.props`.

Winoma.Desktop runs inside this process and binds the shell's own copies of Winshell.Core, Winshell.Kit, Winland.Ipc, WinUI, and the WinRT runtime. A change to a public member of Winshell.Core or Winshell.Kit, or to the extension contract, breaks Winoma when it moves to that version, so say so in the commit and open an issue on Winoma.

## Boundaries

- Win32 lives in `src/Winshell`. A new behavior gets a Core type and a test before a platform call.
- Start `Winshell.Host.exe` through WMI, as below. A child of the agent command dies with the command and leaves the Windows taskbar hidden. Leave `Winshell.Watch` running. It runs from a copy outside the live folder and exits when `Local\Winshell` is held.
- A file in `.config\winshell` may `include` others. Included files merge key by key: objects merge, arrays and scalars replace, and `null` removes a key. The file that includes them applies last.
- Winshell is unreleased, and Dante's Host is the only install. Rename an id or change a file format outright, and fix his files in `%USERPROFILE%\.config\winshell` in the same change. A change Winoma must follow is an issue there.
- Read ADR 0026 before changing how a plugin loads or what it is granted, and ADR 0031 before adding a command, a chord, or an Escape claim.

## Workflow

After a change:

1. `dotnet test tests/Winshell.Tests/Winshell.Tests.csproj` passes, and `dotnet build src/Winshell/Winshell.csproj` and `dotnet build src/Winshell.Cli/Winshell.Cli.csproj` have no warnings.
2. Replace the shell. Publish while the old one runs, and go on only when the publish succeeded. Copy `%USERPROFILE%\.config\winshell` to a timestamped folder under `%LOCALAPPDATA%` before the swap. Leave `Winland.Host` and `Winland.Watch` running.

   ```powershell
   $next = Join-Path $env:TEMP 'winshell-publish\Winshell'
   $live = Join-Path $env:LOCALAPPDATA 'Winshell'
   Remove-Item (Join-Path $live '*.old') -ErrorAction SilentlyContinue
   Set-Content (Join-Path $env:USERPROFILE '.config\winshell\lifetime') replacing -NoNewline
   Get-Process Winshell.Host -ErrorAction SilentlyContinue | Stop-Process -Force
   $gone = (Get-Date).AddSeconds(30)
   while ((Get-Date) -lt $gone -and (Get-Process Winshell.Host -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 200 }
   if (Get-Process Winshell.Host -ErrorAction SilentlyContinue) { throw 'the old shell is still running' }
   New-Item -ItemType Directory -Force $live | Out-Null
   Get-ChildItem $live -File | Where-Object { $_.Name -like 'Win*' } | Rename-Item -NewName { $_.Name + '.old' }
   $copied = $false
   $copyUntil = (Get-Date).AddSeconds(30)
   do {
     robocopy $next $live /E /XD watch /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
     if ($LASTEXITCODE -lt 8) { $copied = $true; break }
     # A held file is renamed, which Windows allows, and the copy writes the new one.
     Get-ChildItem $live -File -Recurse | Where-Object { $_.FullName -notlike '*\watch\*' -and $_.Name -notlike '*.old' } | ForEach-Object {
       $file = $_
       try {
         $held = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
         $held.Dispose()
       } catch {
         Rename-Item -LiteralPath $file.FullName -NewName ($file.Name + '.old') -ErrorAction SilentlyContinue
       }
     }
   } while ((Get-Date) -lt $copyUntil)
   if (-not $copied) { throw 'the copy of Winshell failed' }
   Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$live\Winshell.Host.exe`""; CurrentDirectory = $live } | Out-Null
   ```

   `replacing` tells `Winshell.Watch` that a build follows (ADR 0059). If `Local\Winshell` is not held 30 seconds after the old process exits, it shows the taskbar. A process that starts while a killed one still holds `Local\Winshell` exits at once, so the wait comes first.

   A shell built against another Winland.Ipc than the running Winland is refused at the handshake. Replace Winland from its repo first, or build with `dev.ps1 -Pinned` against the Winland that runs.

   Wait up to 60 seconds for `ready`: in `winoma.log` with the new shell's `pid=` when Winoma is loaded, or in `winshell.log` when it is not. Roll back when the process exits before `ready`: restore the config backup and the `.old` files, and start `Winshell.Host.exe`. Done when `Winshell.Host` is alive, the log has `ready`, and the taskbar is hidden when the setting says so.
3. Check it with `winshell state` and `winshell summon`. Use the CUA driver only when the question is what the screen drew.

Diagnose in this order, and leave the desktop alone until the bug has a name: `winshell state -j`, then the log for that pid, then one `winshell` command to reproduce, then the screen. Read `docs/agents/files.md` before reading or writing `lifetime`, `restarts`, or `winshell.log`.

`WINSHELL_CONFIG` or `--config <folder>` points `Winshell.Host` at another config folder, and `--config` wins when both are set. A check of the shell with no Winoma uses a folder of its own: no `include` of Winoma's shipped defaults, `"taskbar": "shown"`, and a sheet plugin in that folder's `plugins`. Leave Dante's `.config\winshell\settings.json` as it is.

## Issues

Issues are GitHub Issues on this repo, labelled with the roles in `docs/agents/triage-labels.md`. A bug found here that belongs to Winland or Winoma is an issue there: `gh issue create -R dante-sparras/winland` or `-R dante-sparras/winoma`, with what was run, what was seen, and the log lines. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking.

## More

Quickshell v0.3.2 is cloned at `docs/agents/quickshell/` (https://github.com/quickshell-mirror/quickshell, tag v0.3.2). Read `site/` there before matching a Quickshell behavior.
