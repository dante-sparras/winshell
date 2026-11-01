# A configuration is a WinUI 3 program

**Status:** accepted

A configuration becomes a .NET project that references a `Winshell` package and builds into one program showing real WinUI 3 controls. QML, Jint and the Skia scene go, with no release that supports both and no converter. This supersedes ADR-0001, ADR-0002 and ADR-0005, and the backdrop part of ADR-0003. Until the cut below, those ADRs still describe what `main` runs.

The reason is the controls. ADR-0005 left a real IME composition, UI Automation across the built-in controls and a token pass still to do, on top of the 25,000 lines of `Quick/` that imitate Windows 11. WinUI already is Windows 11, so that work and that code stop being ours. Startup and C# tooling are bonuses, not the reason. The cost of moving is low now: this is 0.x, a release can still break a configuration, and no external configuration has turned up.

## The shape

- **A configuration is its own program.** It calls something like `Shell.Run<MyShell>()`, with its options in `ShellOptions` where `//@ pragma` lines were. `winshell.exe` stays as the CLI and runs the default shell, which ships as a built configuration, so only someone writing their own shell needs the .NET SDK.
- **A reload is a build, then a new process.** `winshell reload` builds the configuration. If that fails, the running instance stays and `winshell.exe` shows a Notice. If it works, a new instance starts and takes over from the old one through the handoff the updater already uses. The file watcher goes, and `dotnet watch` is the fast loop while writing a shell. `winshell run` builds when sources are newer. The sign-in entry passes `--no-build`, so signing in never depends on the SDK or on a broken edit.
- **Windows stay Winshell's.** `Win32Window` keeps its styles, app bars, placement, input regions and focus. Only the surface changes: a `DesktopWindowXamlSource` replaces `CompositionSurface`, `DesktopAcrylicController` replaces the DWM backdrop attribute, and WinUI's dispatcher replaces `WinEventLoop`. Winshell closes open flyouts itself on a window that doesn't activate, so a plain `Flyout` works in a panel.
- **The public API is what .NET and WinUI don't already have.** Windows and screens, and the Input, Apps, Audio, Media, Power, Windows, Network, Bluetooth, Notifications, Tray, Capture and Winland namespaces, plus a `WindowThumbnail` control and a live capture view. Services are plain public classes using `INotifyPropertyChanged` and `ObservableCollection`, in a package with no Jint, Skia or QML in it, reached from one object such as `Shell.Current.Audio`. ADR-0007's names stay. Qt Quick, `Winshell.Widgets`, `Winshell.Theme`'s tokens and most of `Winshell.Io` go. `IpcHandler` becomes a C# registration with the same type checks.
- **Reactor is the configuration's choice.** A window's content is a `UIElement`, so a configuration adds `Microsoft.UI.Reactor` itself and sets `new ReactorHostControl(component)` as that content. Winshell never references it, and the default shell is XAML and C#, so a released Winshell doesn't rest on a preview whose API can change without notice.
- **Preferences stay TOML.** ADR-0009 stands. Only the configuration and the Notice move.
- **The default shell's logic moves into plain C# view models**, tested without a window. A small set of tests drives the real program through UI Automation.

## What a spike showed

A throwaway program put WinUI content in a window with the same styles as a `Win32Window` panel: Windows App SDK 2.6.4, Reactor 0.1.0-preview.16, unpackaged and self-contained, on two 2560×1440 screens at 96 DPI.

| Check | Result |
| --- | --- |
| Per-pixel transparency | The window behind showed through unchanged. |
| Backdrop | The DWM attribute came out flat `#545454`, with or without XAML and active or not, the color ADR-0005 sampled for Mica. `DesktopAcrylicController`, told the window is active, gave live acrylic on a window that was never activated. |
| Activation | Clicks reached a XAML button, a Reactor button and a text box, and the foreground window did not change. |
| Keyboard | With `WS_EX_NOACTIVATE` off and the window in front, as Start does it, typed text arrived, Tab moved focus, and the foreground went back. |
| Edge | An app bar moved the work area from 0 to 64 and back, and the content followed the window. |
| Click-through | Clicks passed a `SetWindowRgn` hole and a whole window with `WS_EX_TRANSPARENT`, and the content stayed drawn. |
| DWM thumbnail | It works, and it draws on top of the XAML, so nothing can overlap a preview. |
| Reactor | `ReactorHostControl` works when created from code, and followed `UseState` and an `INotifyPropertyChanged` object. |
| Flyout and tooltip | They leave a short panel in a window of their own and take no focus. A click outside did not close the flyout. |
| Disposal | 20 islands made and disposed in one process took private memory from 97 MB to 101 MB. |
| Two bars of a taskbar, Release, ReadyToRun | 1.37 to 1.79 s to first frame, 128 MB working set (134 MB with Reactor), under 0.1% idle CPU, over seven runs. |

