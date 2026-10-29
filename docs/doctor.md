# winshell doctor

`winshell doctor` checks how this PC is set up for Winshell. It prints a line per check, `PASS`, `WARN` or `FAIL`, and a fix under each warning and failure. It exits with 1 when a check fails and 0 otherwise.

```text
winshell doctor              # the checks, colored when printed to a console
winshell doctor --no-color   # without color; NO_COLOR or a redirected output does the same
winshell doctor --json       # the same checks as JSON
winshell doctor -c <name>    # check that config instead of the one winshell would pick
winshell doctor -p <path>    # check the shell.qml at that path, or in that folder
```

The **Run diagnostics** button on the About page of Winshell Settings runs the same checks for the config the shell runs, and lists them under the card.

## The checks

| Check | Passes when | Otherwise |
| --- | --- | --- |
| Windows | Windows 11 version 22H2 (build 22621) or later. Shows the version and build. | Fails before Windows 11 and warns before 22H2. |
| .NET | .NET 10 or later. Says whether it is built into Winshell or installed on the PC. | Fails. |
| Config | Every `.qml` file in the config's folder and the folders under it parses, and the running instance of that config logged no load error after its last load. Folders starting with `.`, `node_modules` and links are skipped. No config of your own passes: Winshell runs its default shell. | Fails with the first error's file, line and column, or when several configs exist and none is picked. |
| Folders | Winshell can write the config folder and `%LOCALAPPDATA%\winshell`, and no instance runs as administrator. | Fails when a folder cannot be written; warns about an instance that runs as administrator. |
| App bars | No app other than the Windows taskbar and Winshell reserves an edge of a screen. | Warns, naming the app when a window of its size lies along that edge. Also warns when the Windows taskbar and a Winshell panel both reserve the same edge. |
| DPI | Winshell is per-monitor DPI aware, and Windows keeps no compatibility setting for `winshell.exe`. Shows each screen's scale. | Fails when an "override high DPI scaling" setting is set or Winshell is not per-monitor aware; warns about another compatibility mode. |
| Sign-in | No sign-in entry, or one that starts this `winshell.exe` and that Startup apps leaves on. | Fails when the entry starts a `winshell.exe` that is not there; warns when it starts another one, or Startup apps turned it off. |
| Notifications | Windows lets Winshell read notifications. Asked without a prompt. | Warns, saying whether access was denied, never asked for, or needs a packaged Winshell. |
| Running | Always passes. Lists each running instance, its process, its config and its log. | |
| Crashes | No crash report in `%LOCALAPPDATA%\winshell\crashes` from the last 7 days. | Warns with the count and the newest report. |

WebView2 and the Windows App SDK are not checked: Winshell uses neither. A check that cannot run warns with the reason, and the other checks still run.

## What it changes

Nothing. It shows no prompts, writes no registry values or settings, and starts or stops no instance. To check that a folder can be written, it makes one empty file there, which is deleted as it closes.

## JSON

```json
{
  "version": "0.5.0",
  "checks": [
    { "id": "windows", "title": "Windows", "status": "pass", "message": "Windows 11 25H2, build 26200.1", "fix": null }
  ],
  "summary": { "pass": 9, "warn": 1, "fail": 0 }
}
```

`id` is one of `windows`, `dotnet`, `config`, `folders`, `appBars`, `dpi`, `signIn`, `notifications`, `instances` and `crashes`. `status` is `pass`, `warn` or `fail`. `fix` is null when the check passed.

## Not checked by the automated tests

The tests judge each check from made-up facts, run every check on the CI machine, and run `winshell doctor` and `--json` as processes. They do not set up another app bar, a compatibility setting, an elevated instance or a folder that cannot be written on a real PC, so those messages were only checked from made-up facts.
