# Winshell

A desktop shell toolkit for Windows 11. Use the built-in taskbar, Start menu, quick settings and notification center, or write your own shell with C# and WinUI 3.

Running the built-in shell needs no .NET SDK. Writing a shell does.

## Install with PowerShell

```powershell
irm https://github.com/enpaze/winshell/releases/latest/download/install.ps1 | iex
winshell run -d
```

Installs for your user, without administrator rights. Or download the x64 or arm64 zip from [Releases](https://github.com/enpaze/winshell/releases), extract it and run `winshellw.exe`.

Releases are currently unsigned. Custom configurations may break between releases until 1.0.

## Get started

Right-click the taskbar and choose **Taskbar settings** to customize Winshell or enable startup at sign-in. Explorer's taskbar stays visible by default; see [taskbar setup](docs/reference/default-shell.md#taskbar).

Stop Winshell with `winshell kill`. If something goes wrong, run `winshell doctor` or read [troubleshooting](docs/troubleshooting.md).

To build a shell of your own, start with the [configuration guide](docs/configuration.md).

[Commands](docs/reference/command-line.md) · [API reference](docs/reference/index.md) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE)
