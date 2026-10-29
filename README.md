# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Install

Winshell runs on Windows 11.

- **winget:** `winget install DanteSparras.Winshell`, once winget has accepted the package, then run `winshell`.
- **Portable:** download `winshell-<version>-win-x64.zip` (or `win-arm64`) from the [latest release](https://github.com/dante-sparras/winshell/releases/latest), unzip it anywhere and run `winshell.exe`. `SHA256SUMS.txt` on the release has each file's hash.

Both are self-contained: no .NET install is needed. Both update themselves from GitHub Releases, unless you turn that off in Winshell Settings > About. See [Winshell updates](docs/updates.md).

## Getting started

See the [development runbook](docs/development.md) for prerequisites, first checkout, and commands.

Coming from Quickshell? See [Migrating from Quickshell](docs/migrating-from-quickshell.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening an issue or PR.

## Privacy

Winshell collects nothing. See [Privacy](docs/privacy.md).

## License

[MIT](LICENSE)
