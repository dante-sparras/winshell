# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Install

Winshell runs on Windows 11.

- **winget:** `winget install DanteSparras.Winshell`, once winget has accepted the package, then run `winshell`.
- **Portable:** download `winshell-<version>-win-x64.zip` (or `win-arm64`) from the [latest release](https://github.com/dante-sparras/winshell/releases/latest), unzip it anywhere and run `winshell.exe`. `SHA256SUMS.txt` on the release has each file's hash.

Both are self-contained: no .NET install is needed. Both update themselves from GitHub Releases, unless you turn that off in Winshell Settings > About. See [Winshell updates](docs/updates.md). Releases will be signed through SignPath Foundation once its project for Winshell is approved; see the [Code signing policy](docs/code-signing-policy.md).

## Documentation

Start with [Getting started](docs/guide/getting-started.md). The [docs](docs/index.md) also have a configuration guide, troubleshooting, and a reference for every module. To build Winshell from source, see the [development runbook](docs/development.md).

Coming from Quickshell? See [Migrating from Quickshell](docs/migrating-from-quickshell.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening an issue or PR.

## Privacy

Winshell collects nothing. See [Privacy](docs/privacy.md).

## License

[MIT](LICENSE)
