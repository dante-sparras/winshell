# Winshell.Diagnostics

The checks `winshell doctor` prints. `Doctor.Run` is the PC checks. `Doctor.Configuration` reads a configuration project as data and does not build it.

## CheckStatus

| Value | Number | Description |
| --- | --- | --- |
| `Pass` | 0 |  |
| `Warn` | 1 |  |
| `Fail` | 2 |  |

## Doctor

Checks this PC's setup for Winshell. Each check reads what it needs through IDoctorFacts and judges it here, so the judging is tested without the PC. Nothing is changed: no prompt, no registry or settings write, no instance started or stopped, and no build. Telling whether a folder can be written makes and removes one empty file. The user's own folders are written as `%LOCALAPPDATA%`, `%APPDATA%` and `%USERPROFILE%`, so what it prints can go into a bug report without the Windows user name.

| Member | Type | Description |
| --- | --- | --- |
| `BugReports` | string, static, constant |  |
| `Configuration(string configRoot)` | IReadOnlyList of [DoctorCheck](#doctorcheck), static | The configuration project in configRoot, or why there is not one: whether it was found, whether a file the build would run was downloaded, whether its program has been built, and whether a build is needed. Nothing here runs MSBuild. |
| `ForThisProcess(string configRoot)` | IReadOnlyList of [DoctorCheck](#doctorcheck), static | The whole list for the running program, with its own project or the default shell. Nothing is built. |
| `Format(IReadOnlyList of [DoctorCheck](#doctorcheck) checks, string version, bool color)` | string, static | The checks as `winshell doctor` prints them: a line each, a fix under each problem, and a count. |
| `Run([DoctorContext](#doctorcontext) context, [IDoctorFacts](#idoctorfacts) facts)` | IReadOnlyList of [DoctorCheck](#doctorcheck), static |  |
| `StatusName([CheckStatus](#checkstatus) status)` | string, static |  |
| `Summary(IReadOnlyList of [DoctorCheck](#doctorcheck) checks)` | string, static |  |
| `ToJson(IReadOnlyList of [DoctorCheck](#doctorcheck) checks, string version)` | string, static | The checks as `winshell doctor --json` prints them. |

## CrashFolderRead

Crash reports in a folder, or why the folder could not be listed.

| Member | Type | Description |
| --- | --- | --- |
| `CrashFolderRead(IReadOnlyList of (string, DateTime) Reports, string? Unreadable)` | constructor |  |
| `Reports` | IReadOnlyList of (string, DateTime), init |  |
| `Unreadable` | string?, init |  |

## CrashPeek

The start of one crash file. No text and no error means the file is not a Winshell crash report.

| Member | Type | Description |
| --- | --- | --- |
| `CrashPeek(string? Text, string? Unreadable)` | constructor |  |
| `Text` | string?, init |  |
| `Unreadable` | string?, init |  |

## MonitorFacts

A screen: its name, its bounds and its work area, in physical pixels.

| Member | Type | Description |
| --- | --- | --- |
| `MonitorFacts(string Name, int Left, int Top, int Right, int Bottom, int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, double Scale)` | constructor |  |
| `Bottom` | int, init |  |
| `Left` | int, init |  |
| `Name` | string, init |  |
| `Right` | int, init |  |
| `Scale` | double, init |  |
| `Top` | int, init |  |
| `WorkBottom` | int, init |  |
| `WorkLeft` | int, init |  |
| `WorkRight` | int, init |  |
| `WorkTop` | int, init |  |

## WindowFacts

A visible top-level window: its class, its bounds in physical pixels, and its program.

| Member | Type | Description |
| --- | --- | --- |
| `WindowFacts(string ClassName, int Left, int Top, int Right, int Bottom, string Program)` | constructor |  |
| `Bottom` | int, init |  |
| `ClassName` | string, init |  |
| `Left` | int, init |  |
| `Program` | string, init |  |
| `Right` | int, init |  |
| `Top` | int, init |  |

## DoctorCheck

One thing `winshell doctor` checked: what it found and, when something is wrong, how to fix it.

| Member | Type | Description |
| --- | --- | --- |
| `DoctorCheck(string Id, string Title, [CheckStatus](#checkstatus) Status, string Message, string? Fix = null)` | constructor |  |
| `Fix` | string?, init |  |
| `Id` | string, init |  |
| `Message` | string, init |  |
| `Status` | [CheckStatus](#checkstatus), init |  |
| `Title` | string, init |  |

## DoctorContext

What the doctor needs from `winshell.exe`: this executable, the config that would run (or why none would), the running instances and Winshell's folders.

| Member | Type | Description |
| --- | --- | --- |
| `DoctorContext(string ExePath, string ConfigRoot, string? ConfigFile, string? ConfigProblem, IReadOnlyList of [DoctorInstance](#doctorinstance) Instances, string LocalRoot, string CrashRoot)` | constructor |  |
| `ConfigFile` | string?, init |  |
| `ConfigProblem` | string?, init |  |
| `ConfigRoot` | string, init |  |
| `CrashRoot` | string, init |  |
| `ExePath` | string, init |  |
| `Instances` | IReadOnlyList of [DoctorInstance](#doctorinstance), init |  |
| `LocalRoot` | string, init |  |
| `ProjectRoot` | string?, init | The folder [Configuration](#doctor) searches. Null searches `ConfigRoot`. |

## DoctorInstance

A running Winshell, as the instance list has it.

| Member | Type | Description |
| --- | --- | --- |
| `DoctorInstance(string Id, int Pid, string ConfigPath, string LogPath)` | constructor |  |
| `ConfigPath` | string, init |  |
| `Id` | string, init |  |
| `LogPath` | string, init |  |
| `Pid` | int, init |  |

## IDoctor

Runs the checks `winshell doctor` and Winshell Settings' About page show. All of them only read.

| Member | Type | Description |
| --- | --- | --- |
| `Run()` | IReadOnlyList of [DoctorCheck](#doctorcheck) |  |

## IDoctorFacts

What Run reads from this PC. Every read stays a read: the doctor never builds, and a configuration project is judged from the project file alone.

| Member | Type | Description |
| --- | --- | --- |
| `CanWrite(string folder)` | bool |  |
| `CompatibilityLayers(string exePath)` | IReadOnlyList of string |  |
| `ContrastThemeOn()` | bool |  |
| `EdgeWindows()` | IReadOnlyList of [WindowFacts](#windowfacts) |  |
| `IsElevated(int pid)` | bool |  |
| `Monitors()` | IReadOnlyList of [MonitorFacts](#monitorfacts) |  |
| `NotificationAccess()` | string |  |
| `PeekCrash(string path)` | [CrashPeek](#crashpeek) |  |
| `PerMonitorV2()` | bool |  |
| `ReadCrashReports(string folder)` | [CrashFolderRead](#crashfolderread) |  |
| `ResolveLinks(string path)` | string |  |
| `RunEntry()` | string? |  |
| `RunEntryTurnedOff()` | bool |  |
| `SelfContained(string exePath)` | bool |  |
| `TransparencyEffectsOff()` | bool |  |
| `UserConfigProblems(string? configFile, string configRoot, string localRoot)` | IReadOnlyList of string |  |
| `WindowsBuild()` | int |  |
| `WindowsDisplayVersion()` | string? |  |
| `WindowsServer()` | bool |  |
| `WindowsUpdateRevision()` | int |  |

