# Builds the files a release ships into one folder. See docs/development.md#releasing.
#
#   pwsh scripts/package.ps1                  publishes and packs both architectures into artifacts/
#   pwsh scripts/package.ps1 -Publish         only publishes, into artifacts/publish/<runtime>/
#   pwsh scripts/package.ps1 -Pack            only packs what artifacts/publish/ already has
#
# Publishing is a self-contained build of src/Winshell for each runtime, so no .NET install is needed.
# Packing makes winshell-<version>-<runtime>.zip for each, SHA256SUMS.txt, and the winget manifests in winget/.
param(
    [switch] $Publish,
    [switch] $Pack,
    [string] $Out = 'artifacts',
    [string[]] $Runtime = @('win-x64', 'win-arm64'),
    # Defaults to the release in .release-please-manifest.json.
    [string] $Version,
    # The GitHub repository the release, and so the winget installer URLs, belong to.
    [string] $Repository = 'dante-sparras/winshell'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
if (-not $Publish -and -not $Pack) { $Publish = $true; $Pack = $true }
if (-not $Version) {
    $Version = (Get-Content (Join-Path $root '.release-please-manifest.json') -Raw | ConvertFrom-Json).'.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be major.minor.patch, not '$Version'" }
$Out = [IO.Path]::GetFullPath($Out, $root)
$publishRoot = Join-Path $Out 'publish'

if ($Publish) {
    foreach ($rid in $Runtime) {
        $dir = Join-Path $publishRoot $rid
        if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        dotnet publish (Join-Path $root 'src/Winshell/Winshell.csproj') --nologo -c Release -r $rid --self-contained `
            "-p:Version=$Version" -o $dir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish for $rid failed with $LASTEXITCODE" }
        # SkiaSharp and HarfBuzz ship their native symbols, over 100 MB. Winshell's own symbols stay, for stack traces in logs.
        Get-ChildItem -LiteralPath $dir -Filter 'lib*.pdb' | Remove-Item -Force
    }
}

if ($Pack) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sums = [ordered]@{}
    foreach ($rid in $Runtime) {
        $dir = Join-Path $publishRoot $rid
        if (-not (Test-Path (Join-Path $dir 'winshell.exe'))) { throw "No winshell.exe in $dir. Run with -Publish first." }
        $zip = Join-Path $Out "winshell-$Version-$rid.zip"
        if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
        [IO.Compression.ZipFile]::CreateFromDirectory($dir, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
        $sums[(Split-Path -Leaf $zip)] = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    # The format sha256sum -c reads: the hash, two spaces, the file name.
    ($sums.GetEnumerator() | ForEach-Object { "$($_.Value)  $($_.Key)" }) -join "`n" |
        Set-Content -LiteralPath (Join-Path $Out 'SHA256SUMS.txt') -NoNewline

    # winget manifests, ready for wingetcreate submit: https://github.com/microsoft/winget-pkgs
    $id = 'DanteSparras.Winshell'
    $schema = '1.10.0'
    $wingetDir = Join-Path $Out "winget/$Version"
    if (Test-Path $wingetDir) { Remove-Item -LiteralPath $wingetDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $wingetDir | Out-Null
    $header = '# Created by scripts/package.ps1 in https://github.com/' + $Repository
    $installers = foreach ($rid in $Runtime) {
        $name = "winshell-$Version-$rid.zip"
        @"
  - Architecture: $($rid -replace '^win-', '')
    InstallerUrl: https://github.com/$Repository/releases/download/v$Version/$name
    InstallerSha256: $($sums[$name].ToUpperInvariant())
"@
    }
    @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content -LiteralPath (Join-Path $wingetDir "$id.yaml")
    @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $Version
MinimumOSVersion: 10.0.22000.0
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: winshell.exe
    PortableCommandAlias: winshell
# winshell.exe loads the DLLs next to it, so winget puts its folder on PATH instead of linking the exe.
ArchiveBinariesDependOnPath: true
InstallModes:
  - silent
UpgradeBehavior: uninstallPrevious
ReleaseDate: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'))
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content -LiteralPath (Join-Path $wingetDir "$id.installer.yaml")
    @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json

PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Dante Sparrås
PublisherUrl: https://github.com/dante-sparras
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: Winshell
PackageUrl: https://github.com/$Repository
License: MIT
LicenseUrl: https://github.com/$Repository/blob/main/LICENSE
Copyright: Copyright (c) Dante Sparrås
ShortDescription: A desktop shell for Windows 11, described in QML.
Description: |-
  Winshell runs a Windows 11 taskbar, Start, quick settings, a notification center and on-screen displays out of the box.
  Every part of it is a QML file you can change, or replace with a shell of your own.
Moniker: winshell
Tags:
  - desktop
  - qml
  - shell
  - taskbar
  - windows-11
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content -LiteralPath (Join-Path $wingetDir "$id.locale.en-US.yaml")
}
