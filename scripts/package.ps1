# Builds the files a release ships into one folder, and checks them. See docs/development.md#releasing.
#
#   pwsh scripts/package.ps1                  publishes, packs and verifies both architectures into artifacts/
#   pwsh scripts/package.ps1 -Publish         only publishes, into artifacts/publish/<runtime>/
#   pwsh scripts/package.ps1 -Pack            only packs what artifacts/publish/ already has
#   pwsh scripts/package.ps1 -Verify          only checks what artifacts/ already has
#
# Publishing is a self-contained build of src/Winshell for each runtime, so no .NET install is needed.
# Packing makes winshell-<version>-<runtime>.zip for each, SHA256SUMS.txt, and the winget manifests in winget/<version>/
# and, zipped, in winget-manifests.zip. It also makes winshell-<version>.msixbundle for the Microsoft Store, unsigned,
# from packaging/msix/ and the identity -IdentityName, -Publisher and -PublisherDisplayName give it. That needs
# makeappx.exe from the Windows SDK. The same files pack to the same bytes: entries are in a fixed order and dated
# the day of the commit, which is also the manifests' ReleaseDate.
# Verifying checks the checksums, that each zip's binaries are for its architecture, that the x64 winshell.exe runs
# and reports the version, and that the manifests pass the validator wingetcreate runs before it submits them.
param(
    [switch] $Publish,
    [switch] $Pack,
    [switch] $Verify,
    [string] $Out = 'artifacts',
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtime = @('win-x64', 'win-arm64'),
    # Defaults to the release in .release-please-manifest.json.
    [string] $Version,
    # A release tag, such as v1.2.3: the script stops unless the checked-out version is that release.
    [string] $Tag,
    # The GitHub repository the release, and so the winget installer URLs, belong to.
    [string] $Repository = 'dante-sparras/winshell',
    # The package identity Partner Center reserved for the Store. Empty is a test identity.
    [string] $IdentityName,
    [string] $Publisher,
    [string] $PublisherDisplayName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
if (-not $Publish -and -not $Pack -and -not $Verify) { $Publish = $true; $Pack = $true; $Verify = $true }
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $root '.release-please-manifest.json') -Raw | ConvertFrom-Json).'.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be major.minor.patch, not '$Version'" }
if ($Tag -and $Tag -cne "v$Version") { throw "The checkout is version $Version, not the release $Tag." }
if (-not $IdentityName) { $IdentityName = 'Winshell.Test' }
if (-not $Publisher) { $Publisher = 'CN=Winshell Test' }
if (-not $PublisherDisplayName) { $PublisherDisplayName = 'Dante Sparrås' }
$Out = [IO.Path]::GetFullPath($Out, $root)
$publishRoot = Join-Path $Out 'publish'
$id = 'DanteSparras.Winshell'
# The newest manifest schema the pinned wingetcreate (v1.12.13.0) validates and submits. winget-pkgs merges it too.
$schema = '1.12.0'
$wingetDir = Join-Path $Out "winget/$Version"
# The day of the commit packed, so a later repair of a release packs the same bytes.
$day = (git -C $root log -1 --format=%cs)
if ($LASTEXITCODE -ne 0) { throw 'git could not read the date of the commit' }
$releaseDay = [datetime]::ParseExact($day.Trim(), 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# A folder this script deletes, which must be inside the given one.
function Assert-Inside([string] $path, [string] $parent) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith([IO.Path]::GetFullPath($parent).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$full is not inside $parent"
    }
    $full
}