A one-line rebuild took 17 to 61 seconds, against 100 ms for a reload today. A self-contained publish was 288 MB and a 110 MB zip, against a 70 MB zip for v0.7.0. Not tested: screens of different DPI, a real CJK IME, MSIX, a pruned package set, and whether hosted CI runners can render WinUI. The startup figure is not comparable with ADR-0005's 2,272 ms, which was a different panel.

## Considered options

- **Loose `.cs` files compiled by embedded Roslyn.** No SDK needed. WinUI expects compiled XAML and PRI files in the install folder, so only code-built UI would work, and WinUI has no supported way to unload code, so each reload would leave assemblies behind.
- **A class library loaded into `winshell.exe`.** It has the same compiled XAML problem, for a saving of one program file.
- **Loose XAML and TOML, with no C#.** Inert and safe, but it rules out Reactor, and the default shell's 170 or so functions would have nowhere to live.
- **No authoring.** Winshell would be a fixed shell with Preferences, and no longer a toolkit.
- **`Microsoft.UI.Xaml.Window` for panels.** It owns its HWND, so the creation-time styles can't be passed, and Reactor's `ReactorWindow` marks no-activate hosting as Early.
- **The shared Windows App Runtime.** A smaller download, but an install step and a runtime that can change underneath a release. Every channel ships one self-contained build instead, with only the WinUI and Foundation packages, and a custom shell builds self-contained too.

## Consequences

- **Editing got slower.** "Save a file and watch it change" is gone. This is the real loss, and a reason some people use a shell toolkit at all.
- **A configuration is a full-trust program, and building it runs its code.** MSBuild targets, analyzers and source generators run during `dotnet build`, earlier than QML ever ran anything. The `Zone.Identifier` check moves to the `.csproj` and runs before any build. `doctor` never builds: it reads the project file as data and loses its parse check. The folder-only load rule and Jint's limits go, because C# has no equivalent. `PipeTrust` keeps its owner and integrity checks and drops the filename check, which only compares a name.
- **State does not survive a reload** unless `PersistentProperties` gets a new mechanism, and every window is replaced.
- **Versions can differ.** An update does not change a custom shell, so a `winshell` CLI and a shell of different versions refuse each other with a plain message. A release also publishes the NuGet package and the `dotnet new winshell` and `winshell-default` templates, which replace copying the default shell's folder.
- **ADR-0007's last sentence ends at the cut.** The Qt Quick modules go with QML. Its Windows-first names stay.
- **The signing lists grow.** A new Winshell DLL goes in `artifact-configuration.xml`, `package.ps1`, `UpdateSignature.cs` and the code signing policy.

## Words

Applied to `GLOSSARY.md` in the cut, because it describes what `main` runs:

- **Configuration:** the .NET project that defines a shell, built into one program.
- **Reload:** building the configuration and replacing the running instance with the result.
- **Module:** a Winshell namespace.
- **Control:** a WinUI control.
- **Popup** becomes **Flyout**, WinUI's word. **Popup window** stays.
- **Generation**, **Item**, **Binding** and **Token** retire. An instance now does what a generation did.

`docs/security/threat-model.md` changes in the cut for the same reason.

## Order of the work

Everything lands on `main`, with no long-lived branch. Both stacks sit in the repo for a while, and no release ships both. New QML features stop now, and fixes continue.

1. **Services.** Extract the public classes behind thin QML adapters, so QML keeps working and each PR is green.
2. **The new stack.** Build the host and the default shell in-tree, built by CI and not shipped, taskbar first. Screens of different DPI, a CJK IME, the pruned package size and rendering on CI runners are checked here. If runners can't render WinUI, the `settings-surface` and `notification-surface` artifacts become manual evidence.
3. **The cut.** One `feat!:` PR switches `winshell.exe` and removes `Winshell.Qml`, `Quick`, `Js`, `Engine`, Jint, Skia, `docs/agents/quickshell` and `docs/migrating-from-quickshell.md`, and updates the glossary, the threat model, the guides, the reference and the README, Store and winget text.
