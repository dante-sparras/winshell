# QML configurations

A configuration used to be QML. It is a .NET project now. There is no converter, and no release runs both.

Rewrite the shell as a project:

```text
dotnet new winshell -n MyShell
```

`Main`, marked `[STAThread]`, calls `Shell.Run<T>(new ShellOptions { ... })`. A window is a `PanelWindow`, `PopupWindow` or `FloatingWindow`, and its content is any WinUI element. Services hang off `Shell`. `Shell.Ipc.Handle` replaces `IpcHandler`. The [configuration guide](../../guide/configuration.md) is the shape. The samples in that guide are compiled in `tests/Winshell.Shell.Tests/GuideSamples.cs`.

`dotnet new winshell-default -n MyShell` is the default shell as a project you can change.

Preferences stayed. `preferences.toml` and `launcher.toml` are still TOML in the config folder (`%APPDATA%\winshell`, or `WINSHELL_CONFIG_DIR`). `quick-settings.toml` is still in the shell's state folder, and `taskbar.json` is still in the shell's data folder. A folder that still has a `shell.qml` and no `.csproj` runs the default shell. A notice says QML configurations stopped working.

`winshell reload` builds the project and, when the build works, replaces the running instance. Saving a file does not. `dotnet watch` is the fast loop. The .NET SDK is needed only while writing a shell. Running the default shell, and signing in, do not need it.