# A zip of a folder whose bytes depend only on the files: entries sorted by path, each dated the release day.
function New-Zip([string] $dir, [string] $zip) {
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    $files = Get-ChildItem -LiteralPath $dir -File -Recurse |
        ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Name = [IO.Path]::GetRelativePath($dir, $_.FullName).Replace('\', '/') } } |
        Sort-Object -Property Name -CaseSensitive
    $stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files) {
                $entry = $archive.CreateEntry($file.Name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new($releaseDay, [TimeSpan]::Zero)
                $to = $entry.Open()
                try {
                    $from = [IO.File]::OpenRead($file.Path)
                    try { $from.CopyTo($to) } finally { $from.Dispose() }
                } finally { $to.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

# The Store package version of this release. The first part is one more than Winshell's, because the Store takes no 0
# there and always installs the highest version. The fourth part is the Store's.
function Get-PackageVersion {
    $parts = $Version.Split('.')
    "$([int] $parts[0] + 1).$($parts[1]).$($parts[2]).0"
}

# makeappx.exe from the newest Windows SDK.
function Get-MakeAppx {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $found = Get-ChildItem -LiteralPath $kits -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
        Sort-Object { [version] $_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $found) { throw "makeappx.exe is not in $kits. Install the Windows SDK." }
    $found
}

function Get-Sha256([string] $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }

if ($Publish) {
    foreach ($rid in $Runtime) {
        $dir = Assert-Inside (Join-Path $publishRoot $rid) $publishRoot
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        dotnet publish (Join-Path $root 'src/Winshell/Winshell.csproj') --nologo -c Release -r $rid --self-contained `
            "-p:Version=$Version" -o $dir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish for $rid failed with $LASTEXITCODE" }
        # SkiaSharp and HarfBuzz ship their native symbols, over 100 MB. Winshell's own symbols stay, for stack traces in logs.
        Get-ChildItem -LiteralPath $dir -Filter 'lib*.pdb' | Remove-Item -Force
    }
}

if ($Pack) {
    $sums = [ordered]@{}
    foreach ($rid in $Runtime) {
        $dir = Join-Path $publishRoot $rid
        if (-not (Test-Path -LiteralPath (Join-Path $dir 'winshell.exe'))) { throw "No winshell.exe in $dir. Run with -Publish first." }
        $zip = Join-Path $Out "winshell-$Version-$rid.zip"
        New-Zip $dir $zip
        $sums[(Split-Path -Leaf $zip)] = Get-Sha256 $zip
    }
    # The format sha256sum -c reads: the hash, two spaces, the file name.
    ($sums.GetEnumerator() | ForEach-Object { "$($_.Value)  $($_.Key)" }) -join "`n" |
        Set-Content -LiteralPath (Join-Path $Out 'SHA256SUMS.txt') -NoNewline

    # winget manifests, ready for wingetcreate submit: https://github.com/microsoft/winget-pkgs
    $wingetDir = Assert-Inside $wingetDir $Out
    if (Test-Path -LiteralPath $wingetDir) { Remove-Item -LiteralPath $wingetDir -Recurse -Force }
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
# winshell.exe loads the DLLs next to it, so winget puts its folder on PATH instead of linking the exe. The command is
# then the exe's own name, winshell.
ArchiveBinariesDependOnPath: true
InstallModes:
  - silent
UpgradeBehavior: uninstallPrevious
ReleaseDate: $($releaseDay.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture))
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
    New-Zip $wingetDir (Join-Path $Out 'winget-manifests.zip')

    # The Microsoft Store package: one .msix per architecture, bundled. The Store signs it.
    $makeappx = Get-MakeAppx
    $packages = Assert-Inside (Join-Path $Out 'msix') $Out
    if (Test-Path -LiteralPath $packages) { Remove-Item -LiteralPath $packages -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $packages | Out-Null
    $template = Get-Content -LiteralPath (Join-Path $root 'packaging/msix/AppxManifest.xml') -Raw
    foreach ($rid in $Runtime) {
        $layout = Assert-Inside (Join-Path $Out "msix-layout/$rid") $Out
        if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }
        Copy-Item -LiteralPath (Join-Path $publishRoot $rid) -Destination $layout -Recurse
        Copy-Item -LiteralPath (Join-Path $root 'packaging/msix/Assets') -Destination (Join-Path $layout 'Assets') -Recurse
        # Start runs winshellw.exe: the same apphost, marked as a Windows app so no console opens. It runs winshell.dll
        # as winshell.exe does. The SDK makes a WinExe's apphost the same way.
        $gui = [IO.File]::ReadAllBytes((Join-Path $layout 'winshell.exe'))
        $subsystem = [BitConverter]::ToInt32($gui, 0x3C) + 4 + 20 + 68
        if ([BitConverter]::ToUInt16($gui, $subsystem) -ne 3) { throw 'winshell.exe is not a console app' }
        $gui[$subsystem] = 2
        [IO.File]::WriteAllBytes((Join-Path $layout 'winshellw.exe'), $gui)
        $manifest = $template.Replace('$(IdentityName)', [Security.SecurityElement]::Escape($IdentityName)).
            Replace('$(Publisher)', [Security.SecurityElement]::Escape($Publisher)).
            Replace('$(PublisherDisplayName)', [Security.SecurityElement]::Escape($PublisherDisplayName)).
            Replace('$(PackageVersion)', (Get-PackageVersion)).
            Replace('$(Architecture)', ($rid -replace '^win-', ''))
        Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Value $manifest -NoNewline
        & $makeappx pack /o /h SHA256 /d $layout /p (Join-Path $packages "winshell-$Version-$rid.msix") | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "makeappx pack for $rid failed with $LASTEXITCODE" }
        Remove-Item -LiteralPath $layout -Recurse -Force
    }
    Remove-Item -LiteralPath (Join-Path $Out 'msix-layout') -Recurse -Force
    & $makeappx bundle /o /bv (Get-PackageVersion) /d $packages /p (Join-Path $Out "winshell-$Version.msixbundle") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed with $LASTEXITCODE" }
}

if ($Verify) {
    # SHA256SUMS.txt lists every zip, and each matches its line.
    $listed = @{}
    foreach ($line in (Get-Content -LiteralPath (Join-Path $Out 'SHA256SUMS.txt'))) {
        $hash, $name = $line -split '  ', 2
        $listed[$name] = $hash
    }
    $machines = @{ 'win-x64' = 0x8664; 'win-arm64' = 0xAA64 }
    $verifyRoot = Join-Path $Out 'verify'
    foreach ($rid in $Runtime) {
        $name = "winshell-$Version-$rid.zip"
        $zip = Join-Path $Out $name
        if (-not (Test-Path -LiteralPath $zip)) { throw "No $name in $Out" }
        if (-not $listed.ContainsKey($name) -or $listed[$name] -ne (Get-Sha256 $zip)) { throw "$name does not match its line in SHA256SUMS.txt" }
        $dir = Assert-Inside (Join-Path $verifyRoot $rid) $Out
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $dir)
        # The apphost, the runtime and the native drawing libraries are all built for this architecture.
        foreach ($binary in 'winshell.exe', 'hostfxr.dll', 'coreclr.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll') {
            $path = Join-Path $dir $binary
            if (-not (Test-Path -LiteralPath $path)) { throw "$name has no $binary at its root" }
            $bytes = [IO.File]::ReadAllBytes($path)
            $machine = [BitConverter]::ToUInt16($bytes, [BitConverter]::ToInt32($bytes, 0x3C) + 4)
            if ($machine -ne $machines[$rid]) { throw "$binary in $name is for machine 0x$($machine.ToString('X4')), not $rid" }
        }
        # arm64 runs only on arm64 hardware; this checks it on an x64 machine, as CI is.
        if ($rid -eq 'win-x64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64') {
            $reported = & (Join-Path $dir 'winshell.exe') --version
            if ($LASTEXITCODE -ne 0 -or $reported -notmatch "^winshell $([regex]::Escape($Version))(\+|$)") {
                throw "The x64 winshell.exe reported '$reported', not version $Version"
            }
            Write-Output "$name runs: $reported"
        }
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
    if (Test-Path -LiteralPath $verifyRoot) { Remove-Item -LiteralPath $verifyRoot -Recurse -Force }

    # The manifests carry the same hashes, and pass WinGetUtil.dll from Microsoft.WindowsPackageManager.Utils 1.12.350,
    # the validator the pinned wingetcreate v1.12.13.0 is built with and runs before it submits.
    $installer = Get-Content -LiteralPath (Join-Path $wingetDir "$id.installer.yaml") -Raw
    foreach ($rid in $Runtime) {
        if ($installer -notmatch "(?m)^\s+InstallerSha256: $($listed["winshell-$Version-$rid.zip"].ToUpperInvariant())\r?$") {
            throw "The installer manifest does not carry the hash of the $rid zip"
        }
    }
    $tools = Join-Path $Out 'tools'
    $utils = Join-Path $tools 'microsoft.windowspackagemanager.utils.1.12.350.nupkg'
    if (-not (Test-Path -LiteralPath $utils)) {
        New-Item -ItemType Directory -Force -Path $tools | Out-Null
        Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/microsoft.windowspackagemanager.utils/1.12.350/microsoft.windowspackagemanager.utils.1.12.350.nupkg' -OutFile $utils
    }
    if ((Get-Sha256 $utils) -ne 'cb7786683a7ce395467850efabba506203889f9c71b395d0d9a26753410eaa58') {
        Remove-Item -LiteralPath $utils
        throw 'The WinGetUtil package does not have the pinned hash'
    }
    $arch = if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
    $dll = Join-Path $tools "$arch/WinGetUtil.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $dll) | Out-Null
        $package = [IO.Compression.ZipFile]::OpenRead($utils)
        try {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($package.GetEntry("runtimes/$arch/native/WinGetUtil.dll"), $dll)
        } finally { $package.Dispose() }
    }
    # Loaded by its path first, so the DllImport by name finds this one.
    [Runtime.InteropServices.NativeLibrary]::Load($dll) | Out-Null
    if (-not ('WinshellPackage.WinGetUtil' -as [type])) {
        Add-Type -Namespace WinshellPackage -Name WinGetUtil -MemberDefinition @'
[DllImport("WinGetUtil.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
public static extern IntPtr WinGetValidateManifest(string manifestPath, [MarshalAs(UnmanagedType.U1)] out bool succeeded, [MarshalAs(UnmanagedType.BStr)] out string message);
'@
    }
    $ok = $false
    $message = ''
    [WinshellPackage.WinGetUtil]::WinGetValidateManifest($wingetDir, [ref] $ok, [ref] $message) | Out-Null
    if (-not $ok) { throw "The winget manifests do not validate: $message" }
    if ($message) { Write-Warning "winget manifest validation: $message" }

    # The Store package: each architecture's has this identity and version, Start's winshellw.exe opens no console and
    # the winshell command is a console app.
    $makeappx = Get-MakeAppx
    if (-not (Test-Path -LiteralPath (Join-Path $Out "winshell-$Version.msixbundle"))) { throw "No winshell-$Version.msixbundle in $Out" }
    foreach ($rid in $Runtime) {
        $dir = Assert-Inside (Join-Path $verifyRoot "msix-$rid") $Out
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
        & $makeappx unpack /o /p (Join-Path $Out "msix/winshell-$Version-$rid.msix") /d $dir | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "makeappx unpack for $rid failed with $LASTEXITCODE" }
        $identity = ([xml] (Get-Content -LiteralPath (Join-Path $dir 'AppxManifest.xml') -Raw)).Package.Identity
        $expected = "$IdentityName|$Publisher|$(Get-PackageVersion)|$($rid -replace '^win-', '')"
        $actual = "$($identity.Name)|$($identity.Publisher)|$($identity.Version)|$($identity.ProcessorArchitecture)"
        if ($actual -cne $expected) { throw "The $rid package's identity is $actual, not $expected" }
        foreach ($pair in @(@('winshell.exe', 3), @('winshellw.exe', 2))) {
            $bytes = [IO.File]::ReadAllBytes((Join-Path $dir $pair[0]))
            $subsystem = [BitConverter]::ToUInt16($bytes, [BitConverter]::ToInt32($bytes, 0x3C) + 4 + 20 + 68)
            if ($subsystem -ne $pair[1]) { throw "$($pair[0]) in the $rid package has subsystem $subsystem, not $($pair[1])" }
        }
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
    if (Test-Path -LiteralPath $verifyRoot) { Remove-Item -LiteralPath $verifyRoot -Recurse -Force }
    Write-Output "Verified $($Runtime -join ' and ') $Version, its winget manifests and its Store package $(Get-PackageVersion)."
}
