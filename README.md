# Winshell

A shell toolkit for Windows 11 that matches Quickshell. It draws a Bar on every monitor, Panels, Toasts, and a Pill, and plugins fill them with the Kit's controls. It ships no widgets of its own. It runs on [Winland](https://github.com/dante-sparras/winland), and [Winoma](https://github.com/dante-sparras/winoma) is the desktop it loads.

- `Winshell.Host.exe` is the shell. `winshell` asks it, the way `qs ipc` asks Quickshell: [docs/user/cli.md](docs/user/cli.md).
- Settings are in `%USERPROFILE%\.config\winshell`: [docs/user/config.md](docs/user/config.md).
- Decisions are in [docs/adr](docs/adr/README.md), and the words in [CONTEXT.md](CONTEXT.md).

Winoma's install puts Winshell in `%LOCALAPPDATA%\Winshell`. Each release here is `Winshell-win-x64.zip`, and Winshell.Core and Winshell.Kit are packages on GitHub Packages.
