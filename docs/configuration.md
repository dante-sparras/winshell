# Configuration guide

A configuration is a .NET project that references the Winshell package and builds into one program. This guide builds one up from a single panel. The [reference](reference/index.md) lists every type and member. The C# on this page is copied from [tests/Winshell.Shell.Tests/GuideSamples.cs](https://github.com/enpaze/winshell/blob/main/tests/Winshell.Shell.Tests/GuideSamples.cs), which the solution builds. Nothing in that file runs. Call `Shell.Run` from `Main`, marked `[STAThread]`.

## Create one

You need the [.NET SDK](https://dotnet.microsoft.com/download), PowerShell 7 and Git. Running the default shell does not. The `dotnet new` templates and the `Winshell` package a configuration references are not published yet, so you pack them from a clone of Winshell. Use a `winshell` [built from that clone](../README.md#build-from-source), so the two are the same release.

```powershell
git clone https://github.com/enpaze/winshell.git
cd winshell
pwsh scripts/pack.ps1
dotnet new install .\artifacts\packages\Winshell.Templates.<version>.nupkg
```

`scripts/pack.ps1` writes both packages to `artifacts\packages`, and `<version>` is the release in their names. Then make the project beside the clone, and tell NuGet where the `Winshell` package is:

```powershell
cd ..
dotnet new winshell -n MyShell
cd MyShell
dotnet new nugetconfig
dotnet nuget add source ..\winshell\artifacts\packages --name winshell --configfile nuget.config
winshell run --path .
```

`dotnet new winshell-default -n MyShell` starts from the default shell. Winshell builds to `MyShell\bin\shell` when a source is newer, then starts that program. `--no-build` runs the program the last build left, and does not build. A failed build does not start the last good build.

A downloaded project is refused before any build. Winshell looks for a `Zone.Identifier` stream on the `.csproj` and on `.props` and `.targets` beside it. Unblock the file (right-click > Properties > Unblock, or `Unblock-File`) if you trust it. `git clone` writes no zone, so it is not caught.

A `winshell` and a shell of different releases refuse each other, except `kill`. The message names both releases.

## The program

`Shell.Run` runs until the shell quits. `ShellId` in `ShellOptions` names the data, state and cache folders.

```csharp
Shell.Run<AppShell>(new ShellOptions { ShellId = "MyShell" });
```

`AppShell` is a `ShellConfiguration`. `Start` runs once the shell is up. `Shell.Current` is that shell.

## A window

Windows stay Winshell's. The content is any `UIElement`. Sizes are logical pixels.

```csharp
        _ = new PanelWindow
        {
            Anchors = Edges.Left | Edges.Right | Edges.Bottom,
            Height = 48,
            ReservedSize = 48,
            Content = new TextBlock { Text = "Winshell" },
            Visible = true,
        };
```

- **[PanelWindow](reference/winshell.md#panelwindow)** sits on the edges of a screen. `Anchors` chooses the edges. `ReservedSize` reserves that edge, so maximized windows stop short of it. `ExclusionMode` of `Ignore` reserves nothing. `AboveWindows` set false puts it behind other windows.
- **[PopupWindow](reference/winshell.md#popupwindow)** is a frameless window placed next to another. `GrabFocus` gives it the keyboard. `Dismissed` fires when it closes.
- **[FloatingWindow](reference/winshell.md#floatingwindow)** is a normal window with a title bar.

`Backdrop` is `WindowBackdrop.Mica`, `Acrylic`, `MicaAlt` or `None`. `Corners` rounds the window. A flyout is a WinUI `Flyout` in the window's content. Winshell closes an open flyout on a window that does not activate.

One panel per screen walks `shell.Screens`. The primary screen is first. The collection changes as monitors are connected and removed.

Reactor is the configuration's choice. Add `Microsoft.UI.Reactor` in the project and set `new ReactorHostControl(component)` as a window's content. Winshell never references it.

## A service

Services hang off `Shell`, created when first read. `shell.Audio`, `shell.Power`, `shell.Apps`, `shell.WindowList`, `shell.Networking`, `shell.Bluetooth`, `shell.Notifications`, `shell.SystemTray`, `shell.Media` and the others are on the [Winshell](reference/winshell.md#shell) page, and each namespace has its own page.

Reading does not ask for permission and does not change anything. Wi-Fi scanning, notification access and Bluetooth pairing ask Windows only when the configuration calls the method, from a user's action. `shell.Session` can ask Windows to lock. Windows' own lock screen takes over.

`shell.NewHotkey()` is a hotkey that works anywhere in Windows. `Sequence` uses `Win`, `Ctrl`, `Alt` and `Shift`.

```csharp
        var hotkey = shell.NewHotkey();
        hotkey.Sequence = "Win+E";
        hotkey.Pressed += (_, _) => { };
```

`shell.Log` writes to the instance log, which `winshell log` prints.

## IPC

`shell.Ipc` is what `winshell ipc` calls. The handler's own types say what it takes and returns. `string`, `int`, `bool`, `double`, `float` and `Windows.UI.Color` cross the pipe. Nothing is evaluated.

```csharp
        shell.Ipc.Handle("notes", "hello", (string name) => "hi " + name);
```

`winshell ipc call notes hello Ada` prints `hi Ada`. `Property` and `Signal` are on [ShellIpc](reference/winshell.md#shellipc). Dispose the registration to take it away.

`winshell ipc call notice dismiss` closes Winshell's own notice. That call is not a target you register, so `ipc show` does not list it.

## Preferences

Preferences stay TOML. A subclass of `TomlFile` has one public read-write property per key. The key is the property name with a lowercase first letter. `Set` writes the field and the file. A comment on a key the file does not have yet comes from `TomlComment`.

```csharp
public sealed class ShellPreferences(string path, IEventLoop loop) : TomlFile(path, loop)
{
    [TomlComment("\"system\" follows Windows; or \"light\" or \"dark\".")]
    public string ColorScheme { get; set => Set(ref field, value); } = "system";
}
```

Pass `Path.Combine(shell.ConfigRoot, "preferences.toml")` and `shell.Loop`. The object follows hand edits. Unknown keys and comments stay. One that differs from a key the class reads only in case or by a letter or two is logged with its line and that key. A file that does not parse is not written. A file past 1 MiB, or nested past 64 levels, is not read. `Error` is `path[line:column]: message`, or empty.

`shell.DataPath`, `shell.StatePath` and `shell.CachePath` are per shell under `%LOCALAPPDATA%\winshell`. State does not survive a reload unless the configuration writes it itself. Every window is replaced.

## Reload

```csharp
        if (!shell.Reload())
            shell.Log.Warn("shell", "Reload did not start.");
```

That starts this program again with `--after` and quits. It does not build. `winshell reload` builds first. On failure the running instance stays and a notice shows. On success the new instance takes over. There is no file watcher. [`winshell dev`](#develop-a-shell) applies each edit as you save.

`winshell run --no-build` skips the build. The sign-in entry uses it, so signing in does not need the SDK and does not run a broken edit.

## Develop a shell

```powershell
winshell dev --path .
```

`winshell dev` builds your project, starts it, and applies each save to the running shell until you press Ctrl+C, which closes that shell and stops `dotnet watch`, as closing the terminal does. It needs the .NET SDK, and it runs a Debug build.

- **A C# edit** reaches the running shell in about a second. The first one takes a little longer, while the compiler warms up. An edited method runs its new code at its next call.
- **A window showing an edited type** gets a new one of its content, so a change to a constructor shows too. The window keeps its place, and every other window keeps its content and its state. A view whose constructor takes arguments stays, and its edited methods still run. Keep state that must survive in a model the view is given.
- **A XAML edit** goes into the panels of that class as you save, in a moment and with no build. The control stays the same object, so its C# fields keep their values, and its `x:Name` fields and event handlers point at the new elements. `{x:Bind}`, `x:Load` and `x:Phase` are the compiler's, and so is a file with no `x:Class`, so saving one of those builds and starts the shell again. A panel with no window open yet shows the new markup when it opens.
- **An edit .NET can't apply to running code**, such as a new base class, restarts the shell. That takes as long as a build, and the state of every window goes with it.
- **Code that doesn't compile** shows in a notice on screen, and the last version that did keeps running. The notice goes when the next save compiles. Before the first successful build there is no shell to show it, so the error is in the console.
- **A TOML or XAML file with an error** shows in the same notice, and what came from it before stays in use. Without `winshell dev` it goes to the log.

Desktop apps you start from the shell under `winshell dev` end, unsaved work and all, when `winshell dev` builds and starts the shell again or when it ends. `dotnet watch` runs the shell in a Windows job object, and what the shell starts joins that job. Run the shell with `winshell run` to keep them open.
