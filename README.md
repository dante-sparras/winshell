# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Install

Winshell runs on Windows 11. Build it from source. You need Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
git clone https://github.com/dante-sparras/winshell.git
cd winshell
dotnet publish src\Winshell -c Release -o "$env:LOCALAPPDATA\Programs\winshell"
```

That puts `winshell.exe` in `%LOCALAPPDATA%\Programs\winshell`. Add that folder to your `PATH` to run `winshell` from anywhere. The build uses the .NET runtime the SDK installed. [Getting started](docs/guide/getting-started.md) covers the first run.

> [!NOTE]
> 0.5.0, the current release, has no download. The release after it will carry a self-contained build, which needs no .NET install, on its [GitHub release page](https://github.com/dante-sparras/winshell/releases): `winshell-<version>-win-x64.zip` and `winshell-<version>-win-arm64.zip`, with `SHA256SUMS.txt` for their hashes. Unzip one into a folder of its own and run `winshell.exe`. A winget package, `winget install DanteSparras.Winshell`, follows once winget has accepted it. Those builds update themselves from GitHub Releases. A build from source does not. See [Winshell updates](docs/updates.md). Releases will be signed through SignPath Foundation once its project for Winshell is approved; see the [Code signing policy](docs/code-signing-policy.md).

### Install with PowerShell

No administrator account. Once that release exists, this downloads its portable build for the current user.

```powershell
irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
```

`iex` cannot take parameters. Set these first, or run the downloaded script as a script block:

```powershell
$env:WINSHELL_VERSION = '0.6.0'  # optional. x.y.z. Omit it for the latest release.
$env:WINSHELL_AUTOSTART = '1'    # optional. Run Winshell at sign-in.
irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
```

```powershell
& ([scriptblock]::Create((irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1))) -Version 0.6.0 -AutoStart
```

The installer checks the zip against `SHA256SUMS.txt` on the release and stops if the hash does not match. It installs into `%LOCALAPPDATA%\Programs\winshell`, adds the `bin` folder to your user PATH, and creates a Start menu shortcut. It will not replace an existing install while that Winshell is running. The files are the portable build, so Winshell updates itself the same way.

Remove that install with:

```powershell
irm https://github.com/dante-sparras/winshell/releases/latest/download/uninstall.ps1 | iex
```

That removes the install folder, the PATH entry, the shortcut, and the sign-in entry. It leaves your configuration in `%APPDATA%\winshell`.

## Documentation

Start with [Getting started](docs/guide/getting-started.md). The [docs](docs/index.md) also have a configuration guide, troubleshooting, and a reference for every module. To build Winshell from source, see the [development runbook](docs/development.md).

Coming from Quickshell? See [Migrating from Quickshell](docs/migrating-from-quickshell.md).

Something not working? Run `winshell doctor`. It checks this PC's setup and says how to fix what's wrong. See [winshell doctor](docs/doctor.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening an issue or PR.

## Privacy

Winshell collects nothing. See [Privacy](docs/privacy.md).

## License

[MIT](LICENSE)
