# Winshell

A desktop shell toolkit for Windows 11. A configuration written in QML describes the shell.

## Install

Winshell runs on Windows 11. [Getting started](docs/guide/getting-started.md) describes the same options. The portable zip and the PowerShell command install the latest release. winget and the Microsoft Store are not available yet. Building from source is for working on Winshell.

### Portable zip

A release's [GitHub release page](https://github.com/dante-sparras/winshell/releases) carries a self-contained build, which needs no .NET install: `winshell-<version>-win-x64.zip` and `winshell-<version>-win-arm64.zip`, with `SHA256SUMS.txt` for their hashes. Unzip one into a folder of its own and run `winshell.exe`. That build updates itself from GitHub Releases. A build from source does not. See [Winshell updates](docs/updates.md).

Releases will be signed through SignPath Foundation once its project for Winshell is approved. Until then a published zip is unsigned. See the [Code signing policy](docs/code-signing-policy.md).

### Install with PowerShell

No administrator account. This downloads the portable build for the current user. It needs `install.ps1` and the zip on that release.

```powershell
irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
```

`iex` cannot take parameters. Set these first, or run the downloaded script as a script block:

```powershell
$env:WINSHELL_VERSION = 'x.y.z'  # optional. Omit it for the latest release.
$env:WINSHELL_AUTOSTART = '1'    # optional. Run Winshell at sign-in.
irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1 | iex
```

```powershell
& ([scriptblock]::Create((irm https://github.com/dante-sparras/winshell/releases/latest/download/install.ps1))) -Version x.y.z -AutoStart
```

The installer checks the zip against `SHA256SUMS.txt` on the release and stops if the hash does not match. It installs into `%LOCALAPPDATA%\Programs\winshell`, adds the `bin` folder to your user PATH, and creates a Start menu shortcut. It will not replace an existing install while that Winshell is running. The files are the portable build, so Winshell updates itself the same way.

Remove that install with:

```powershell
irm https://github.com/dante-sparras/winshell/releases/latest/download/uninstall.ps1 | iex
```

That removes the install folder, the PATH entry, the shortcut, and the sign-in entry. It leaves your configuration in `%APPDATA%\winshell`.

### winget, coming

`winget install DanteSparras.Winshell` once winget has accepted the package. It is not available yet. It will install the same portable build.

### Microsoft Store, coming

The Store package is built for Partner Center and is not published. Publishing waits on that setup, so there is nothing to install from the Store. The Store would sign that package and update it.

### Build from source

You need Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
git clone https://github.com/dante-sparras/winshell.git
cd winshell
dotnet publish src\Winshell -c Release -o "$env:LOCALAPPDATA\winshell-dev"
```

That puts `winshell.exe` in `%LOCALAPPDATA%\winshell-dev`, away from the folder the PowerShell installer uses. Add that folder to your `PATH` to run `winshell` from anywhere. The build uses the .NET runtime the SDK installed, and it does not update itself. [Getting started](docs/guide/getting-started.md) covers the first run.

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
