# What a release ships and how its build is checked, for scripts/package.ps1, scripts/publish-release.ps1 and
# .github/workflows/package.yml. install.ps1 is fetched alone and can't dot-source this; Install.Tests.ps1 serves it a
# release named by Get-ReleaseZipName.

# The release in .release-please-manifest.json.
function Get-ReleaseVersion {
    $manifest = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.release-please-manifest.json'
    (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).'.'
}

function Get-ReleaseZipName([string] $Version, [string] $Runtime) { "winshell-$Version-$Runtime.zip" }

# Every file a release uploads. A local build of fewer runtimes has fewer zips.
function Get-ReleaseAssetName([string] $Version, [string[]] $Runtime = @('win-x64', 'win-arm64')) {
    @($Runtime | ForEach-Object { Get-ReleaseZipName $Version $_ }) +
    @('SHA256SUMS.txt', 'winget-manifests.zip', 'install.ps1', 'uninstall.ps1')
}

# A tool from the newest Windows SDK that has it, such as makeappx.exe or signtool.exe.
function Get-WindowsSdkTool([string] $Name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $found = Get-ChildItem -LiteralPath $kits -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
        Sort-Object { [version] $_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64/$Name" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $found) { throw "$Name is not in $kits. Install the Windows SDK." }
    $found
}

# Runs winshell.exe --version and returns what it printed, or stops unless that is $Version, with or without build
# metadata after a +.
function Assert-WinshellVersion([string] $Exe, [string] $Version) {
    $reported = & $Exe --version
    if ($LASTEXITCODE -ne 0 -or $reported -notmatch "^winshell $([regex]::Escape($Version))(\+|$)") {
        throw "$Exe reported '$reported', not version $Version"
    }
    $reported
}
