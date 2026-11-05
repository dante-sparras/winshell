# Winshell.Diagnostics

The checks `winshell doctor` prints, which a shell can also run for itself.

<!-- generated below: dotnet run scripts/generate-reference.cs -->

## CheckStatus

How a DoctorCheck came out.

| Value | Number | Description |
| --- | --- | --- |
| `Pass` | 0 | Nothing is wrong. |
| `Warn` | 1 | Winshell runs, but something may not work as expected. |
| `Fail` | 2 | Something stops Winshell from working. `winshell doctor` exits with 1. |

## Doctor

Checks this PC's setup for Winshell. Nothing is changed: no prompt, no registry or settings write, no instance started or stopped, and no build. Telling whether a folder can be written makes and removes one empty file. The user's own folders are written as `%LOCALAPPDATA%`, `%APPDATA%` and `%USERPROFILE%`, so what it reports can go into a bug report without the Windows user name.

| Member | Type | Description |
| --- | --- | --- |
| `ForThisProcess(string configRoot)` | IReadOnlyList of [DoctorCheck](#doctorcheck), static | Every check, for the shell running in this process. A configuration project's program is checked in its own folder. Any other program is checked in configRoot. Nothing is built. |
| `StatusName([CheckStatus](#checkstatus) status)` | string, static | The status as `winshell doctor` writes it: `pass`, `warn` or `fail`. |

## DoctorCheck

One thing `winshell doctor` checked: what it found and, when something is wrong, how to fix it.

| Member | Type | Description |
| --- | --- | --- |
| `DoctorCheck(string Id, string Title, [CheckStatus](#checkstatus) Status, string Message, string? Fix = null)` | constructor |  |
| `Fix` | string?, init | How to fix what is wrong, or null when nothing is or there is nothing to do. |
| `Id` | string, init | A stable name for the check, such as `dotnet`. |
| `Message` | string, init | What the check found. |
| `Status` | [CheckStatus](#checkstatus), init | How the check came out. |
| `Title` | string, init | The check's name as `winshell doctor` prints it. |
